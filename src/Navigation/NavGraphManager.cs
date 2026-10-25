using System.Collections.Generic;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Profiles;
using VikingAI.Telemetry;

namespace VikingAI.Navigation
{
    /// <summary>
    /// Manages the active 32x32m 2.5D navigation sectors, registered INavTraversalRule contracts,
    /// and fallback/enhanced A* path queries when doors, ledges, or structural breaches are involved.
    /// </summary>
    public static class NavGraphManager
    {
        private static readonly Dictionary<Vector2Int, NavSector> _sectors = new Dictionary<Vector2Int, NavSector>(64);
        private static readonly List<INavTraversalRule> _rules = new List<INavTraversalRule>
        {
            new GroundWalkRule(),
            new WaterSwimRule(),
            new DoorTraversalRule(),
            new LedgeJumpRule(),
            new SiegeBreachRule()
        };

        public static Character? CurrentPathingCharacter { get; set; }

        public static void RegisterTraversalRule(INavTraversalRule rule)
        {
            if (rule == null) return;
            _rules.RemoveAll(r => r.Id == rule.Id);
            _rules.Add(rule);
        }

        public static void InvalidatePosition(Vector3 worldPos)
        {
            Vector2Int coord = WorldToSectorCoord(worldPos);
            if (_sectors.TryGetValue(coord, out NavSector sector))
            {
                sector.IsDirty = true;
            }
        }

        public static bool TrySolvePath(
            Vector3 from,
            Vector3 to,
            Pathfinding.AgentType agentType,
            List<Vector3>? outPath)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableUnifiedNavGraph.Value)
            {
                return false;
            }

            Character? agent = CurrentPathingCharacter;
            AgentBehaviorProfile profile = agent != null
                ? AgentProfileRegistry.GetProfile(agent)
                : new AgentBehaviorProfile
                {
                    CanOpenDoors = false,
                    MaxJumpUpHeight = ModConfig.VanillaEnemiesJumpLedges.Value ? 1.4f : 0f,
                    MaxDropDownHeight = ModConfig.VanillaEnemiesJumpLedges.Value ? 2.5f : 0f,
                    AllowSiegeBreachPathing = ModConfig.VanillaEnemiesBreachWeakWalls.Value
                };

            // Only invoke the custom 2.5D clearance A* solver when the agent has custom traversal rules enabled
            // or when custom traversal rules have been registered by downstream mods.
            bool hasExtendedCapabilities =
                profile.CanOpenDoors ||
                profile.MaxJumpUpHeight > 0.01f ||
                profile.MaxDropDownHeight > 0.01f ||
                profile.AllowSiegeBreachPathing ||
                _rules.Count > 5;

            if (!hasExtendedCapabilities)
            {
                return false;
            }

