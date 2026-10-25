using System.Collections.Generic;
using UnityEngine;
using VikingAI.Core;

namespace VikingAI.Navigation
{
    public sealed class NavSector
    {
        public const int Resolution = 32; // 32x32 cells at 1.0m per cell = 32m x 32m tile
        public const float CellSize = 1.0f;

        public readonly Vector2Int SectorCoord;
        public readonly Vector3 OriginWorld;
        public readonly NavCell[] Layer0 = new NavCell[Resolution * Resolution];
        public readonly NavCell[] Layer1 = new NavCell[Resolution * Resolution];
        public readonly bool[] HasLayer1 = new bool[Resolution * Resolution];
        public float LastBuildTime;
        public float LastAccessTime;
        public bool IsDirty = true;

        public NavSector(Vector2Int coord)
        {
            SectorCoord = coord;
            OriginWorld = new Vector3((coord.x - 0.5f) * Resolution * CellSize, 0f, (coord.y - 0.5f) * Resolution * CellSize);
        }

        public static int GetIndex(int lx, int lz) => (lz * Resolution) + lx;

        public Vector3 GetCellWorldPosition(int lx, int lz, int layer)
        {
            int idx = GetIndex(lx, lz);
            float y = layer == 1 && HasLayer1[idx] ? Layer1[idx].FloorY : Layer0[idx].FloorY;
            return new Vector3(
                OriginWorld.x + (lx + 0.5f) * CellSize,
                y,
                OriginWorld.z + (lz + 0.5f) * CellSize);
        }
    }

    /// <summary>
    /// Builds and incrementally updates 32x32m 2.5D navigation sectors with 2-pass Chamfer clearance radii,
    /// doorway detection, fire hazard tagging, and structural HP breach metadata.
    /// </summary>
    public static class NavSectorBuilder
    {
        private static readonly RaycastHit[] _rayHits = new RaycastHit[16];
        private static readonly List<Door> _nearbyDoors = new List<Door>(16);
        private static readonly List<WearNTear> _nearbyPieces = new List<WearNTear>(64);
        private static readonly List<Fireplace> _nearbyFires = new List<Fireplace>(16);

