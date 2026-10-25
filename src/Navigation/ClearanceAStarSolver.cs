using System;
using System.Collections.Generic;
using UnityEngine;
using VikingAI.Profiles;

namespace VikingAI.Navigation
{
    /// <summary>
    /// Parameterized A* solver over the 2.5D NavSector graph.
    /// Evaluates agent radial clearance, vertical ceiling clearance, and registered INavTraversalRule contracts.
    /// </summary>
    public static class ClearanceAStarSolver
    {
        private struct NodeKey : IEquatable<NodeKey>
        {
            public readonly int WorldGridX;
            public readonly int WorldGridZ;
            public readonly int Layer;

            public NodeKey(int x, int z, int layer)
            {
                WorldGridX = x;
                WorldGridZ = z;
                Layer = layer;
            }

            public bool Equals(NodeKey other) =>
                WorldGridX == other.WorldGridX && WorldGridZ == other.WorldGridZ && Layer == other.Layer;

            public override bool Equals(object? obj) => obj is NodeKey other && Equals(other);

            public override int GetHashCode() => (WorldGridX * 73856093) ^ (WorldGridZ * 19349663) ^ (Layer * 83492791);
        }

        private static readonly int[] _dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] _dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] _stepDist = { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };

        private static readonly Dictionary<NodeKey, float> _gScore = new Dictionary<NodeKey, float>(512);
        private static readonly Dictionary<NodeKey, NodeKey> _cameFrom = new Dictionary<NodeKey, NodeKey>(512);
        private static readonly HashSet<NodeKey> _closed = new HashSet<NodeKey>();
        private static readonly List<(NodeKey Key, float F)> _openHeap = new List<(NodeKey, float)>(256);

        public static bool TryFindPath(
            Vector3 startWorld,
            Vector3 goalWorld,
            Character? agent,
            AgentBehaviorProfile profile,
            float requiredRadius,
            float requiredHeight,
            IReadOnlyList<INavTraversalRule> rules,
            List<Vector3>? outWaypoints,
            int maxIterations = 512)
        {
            Vector3 planarDiff = goalWorld - startWorld;
            planarDiff.y = 0f;
            if (planarDiff.sqrMagnitude > 96f * 96f)
            {
                return false;
            }

            NavGraphManager.WorldToGlobalCell(startWorld, out int sx, out int sz);
            NavGraphManager.WorldToGlobalCell(goalWorld, out int gx, out int gz);

            int startLayer = NavGraphManager.ResolveBestLayer(sx, sz, startWorld.y);
            int goalLayer = NavGraphManager.ResolveBestLayer(gx, gz, goalWorld.y);

            NodeKey startKey = new NodeKey(sx, sz, startLayer);
            NodeKey goalKey = new NodeKey(gx, gz, goalLayer);

            _gScore.Clear();
            _cameFrom.Clear();
            _closed.Clear();
            _openHeap.Clear();

            _gScore[startKey] = 0f;
            _openHeap.Add((startKey, Heuristic(startKey, goalKey)));

            NodeKey bestPartial = startKey;
            float bestPartialH = Heuristic(startKey, goalKey);
            int iterations = 0;

            while (_openHeap.Count > 0 && iterations++ < maxIterations)
            {
                int bestIdx = 0;
                float bestF = _openHeap[0].F;
                for (int i = 1; i < _openHeap.Count; i++)
                {
                    if (_openHeap[i].F < bestF)
                    {
                        bestF = _openHeap[i].F;
                        bestIdx = i;
                    }
                }

                NodeKey current = _openHeap[bestIdx].Key;
                int last = _openHeap.Count - 1;
                _openHeap[bestIdx] = _openHeap[last];
                _openHeap.RemoveAt(last);

                if (current.Equals(goalKey))
                {
                    ReconstructPath(startKey, current, goalWorld, outWaypoints);
                    return true;
                }

                if (!_closed.Add(current)) continue;

                if (!NavGraphManager.TryGetCell(current.WorldGridX, current.WorldGridZ, current.Layer, out NavCell fromCell))
                {
                    continue;
                }

                float currentG = _gScore[current];

                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = current.WorldGridX + _dx[dir];
                    int nz = current.WorldGridZ + _dz[dir];
                    float stepLen = _stepDist[dir];

                    for (int nLayer = 0; nLayer <= 1; nLayer++)
                    {
                        NodeKey neighborKey = new NodeKey(nx, nz, nLayer);
                        if (_closed.Contains(neighborKey)) continue;

                        if (!NavGraphManager.TryGetCell(nx, nz, nLayer, out NavCell toCell)) continue;

                        if (toCell.CeilingClearance < requiredHeight) continue;
                        if (!toCell.IsDoorway && !toCell.IsDestructible && toCell.TrueClearanceRadius < requiredRadius * 0.6f)
                        {
                            continue;
                        }

                        if (!EvaluateRules(agent, profile, in fromCell, in toCell, stepLen, rules, out float costMult))
                        {
                            continue;
                        }

                        float tentativeG = currentG + (stepLen * costMult);
                        if (!_gScore.TryGetValue(neighborKey, out float existingG) || tentativeG < existingG)
                        {
                            _gScore[neighborKey] = tentativeG;
                            _cameFrom[neighborKey] = current;
                            float h = Heuristic(neighborKey, goalKey);
                            _openHeap.Add((neighborKey, tentativeG + h));

                            if (h < bestPartialH)
                            {
                                bestPartialH = h;
                                bestPartial = neighborKey;
                            }
                        }
                    }
                }
            }

            if (bestPartialH <= 2.2f && !bestPartial.Equals(startKey))
            {
                ReconstructPath(startKey, bestPartial, goalWorld, outWaypoints);
                return true;
            }

            return false;
        }

        private static bool EvaluateRules(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float stepLen,
            IReadOnlyList<INavTraversalRule> rules,
            out float bestCost)
        {
            bestCost = float.MaxValue;
            bool anyValid = false;

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].TryEvaluateEdge(agent, profile, in from, in to, stepLen, out float ruleCost))
                {
                    if (ruleCost < bestCost)
                    {
                        bestCost = ruleCost;
                    }
                    anyValid = true;
                }
            }

            return anyValid;
        }

        private static float Heuristic(NodeKey a, NodeKey b)
        {
            float dx = a.WorldGridX - b.WorldGridX;
            float dz = a.WorldGridZ - b.WorldGridZ;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static void ReconstructPath(NodeKey start, NodeKey end, Vector3 exactGoal, List<Vector3>? outWaypoints)
        {
            if (outWaypoints == null) return;

            outWaypoints.Clear();
            NodeKey curr = end;
            int safety = 0;

            while (!curr.Equals(start) && safety++ < 512)
            {
                Vector3 wp = NavGraphManager.GlobalCellToWorld(curr.WorldGridX, curr.WorldGridZ, curr.Layer);
                outWaypoints.Add(wp);
                if (!_cameFrom.TryGetValue(curr, out curr)) break;
            }

            outWaypoints.Add(NavGraphManager.GlobalCellToWorld(start.WorldGridX, start.WorldGridZ, start.Layer));
            outWaypoints.Reverse();

            if (outWaypoints.Count > 0)
            {
                outWaypoints[outWaypoints.Count - 1] = exactGoal;
            }

            SimplifyCollinearWaypoints(outWaypoints);
        }

        private static void SimplifyCollinearWaypoints(List<Vector3> path)
        {
            if (path.Count <= 2) return;

            for (int i = path.Count - 2; i >= 1; i--)
            {
                Vector3 d1 = (path[i] - path[i - 1]).normalized;
                Vector3 d2 = (path[i + 1] - path[i]).normalized;
                if (Vector3.Dot(d1, d2) > 0.985f && Mathf.Abs(path[i + 1].y - path[i - 1].y) < 0.45f)
                {
                    path.RemoveAt(i);
                }
            }
        }
    }
}