            GetAgentDimensions(agentType, out float radius, out float height);
            return ClearanceAStarSolver.TryFindPath(
                from,
                to,
                agent,
                profile,
                radius,
                height,
                _rules,
                outPath);
        }

        public static NavSector GetOrBuildSector(Vector2Int sectorCoord)
        {
            float now = Time.time;
            if (!_sectors.TryGetValue(sectorCoord, out NavSector sector))
            {
                sector = new NavSector(sectorCoord);
                _sectors[sectorCoord] = sector;
            }

            sector.LastAccessTime = now;
            if (sector.IsDirty || now - sector.LastBuildTime > 12.0f)
            {
                NavSectorBuilder.BuildSector(sector);
            }

            return sector;
        }

        public static bool TryGetCell(int globalX, int globalZ, int layer, out NavCell cell)
        {
            GlobalCellToSectorAndLocal(globalX, globalZ, out Vector2Int secCoord, out int lx, out int lz);
            NavSector sector = GetOrBuildSector(secCoord);
            int idx = NavSector.GetIndex(lx, lz);

            if (layer == 1)
            {
                if (!sector.HasLayer1[idx])
                {
                    cell = default;
                    return false;
                }
                cell = sector.Layer1[idx];
                return true;
            }

            cell = sector.Layer0[idx];
            return true;
        }

        public static int ResolveBestLayer(int globalX, int globalZ, float worldY)
        {
            GlobalCellToSectorAndLocal(globalX, globalZ, out Vector2Int secCoord, out int lx, out int lz);
            NavSector sector = GetOrBuildSector(secCoord);
            int idx = NavSector.GetIndex(lx, lz);

            if (sector.HasLayer1[idx])
            {
                float d0 = Mathf.Abs(sector.Layer0[idx].FloorY - worldY);
                float d1 = Mathf.Abs(sector.Layer1[idx].FloorY - worldY);
                if (d1 < d0) return 1;
            }

            return 0;
        }

        public static void WorldToGlobalCell(Vector3 worldPos, out int gx, out int gz)
        {
            gx = Mathf.FloorToInt(worldPos.x / NavSector.CellSize);
            gz = Mathf.FloorToInt(worldPos.z / NavSector.CellSize);
        }

        public static Vector3 GlobalCellToWorld(int gx, int gz, int layer)
        {
            GlobalCellToSectorAndLocal(gx, gz, out Vector2Int secCoord, out int lx, out int lz);
            NavSector sector = GetOrBuildSector(secCoord);
            return sector.GetCellWorldPosition(lx, lz, layer);
        }

        public static Vector2Int WorldToSectorCoord(Vector3 worldPos)
        {
            const float tileSpan = NavSector.Resolution * NavSector.CellSize;
            int sx = Mathf.FloorToInt((worldPos.x + tileSpan * 0.5f) / tileSpan);
            int sz = Mathf.FloorToInt((worldPos.z + tileSpan * 0.5f) / tileSpan);
            return new Vector2Int(sx, sz);
        }

        private static void GlobalCellToSectorAndLocal(int gx, int gz, out Vector2Int secCoord, out int lx, out int lz)
        {
            const int N = NavSector.Resolution;
            const int half = N / 2;
            int shiftedX = gx + half;
            int shiftedZ = gz + half;

            int sx = FloorDiv(shiftedX, N);
            int sz = FloorDiv(shiftedZ, N);
            secCoord = new Vector2Int(sx, sz);

            lx = shiftedX - (sx * N);
            lz = shiftedZ - (sz * N);
        }

        private static int FloorDiv(int a, int b)
        {
            return a >= 0 ? a / b : ((a - b + 1) / b);
        }

        public static void GetAgentDimensions(Pathfinding.AgentType agentType, out float radius, out float height)
        {
            switch (agentType)
            {
                case Pathfinding.AgentType.TrollSize:
                    radius = 1.0f;
                    height = 7.0f;
                    break;
                case Pathfinding.AgentType.Abomination:
                    radius = 1.5f;
                    height = 5.0f;
                    break;
                case Pathfinding.AgentType.SeekerQueen:
                    radius = 1.5f;
                    height = 7.0f;
                    break;
                case Pathfinding.AgentType.HugeSize:
                    radius = 2.0f;
                    height = 10.0f;
                    break;
                case Pathfinding.AgentType.GoblinBruteSize:
                case Pathfinding.AgentType.HorseSize:
                    radius = 0.8f;
                    height = 3.2f;
                    break;
                case Pathfinding.AgentType.HumanoidBig:
                case Pathfinding.AgentType.HumanoidBigNoSwim:
                    radius = 0.5f;
                    height = 2.5f;
                    break;
                default:
                    radius = 0.4f;
                    height = 1.8f;
                    break;
            }
        }

        public static void DrawDebugTelemetry()
        {
            if (!NavDebugOverlay.IsModeActive(DebugDrawMode.Grid)) return;
            Player local = Player.m_localPlayer;
            if (local == null) return;

            WorldToGlobalCell(local.transform.position, out int cx, out int cz);
            for (int dz = -10; dz <= 10; dz++)
            {
                for (int dx = -10; dx <= 10; dx++)
                {
                    if (!TryGetCell(cx + dx, cz + dz, 0, out NavCell cell)) continue;
                    Vector3 p = GlobalCellToWorld(cx + dx, cz + dz, 0);

                    Color col = Color.green;
                    if (cell.IsDoorway) col = Color.cyan;
                    else if (cell.IsDestructible) col = Color.red;
                    else if ((cell.Flags & NavSurfaceFlags.Hazard) != 0) col = new Color(1f, 0.5f, 0f);
                    else if (cell.IsWater) col = Color.blue;

                    NavDebugOverlay.AddLine(p, p + Vector3.up * Mathf.Min(1.2f, cell.TrueClearanceRadius * 0.25f), col);
                }
            }
        }
    }
}
