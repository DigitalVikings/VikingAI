using System.Collections.Generic;
using UnityEngine;
using VikingAI.Core;
using VikingAI.Navigation;
using VikingAI.Telemetry;

namespace VikingAI.FlowField
{
    /// <summary>
    /// Maintains a 64x64m (32x32 cells at 2m resolution) player-centric Dijkstra integration field
    /// and flow vector grid updated at 5 Hz. Allows large packs and raids to share a single spatial
    /// gradient with crowding and fire-hazard repulsion.
    /// </summary>
    public sealed class TacticalFlowField
    {
        public const int GridSize = 32;
        public const float CellSize = 2.0f;
        public const float HalfSpan = (GridSize * CellSize) * 0.5f; // 32m radius around anchor
        private const float RebuildInterval = 0.2f; // 5 Hz

        private static readonly Dictionary<long, TacticalFlowField> _playerFields =
            new Dictionary<long, TacticalFlowField>(4);

        private static readonly int[] _dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] _dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] _stepCost = { 2f, 2f, 2f, 2f, 2.828f, 2.828f, 2.828f, 2.828f };

        public Vector3 OriginWorld { get; private set; }
        public Vector3 AnchorWorld { get; private set; }
        private float _lastUpdateTime = -999f;

        private readonly float[] _integrationCost = new float[GridSize * GridSize];
        private readonly float[] _cellTraversalCost = new float[GridSize * GridSize];
        private readonly Vector3[] _flowVectors = new Vector3[GridSize * GridSize];
        private readonly Queue<int> _openQueue = new Queue<int>(256);
        private readonly List<Character> _crowdTemp = new List<Character>(32);

        public static TacticalFlowField GetOrUpdateForTarget(Character target)
        {
            long id = target is Player p ? p.GetPlayerID() : target.GetInstanceID();
            if (!_playerFields.TryGetValue(id, out TacticalFlowField field))
            {
                field = new TacticalFlowField();
                _playerFields[id] = field;
            }

            float now = Time.time;
            if (now - field._lastUpdateTime >= RebuildInterval ||
                (target.transform.position - field.AnchorWorld).sqrMagnitude > 9.0f)
            {
                field._lastUpdateTime = now;
                field.Rebuild(target.transform.position, target);
            }

            return field;
        }

        public bool TrySampleFlow(Vector3 worldPos, out Vector3 flowDir)
        {
            int gx = Mathf.FloorToInt((worldPos.x - OriginWorld.x) / CellSize);
            int gz = Mathf.FloorToInt((worldPos.z - OriginWorld.z) / CellSize);

            if (gx < 0 || gx >= GridSize || gz < 0 || gz >= GridSize)
            {
                flowDir = Vector3.zero;
                return false;
            }

            flowDir = _flowVectors[gz * GridSize + gx];
            return flowDir.sqrMagnitude > 0.01f;
        }

        private void Rebuild(Vector3 center, Character target)
        {
            AnchorWorld = center;
            OriginWorld = new Vector3(
                Mathf.Floor(center.x / CellSize) * CellSize - HalfSpan,
                center.y,
                Mathf.Floor(center.z / CellSize) * CellSize - HalfSpan);

            // 1. Initialize base traversal cost from NavGraphManager + crowd density
            for (int z = 0; z < GridSize; z++)
            {
                float wz = OriginWorld.z + (z + 0.5f) * CellSize;
                for (int x = 0; x < GridSize; x++)
                {
                    float wx = OriginWorld.x + (x + 0.5f) * CellSize;
                    int idx = z * GridSize + x;

                    NavGraphManager.WorldToGlobalCell(new Vector3(wx, center.y, wz), out int ncx, out int ncz);
                    float cost = 1.0f;
                    if (NavGraphManager.TryGetCell(ncx, ncz, 0, out NavCell navCell))
                    {
                        if (!navCell.IsWalkable || (navCell.IsDestructible && !navCell.IsDoorway))
                        {
                            cost = 250.0f;
                        }
                        else if ((navCell.Flags & NavSurfaceFlags.Hazard) != 0)
                        {
                            cost = 15.0f;
                        }
                        else if (navCell.IsWater)
                        {
                            cost = 3.5f;
                        }
                    }

                    _cellTraversalCost[idx] = cost;
                    _integrationCost[idx] = float.MaxValue;
                }
            }

            // Stamp crowd density repulsion so trailing attackers route around congested lanes
            _crowdTemp.Clear();
            SpatialCharacterGrid.QueryRadius(center, HalfSpan, _crowdTemp, exclude: target);
            for (int i = 0; i < _crowdTemp.Count; i++)
            {
                Vector3 cPos = _crowdTemp[i].transform.position;
                int cx = Mathf.FloorToInt((cPos.x - OriginWorld.x) / CellSize);
                int cz = Mathf.FloorToInt((cPos.z - OriginWorld.z) / CellSize);
                if (cx >= 0 && cx < GridSize && cz >= 0 && cz < GridSize)
                {
                    _cellTraversalCost[cz * GridSize + cx] += 2.5f;
                }
            }

            // 2. Dijkstra integration wavefront from target center
            int startX = Mathf.Clamp(Mathf.FloorToInt((center.x - OriginWorld.x) / CellSize), 0, GridSize - 1);
            int startZ = Mathf.Clamp(Mathf.FloorToInt((center.z - OriginWorld.z) / CellSize), 0, GridSize - 1);
            int startIdx = startZ * GridSize + startX;

            _openQueue.Clear();
            _integrationCost[startIdx] = 0f;
            _openQueue.Enqueue(startIdx);

            while (_openQueue.Count > 0)
            {
                int currIdx = _openQueue.Dequeue();
                int cx = currIdx % GridSize;
                int cz = currIdx / GridSize;
                float currTotal = _integrationCost[currIdx];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + _dx[d];
                    int nz = cz + _dz[d];
                    if (nx < 0 || nx >= GridSize || nz < 0 || nz >= GridSize) continue;

                    int nIdx = nz * GridSize + nx;
                    float cellMult = _cellTraversalCost[nIdx];
                    if (cellMult >= 200f) continue;

                    float newCost = currTotal + (_stepCost[d] * cellMult);
                    if (newCost < _integrationCost[nIdx])
                    {
                        _integrationCost[nIdx] = newCost;
                        _openQueue.Enqueue(nIdx);
                    }
                }
            }

            // 3. Compute normalized gradient flow vectors
            for (int z = 0; z < GridSize; z++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    int idx = z * GridSize + x;
                    float bestVal = _integrationCost[idx];
                    if (bestVal >= float.MaxValue * 0.5f)
                    {
                        _flowVectors[idx] = Vector3.zero;
                        continue;
                    }

                    Vector3 bestDir = Vector3.zero;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + _dx[d];
                        int nz = z + _dz[d];
                        if (nx < 0 || nx >= GridSize || nz < 0 || nz >= GridSize) continue;

                        float nVal = _integrationCost[nz * GridSize + nx];
                        if (nVal < bestVal)
                        {
                            bestVal = nVal;
                            bestDir = new Vector3(_dx[d], 0f, _dz[d]).normalized;
                        }
                    }

                    _flowVectors[idx] = bestDir;
                }
            }
        }

        public static void DrawDebugTelemetry()
        {
            if (!NavDebugOverlay.IsModeActive(DebugDrawMode.Flow)) return;
            Player local = Player.m_localPlayer;
            if (local == null) return;

            TacticalFlowField field = GetOrUpdateForTarget(local);
            for (int z = 0; z < GridSize; z += 2)
            {
                for (int x = 0; x < GridSize; x += 2)
                {
                    int idx = z * GridSize + x;
                    Vector3 flow = field._flowVectors[idx];
                    if (flow.sqrMagnitude < 0.01f) continue;

                    float wx = field.OriginWorld.x + (x + 0.5f) * CellSize;
                    float wz = field.OriginWorld.z + (z + 0.5f) * CellSize;
                    Vector3 start = new Vector3(wx, local.transform.position.y + 0.35f, wz);
                    NavDebugOverlay.AddLine(start, start + flow * 1.3f, Color.yellow);
                }
            }
        }
    }
}
