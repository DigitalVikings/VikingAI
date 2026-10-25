using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Locomotion;
using VikingAI.Profiles;
using VikingAI.Tactics;

namespace VikingAI.Patches
{
    /// <summary>
    /// Hooks BaseAI and MonsterAI sensory, damage, and brain update methods to drive:
    /// 1. The 4-stage Sensory &amp; Perception Engine (CanSeeTarget, CanHearTarget, CanSenseTarget).
    /// 2. Near-projectile sound investigation and on-damaged ally call waves.
    /// 3. BrainPipeline module evaluation before falling back to vanilla MonsterAI.UpdateAI.
    /// </summary>
    [HarmonyPatch]
    public static class TacticsPatches
    {
        private static readonly AccessTools.FieldRef<BaseAI, Character> _aiCharacter =
            AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
        private static readonly AccessTools.FieldRef<BaseAI, ZNetView> _aiNView =
            AccessTools.FieldRefAccess<BaseAI, ZNetView>("m_nview");
        private static readonly MethodInfo _updateSleepMethod =
            AccessTools.Method(typeof(MonsterAI), "UpdateSleep");
        private static readonly MethodInfo _updateTargetMethod =
            AccessTools.Method(typeof(MonsterAI), "UpdateTarget");

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new[] { typeof(Character) })]
        [HarmonyPrefix]
        private static bool BaseAI_CanSeeTarget_Prefix(
            BaseAI __instance,
            Character target,
            ref bool __result,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!SensoryMemoryController.ShouldUseSensoryEngine(__instance, out Character? observer, out AgentBehaviorProfile profile) ||
                observer == null)
            {
                return true;
            }

            __result = SensoryMemoryController.EvaluateVisualStimulus(__instance, observer, profile, target);
            return false;
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), new[] { typeof(Character) })]
        [HarmonyPrefix]
        private static bool BaseAI_CanHearTarget_Prefix(
            BaseAI __instance,
            Character target,
            ref bool __result,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!SensoryMemoryController.ShouldUseSensoryEngine(__instance, out Character? observer, out AgentBehaviorProfile profile) ||
                observer == null)
            {
                return true;
            }

            __result = SensoryMemoryController.EvaluateAuditoryStimulus(__instance, observer, profile, target);
            return false;
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), new[] { typeof(Character), typeof(bool) })]
        [HarmonyPrefix]
        private static bool BaseAI_CanSenseTarget_Prefix(
            BaseAI __instance,
            Character target,
            bool passiveAggresive,
            ref bool __result,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!SensoryMemoryController.ShouldUseSensoryEngine(__instance, out Character? observer, out AgentBehaviorProfile profile) ||
                observer == null)
            {
                return true;
            }

            if (!passiveAggresive &&
                ZoneSystem.instance != null &&
                ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) &&
                (!observer.IsTamed() || !target.GetBaseAI().IsAlerted()))
            {
                __result = false;
                return false;
            }

            bool heard = SensoryMemoryController.EvaluateAuditoryStimulus(__instance, observer, profile, target);
            bool seen = SensoryMemoryController.EvaluateVisualStimulus(__instance, observer, profile, target);
            __result = heard || seen;
            return false;
        }

        [HarmonyPatch(typeof(MonsterAI), "OnDamaged")]
        [HarmonyPostfix]
        private static void MonsterAI_OnDamaged_Postfix(MonsterAI __instance, Character attacker)
        {
            if (!ModConfig.ModEnabled.Value) return;
            SensoryMemoryController.NotifyDamaged(__instance, attacker);
        }

        [HarmonyPatch(typeof(MonsterAI), "RPC_OnNearProjectileHit")]
        [HarmonyPrefix]
        private static bool MonsterAI_RPC_OnNearProjectileHit_Prefix(
            MonsterAI __instance,
            Vector3 center,
            ZDOID attackerID,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!SensoryMemoryController.ShouldUseSensoryEngine(__instance, out Character? observer, out _) ||
                observer == null)
            {
                return true;
            }

            ZNetView nview = _aiNView(__instance);
            if (nview == null || !nview.IsOwner()) return false;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)) return false;

            Character? attacker = null;
            if (ZNetScene.instance != null)
            {
                GameObject go = ZNetScene.instance.FindInstance(attackerID);
                if (go != null) attacker = go.GetComponent<Character>();
            }

            // Instead of magically revealing a hidden sniper's exact identity, create a strong sound investigation impulse at impact
            SensoryMemoryController.NotifySoundImpulse(__instance, attacker, center, 0.55f);
            return false;
        }

        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        [HarmonyPrefix]
        private static bool MonsterAI_UpdateAI_Prefix(
            MonsterAI __instance,
            float dt,
            ref bool __result,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!ModConfig.ModEnabled.Value) return true;

            ZNetView nview = _aiNView(__instance);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return true;
            }

            DoorTraversalController.UpdatePendingAutoCloses();

            Character character = _aiCharacter(__instance);
            if (character == null || character.IsDead()) return true;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            SensoryMemoryController.TickAgentMemory(__instance, character, profile, dt);

            if (__instance.IsSleeping())
            {
                return true;
            }

            if (BrainPipeline.TryExecuteModules(__instance, character, dt))
            {
                // Keep target timers updated on ticks claimed by a custom brain module
                if (_updateTargetMethod != null)
                {
                    object?[] args = { character as Humanoid, dt, false, false };
                    _updateTargetMethod.Invoke(__instance, args);
                }

                __result = true;
                return false;
            }

            return true;
        }
    }
}
