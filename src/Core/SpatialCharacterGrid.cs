using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingAI.Core
{
    /// <summary>
    /// High-performance 2D spatial hash grid over Character.GetAllCharacters().
    /// Rebuilt at 20 Hz so steering, pack coordination, and perception queries run in O(1) cell lookups
    /// with zero per-agent Physics.OverlapSphere allocations.
    /// </summary>
    public static class SpatialCharacterGrid
    {
        public const float CellSize = 8.0f;
        private const float InvCellSize = 1.0f / CellSize;
        private const float RebuildInterval = 0.05f; // 20 Hz

        private static readonly Dictionary<long, List<Character>> _cells = new Dictionary<long, List<Character>>(128);
        private static readonly Stack<List<Character>> _listPool = new Stack<List<Character>>(128);
        private static float _lastRebuildTime = -999f;

        public static void EnsureUpToDate()
        {
            float now = Time.time;
            if (now - _lastRebuildTime < RebuildInterval && _lastRebuildTime <= now)
            {
                return;
            }
            _lastRebuildTime = now;
            Rebuild();
        }

        public static void Rebuild()
        {
            foreach (var kvp in _cells)
            {
                kvp.Value.Clear();
                _listPool.Push(kvp.Value);
            }
            _cells.Clear();

            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c.IsDead()) continue;

                Vector3 pos = c.transform.position;
                long key = GetCellKey(pos.x, pos.z);
                if (!_cells.TryGetValue(key, out List<Character> cellList))
                {
                    cellList = _listPool.Count > 0 ? _listPool.Pop() : new List<Character>(8);
                    _cells[key] = cellList;
                }
                cellList.Add(c);
            }
        }

        /// <summary>
        /// Populates <paramref name="results"/> with all live characters within <paramref name="radius"/> of <paramref name="center"/>.
        /// Caller is responsible for clearing <paramref name="results"/> before calling if desired.
        /// </summary>
        public static void QueryRadius(Vector3 center, float radius, List<Character> results, Character? exclude = null)
        {
            EnsureUpToDate();

            int minX = Mathf.FloorToInt((center.x - radius) * InvCellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) * InvCellSize);
            int minZ = Mathf.FloorToInt((center.z - radius) * InvCellSize);
            int maxZ = Mathf.FloorToInt((center.z + radius) * InvCellSize);
            float radiusSq = radius * radius;

            for (int gx = minX; gx <= maxX; gx++)
            {
                for (int gz = minZ; gz <= maxZ; gz++)
                {
                    long key = PackCoords(gx, gz);
                    if (!_cells.TryGetValue(key, out List<Character> cellList)) continue;

                    for (int i = 0; i < cellList.Count; i++)
                    {
                        Character c = cellList[i];
                        if (c == null || c == exclude || c.IsDead()) continue;

                        Vector3 diff = c.transform.position - center;
                        if (diff.sqrMagnitude <= radiusSq)
                        {
                            results.Add(c);
                        }
                    }
                }
            }
        }

        private static long GetCellKey(float worldX, float worldZ)
        {
            int gx = Mathf.FloorToInt(worldX * InvCellSize);
            int gz = Mathf.FloorToInt(worldZ * InvCellSize);
            return PackCoords(gx, gz);
        }

        private static long PackCoords(int gx, int gz)
        {
            return ((long)gx << 32) | (uint)gz;
        }
    }
}