        public static void BuildSector(NavSector sector)
        {
            sector.LastBuildTime = Time.time;
            sector.IsDirty = false;

            int solidMask = Pathfinding.instance != null
                ? Pathfinding.instance.m_layers.value
                : LayerMask.GetMask("terrain", "Default", "static_solid", "piece");

            float waterLevel = 30.0f;
            Vector3 sectorCenter = new Vector3(
                sector.OriginWorld.x + 16.0f,
                0f,
                sector.OriginWorld.z + 16.0f);

            _nearbyDoors.Clear();
            PieceRegistry.QueryDoorsInRadius(sectorCenter, 26.0f, _nearbyDoors);

            _nearbyPieces.Clear();
            PieceRegistry.QueryWearNTearInRadius(sectorCenter, 26.0f, _nearbyPieces);

            _nearbyFires.Clear();
            PieceRegistry.QuerySetInRadius(
                (HashSet<Fireplace>)PieceRegistry.Fireplaces,
                sectorCenter,
                26.0f,
                _nearbyFires);

            for (int lz = 0; lz < NavSector.Resolution; lz++)
            {
                float wz = sector.OriginWorld.z + (lz + 0.5f) * NavSector.CellSize;
                for (int lx = 0; lx < NavSector.Resolution; lx++)
                {
                    float wx = sector.OriginWorld.x + (lx + 0.5f) * NavSector.CellSize;
                    int idx = NavSector.GetIndex(lx, lz);
                    sector.HasLayer1[idx] = false;

                    float groundY = 0f;
                    if (ZoneSystem.instance != null)
                    {
                        ZoneSystem.instance.GetGroundHeight(new Vector3(wx, 500f, wz), out groundY);
                    }

                    Vector3 rayStart = new Vector3(wx, groundY + 40.0f, wz);
                    int hitCount = Physics.RaycastNonAlloc(rayStart, Vector3.down, _rayHits, 60.0f, solidMask);

                    float lowestY = groundY;
                    float upperY = float.MinValue;
                    Vector3 lowestNormal = Vector3.up;
                    Collider? hitCollider = null;

                    for (int h = 0; h < hitCount; h++)
                    {
                        RaycastHit hit = _rayHits[h];
                        if (hit.point.y <= groundY + 0.4f)
                        {
                            if (hit.point.y >= lowestY - 0.2f)
                            {
                                lowestY = hit.point.y;
                                lowestNormal = hit.normal;
                                hitCollider = hit.collider;
                            }
                        }
                        else if (hit.point.y > lowestY + 1.8f && hit.normal.y > 0.5f)
                        {
                            if (hit.point.y > upperY)
                            {
                                upperY = hit.point.y;
                            }
                        }
                    }

                    NavSurfaceFlags flags0 = NavSurfaceFlags.Walkable;
                    if (lowestY < waterLevel - 1.2f)
                    {
                        flags0 |= NavSurfaceFlags.Water;
                        lowestY = waterLevel - 0.5f;
                    }

                    if (lowestNormal.y < 0.65f)
                    {
                        flags0 |= NavSurfaceFlags.SteepSlope;
                    }

                    ushort structuralHp = 0;
                    Vector3 cellPos = new Vector3(wx, lowestY, wz);

                    // Check doorway proximity
                    for (int d = 0; d < _nearbyDoors.Count; d++)
                    {
                        Door door = _nearbyDoors[d];
                        if (door != null && (door.transform.position - cellPos).sqrMagnitude <= 1.44f)
                        {
                            flags0 |= NavSurfaceFlags.Doorway | NavSurfaceFlags.Walkable;
                            break;
                        }
                    }

                    // Check fire hazard proximity
                    for (int f = 0; f < _nearbyFires.Count; f++)
                    {
                        Fireplace fp = _nearbyFires[f];
                        if (fp != null && fp.IsBurning() && (fp.transform.position - cellPos).sqrMagnitude <= 1.6f)
                        {
                            flags0 |= NavSurfaceFlags.Hazard;
                            break;
                        }
                    }

                    // Check blocking wall / destructible piece if not a doorway
                    if ((flags0 & NavSurfaceFlags.Doorway) == 0 && hitCollider != null)
                    {
                        WearNTear wnt = hitCollider.GetComponentInParent<WearNTear>();
                        if (wnt != null && hitCollider.bounds.size.y > 1.3f && lowestY > groundY + 1.1f)
                        {
                            flags0 |= NavSurfaceFlags.Destructible | NavSurfaceFlags.Structure;
                            structuralHp = (ushort)Mathf.Clamp(wnt.GetHealthPercentage() * wnt.m_health, 10f, 60000f);
                        }
                    }

                    float ceiling0 = upperY > lowestY ? (upperY - lowestY) : 20.0f;

                    sector.Layer0[idx] = new NavCell
                    {
                        FloorY = lowestY,
                        CeilingClearance = ceiling0,
                        TrueClearanceRadius = 4.0f,
                        StructuralHp = structuralHp,
                        Flags = flags0
                    };

                    if (upperY > float.MinValue)
                    {
                        sector.HasLayer1[idx] = true;
                        sector.Layer1[idx] = new NavCell
                        {
                            FloorY = upperY,
                            CeilingClearance = 20.0f,
                            TrueClearanceRadius = 2.0f,
                            StructuralHp = 0,
                            Flags = NavSurfaceFlags.Walkable | NavSurfaceFlags.Structure
                        };
                    }
                }
            }

            ComputeChamferClearance(sector.Layer0);
        }

        private static void ComputeChamferClearance(NavCell[] cells)
        {
            const int N = NavSector.Resolution;

            // Initialize obstacle boundaries
            for (int z = 0; z < N; z++)
            {
                for (int x = 0; x < N; x++)
                {
                    int idx = NavSector.GetIndex(x, z);
                    ref NavCell c = ref cells[idx];
                    if (!c.IsWalkable || (c.IsDestructible && !c.IsDoorway))
                    {
                        c.TrueClearanceRadius = 0f;
                    }
                    else
                    {
                        c.TrueClearanceRadius = 8.0f;
                    }
                }
            }

            // Forward pass
            for (int z = 1; z < N - 1; z++)
            {
                for (int x = 1; x < N - 1; x++)
                {
                    int idx = NavSector.GetIndex(x, z);
                    ref NavCell c = ref cells[idx];
                    if (c.TrueClearanceRadius <= 0f) continue;

                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x - 1, z)], 1.0f);
                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x, z - 1)], 1.0f);
                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x - 1, z - 1)], 1.414f);
                }
            }

            // Backward pass
            for (int z = N - 2; z >= 1; z--)
            {
                for (int x = N - 2; x >= 1; x--)
                {
                    int idx = NavSector.GetIndex(x, z);
                    ref NavCell c = ref cells[idx];
                    if (c.TrueClearanceRadius <= 0f) continue;

                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x + 1, z)], 1.0f);
                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x, z + 1)], 1.0f);
                    CheckNeighborStep(ref c, in cells[NavSector.GetIndex(x + 1, z + 1)], 1.414f);
                }
            }
        }

        private static void CheckNeighborStep(ref NavCell current, in NavCell neighbor, float dist)
        {
            if (Mathf.Abs(current.FloorY - neighbor.FloorY) > 1.6f && !neighbor.IsDoorway)
            {
                current.TrueClearanceRadius = Mathf.Min(current.TrueClearanceRadius, dist);
            }
            else
            {
                current.TrueClearanceRadius = Mathf.Min(current.TrueClearanceRadius, neighbor.TrueClearanceRadius + dist);
            }
        }
    }
}
