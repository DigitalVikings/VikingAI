using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using VikingAI.Configuration;
using VikingAI.Navigation;

namespace VikingAI.Patches
{
    /// <summary>
    /// Hooks Valheim's Pathfinding and BaseAI path queries to:
    /// 1. Normalize 32m tile-seam link costs (10.0 -> 1.2) so agents do not zig-zag along tile borders.
    /// 2. Accelerate async tile build handoffs when multiple agent sizes are active.
    /// 3. Route failed or doorway/ledge/breach-blocked paths through NavGraphManager's 2.5D clearance A* solver.
    /// </summary>
    [HarmonyPatch]
    public static class PathfindingPatches
    {
        private static readonly AccessTools.FieldRef<BaseAI, Character> _aiCharacter =
            AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
        private static readonly AccessTools.FieldRef<Pathfinding, NavMeshPath> _pfPath =
            AccessTools.FieldRefAccess<Pathfinding, NavMeshPath>("m_path");
        private static readonly AccessTools.FieldRef<Pathfinding, AsyncOperation> _pfBuildOp =
            AccessTools.FieldRefAccess<Pathfinding, AsyncOperation>("m_buildOperation");
        private static readonly MethodInfo _buildTilesMethod =
            AccessTools.Method(typeof(Pathfinding), "Buildtiles");

        [HarmonyPatch(typeof(Pathfinding), "Awake")]
        [HarmonyPostfix]
        private static void Pathfinding_Awake_Postfix(Pathfinding __instance)
        {
            if (ModConfig.ModEnabled.Value && ModConfig.EnableTileSeamCostFix.Value)
            {
                __instance.m_linkCost = ModConfig.TileSeamCostModifier.Value;
            }
        }

        [HarmonyPatch(typeof(Pathfinding), "ConnectAlongEdge")]
        [HarmonyPrefix]
        private static void Pathfinding_ConnectAlongEdge_Prefix(Pathfinding __instance)
        {
            if (!ModConfig.ModEnabled.Value) return;
            __instance.m_linkCost = ModConfig.EnableTileSeamCostFix.Value
                ? ModConfig.TileSeamCostModifier.Value
                : 10.0f;
        }

        [HarmonyPatch(typeof(Pathfinding), "UpdatePathfinding")]
        [HarmonyPostfix]
        private static void Pathfinding_UpdatePathfinding_Postfix(Pathfinding __instance)
        {
            if (!ModConfig.ModEnabled.Value || _buildTilesMethod == null) return;

            int extraBuilds = ModConfig.MaxAsyncTileBuildsPerTick.Value - 1;
            for (int i = 0; i < extraBuilds; i++)
            {
                AsyncOperation op = _pfBuildOp(__instance);
                if (op != null && !op.isDone)
                {
                    break;
                }
                _buildTilesMethod.Invoke(__instance, null);
            }
        }

        [HarmonyPatch(typeof(BaseAI), "FindPath")]
        [HarmonyPrefix]
        private static void BaseAI_FindPath_Prefix(BaseAI __instance)
        {
            NavGraphManager.CurrentPathingCharacter = _aiCharacter(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "FindPath")]
        [HarmonyFinalizer]
        private static void BaseAI_FindPath_Finalizer()
        {
            NavGraphManager.CurrentPathingCharacter = null;
        }

        [HarmonyPatch(typeof(BaseAI), "HavePath")]
        [HarmonyPrefix]
        private static void BaseAI_HavePath_Prefix(BaseAI __instance)
        {
            NavGraphManager.CurrentPathingCharacter = _aiCharacter(__instance);
        }

        [HarmonyPatch(typeof(BaseAI), "HavePath")]
        [HarmonyFinalizer]
        private static void BaseAI_HavePath_Finalizer()
        {
            NavGraphManager.CurrentPathingCharacter = null;
        }

        [HarmonyPatch(typeof(Pathfinding), nameof(Pathfinding.GetPath))]
        [HarmonyPostfix]
        private static void Pathfinding_GetPath_Postfix(
            Pathfinding __instance,
            Vector3 from,
            Vector3 to,
            List<Vector3>? path,
            Pathfinding.AgentType agentType,
            ref bool __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableUnifiedNavGraph.Value)
            {
                return;
            }

            NavMeshPath navPath = _pfPath(__instance);
            bool isIncompleteOrFailed = !__result ||
                (navPath != null && navPath.status != NavMeshPathStatus.PathComplete);

            if (isIncompleteOrFailed)
            {
                if (NavGraphManager.TrySolvePath(from, to, agentType, path))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        [HarmonyPostfix]
        private static void Piece_SetCreator_Postfix(Piece __instance)
        {
            if (__instance != null)
            {
                NavGraphManager.InvalidatePosition(__instance.transform.position);
            }
        }

        [HarmonyPatch(typeof(WearNTear), "OnDestroy")]
        [HarmonyPostfix]
        private static void WearNTear_OnDestroy_Postfix(WearNTear __instance)
        {
            if (__instance != null)
            {
                NavGraphManager.InvalidatePosition(__instance.transform.position);
            }
        }
    }
}
