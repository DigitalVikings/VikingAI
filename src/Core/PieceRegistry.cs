using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VikingAI.Core
{
    /// <summary>
    /// Tracks active interactive pieces and world affordances via Awake/OnDestroy registration hooks.
    /// Strictly adheres to the Per-Frame Allocation &amp; Scene Search Invariant (zero FindObjectsOfType calls).
    /// </summary>
    [HarmonyPatch]
    public static class PieceRegistry
    {
        private static readonly HashSet<Door> _doors = new HashSet<Door>();
        private static readonly HashSet<WearNTear> _wearNTears = new HashSet<WearNTear>();
        private static readonly HashSet<Smelter> _smelters = new HashSet<Smelter>();
        private static readonly HashSet<Container> _containers = new HashSet<Container>();
        private static readonly HashSet<Fireplace> _fireplaces = new HashSet<Fireplace>();
        private static readonly HashSet<Plant> _plants = new HashSet<Plant>();
        private static readonly HashSet<Pickable> _pickables = new HashSet<Pickable>();
        private static readonly HashSet<Bed> _beds = new HashSet<Bed>();
        private static readonly HashSet<Chair> _chairs = new HashSet<Chair>();

        public static IReadOnlyCollection<Door> Doors => _doors;
        public static IReadOnlyCollection<WearNTear> WearNTears => _wearNTears;
        public static IReadOnlyCollection<Smelter> Smelters => _smelters;
        public static IReadOnlyCollection<Container> Containers => _containers;
        public static IReadOnlyCollection<Fireplace> Fireplaces => _fireplaces;
        public static IReadOnlyCollection<Plant> Plants => _plants;
        public static IReadOnlyCollection<Pickable> Pickables => _pickables;
        public static IReadOnlyCollection<Bed> Beds => _beds;
        public static IReadOnlyCollection<Chair> Chairs => _chairs;

        public static void QueryDoorsInRadius(Vector3 center, float radius, List<Door> results)
        {
            QuerySetInRadius(_doors, center, radius, results);
        }

        public static void QueryWearNTearInRadius(Vector3 center, float radius, List<WearNTear> results)
        {
            QuerySetInRadius(_wearNTears, center, radius, results);
        }

        public static void QuerySetInRadius<T>(HashSet<T> set, Vector3 center, float radius, List<T> results) where T : Component
        {
            float rSq = radius * radius;
            bool hasNulls = false;

            foreach (T item in set)
            {
                if (item == null)
                {
                    hasNulls = true;
                    continue;
                }

                if ((item.transform.position - center).sqrMagnitude <= rSq)
                {
                    results.Add(item);
                }
            }

            if (hasNulls)
            {
                set.RemoveWhere(x => x == null);
            }
        }

        private static void RegisterPieceComponents(Piece piece)
        {
            if (piece == null) return;
            GameObject go = piece.gameObject;

            if (go.TryGetComponent<Door>(out var door)) _doors.Add(door);
            if (go.TryGetComponent<WearNTear>(out var wnt)) _wearNTears.Add(wnt);
            if (go.TryGetComponent<Smelter>(out var smelter)) _smelters.Add(smelter);
            if (go.TryGetComponent<Container>(out var container)) _containers.Add(container);
            if (go.TryGetComponent<Fireplace>(out var fireplace)) _fireplaces.Add(fireplace);
            if (go.TryGetComponent<Plant>(out var plant)) _plants.Add(plant);
            if (go.TryGetComponent<Bed>(out var bed)) _beds.Add(bed);

            Chair chair = go.GetComponentInChildren<Chair>();
            if (chair != null) _chairs.Add(chair);
        }

        private static void UnregisterPieceComponents(Piece piece)
        {
            if (piece == null) return;
            GameObject go = piece.gameObject;

            if (go.TryGetComponent<Door>(out var door)) _doors.Remove(door);
            if (go.TryGetComponent<WearNTear>(out var wnt)) _wearNTears.Remove(wnt);
            if (go.TryGetComponent<Smelter>(out var smelter)) _smelters.Remove(smelter);
            if (go.TryGetComponent<Container>(out var container)) _containers.Remove(container);
            if (go.TryGetComponent<Fireplace>(out var fireplace)) _fireplaces.Remove(fireplace);
            if (go.TryGetComponent<Plant>(out var plant)) _plants.Remove(plant);
            if (go.TryGetComponent<Bed>(out var bed)) _beds.Remove(bed);

            Chair chair = go.GetComponentInChildren<Chair>();
            if (chair != null) _chairs.Remove(chair);
        }

        private static readonly AccessTools.FieldRef<Piece, ZNetView> _pieceNView =
            AccessTools.FieldRefAccess<Piece, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Door, ZNetView> _doorNView =
            AccessTools.FieldRefAccess<Door, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Pickable, ZNetView> _pickableNView =
            AccessTools.FieldRefAccess<Pickable, ZNetView>("m_nview");

        [HarmonyPatch(typeof(Piece), "Awake")]
        [HarmonyPostfix]
        private static void Piece_Awake_Postfix(Piece __instance)
        {
            ZNetView nview = _pieceNView(__instance);
            if (nview != null && nview.IsValid())
            {
                RegisterPieceComponents(__instance);
            }
        }

        [HarmonyPatch(typeof(Piece), "OnDestroy")]
        [HarmonyPostfix]
        private static void Piece_OnDestroy_Postfix(Piece __instance)
        {
            UnregisterPieceComponents(__instance);
        }

        [HarmonyPatch(typeof(Door), "Awake")]
        [HarmonyPostfix]
        private static void Door_Awake_Postfix(Door __instance)
        {
            if (__instance == null) return;
            ZNetView nview = _doorNView(__instance);
            if (nview != null && nview.IsValid())
            {
                _doors.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(Pickable), "Awake")]
        [HarmonyPostfix]
        private static void Pickable_Awake_Postfix(Pickable __instance)
        {
            if (__instance == null) return;
            ZNetView nview = _pickableNView(__instance);
            if (nview != null && nview.IsValid())
            {
                _pickables.Add(__instance);
            }
        }
    }
}
