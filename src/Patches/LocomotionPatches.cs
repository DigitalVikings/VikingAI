using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Locomotion;
using VikingAI.Profiles;

namespace VikingAI.Patches
{
    /// <summary>
    /// Hooks BaseAI.MoveTowards, BaseAI.MoveTo, and BaseAI.Follow to provide:
    /// 1. 16-ray Context Steering with crowd separation and obstacle whiskers.
    /// 2. Decoupled SetLookDir / SetMoveDir for 360-degree humanoid combat strafing and backstepping.
    /// 3. Automatic door opening/closing and agile ledge jumping along paths.
    /// 4. Polar V-wedge follower formations and automatic lateral yielding when a player approaches.
    /// </summary>
    [HarmonyPatch]
    public static class LocomotionPatches
    {
        private static readonly AccessTools.FieldRef<BaseAI, Character> _aiCharacter =
            AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
        private static readonly AccessTools.FieldRef<BaseAI, List<Vector3>> _aiPath =
            AccessTools.FieldRefAccess<BaseAI, List<Vector3>>("m_path");
        private static readonly AccessTools.FieldRef<BaseAI, float> _aiJumpTimer =
            AccessTools.FieldRefAccess<BaseAI, float>("m_jumpTimer");
        private static readonly MethodInfo _moveToMethod =
            AccessTools.Method(typeof(BaseAI), "MoveTo");

        /// <summary>
        /// Optional per-frame combat look target override set by tactical brain modules (e.g. flanking strafe).
        /// </summary>
        public static Vector3? ActiveLookDirOverride { get; set; }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.MoveTowards))]
        [HarmonyPrefix]
        private static bool BaseAI_MoveTowards_Prefix(
            BaseAI __instance,
            ref Vector3 dir,
            bool run,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!ModConfig.ModEnabled.Value) return true;

            Character character = _aiCharacter(__instance);
            if (character == null || character.IsFlying() || __instance.m_serpentMovement)
            {
                return true;
            }

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            Vector3 desiredDir = dir.normalized;

            // Actuate any closed doorway directly ahead if this profile can open doors
            if (profile.CanOpenDoors)
            {
                DoorTraversalController.CheckAndActuateDoors(character, profile, desiredDir);
            }

            // Apply 16-ray context steering
            if (ModConfig.EnableContextSteering.Value)
            {
                desiredDir = SteeringPipeline.Evaluate(character, profile, desiredDir);
            }

            // Determine whether to lock facing onto the combat target and strafe/backstep
            Vector3? lookOverride = ActiveLookDirOverride;
            if (!lookOverride.HasValue && profile.StrafeInCombat && __instance is MonsterAI mai)
            {
                Character target = mai.GetTargetCreature();
                if (target != null)
                {
                    Vector3 toTarget = target.transform.position - character.transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.04f && toTarget.sqrMagnitude <= 144.0f) // within 12m
                    {
                        lookOverride = toTarget.normalized;
                    }
                }
            }

            if (lookOverride.HasValue)
            {
                __instance.LookTowards(lookOverride.Value);
                character.SetMoveDir(desiredDir);
                character.SetRun(run);

                if (__instance.m_jumpInterval > 0f && _aiJumpTimer(__instance) >= __instance.m_jumpInterval)
                {
                    _aiJumpTimer(__instance) = 0f;
                    character.Jump();
                }

                return false;
            }

            dir = desiredDir;
            return true;
        }

        [HarmonyPatch(typeof(BaseAI), "MoveTo")]
        [HarmonyPostfix]
        private static void BaseAI_MoveTo_Postfix(BaseAI __instance)
        {
            if (!ModConfig.ModEnabled.Value) return;

            Character character = _aiCharacter(__instance);
            if (character == null || character.IsFlying()) return;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            if (profile.MaxJumpUpHeight <= 0.01f) return;

            List<Vector3> path = _aiPath(__instance);
            if (path == null || path.Count == 0) return;

            Vector3 nextCorner = path[0];
            Vector3 myPos = character.transform.position;
            float deltaY = nextCorner.y - myPos.y;
            float horizDist = Utils.DistanceXZ(nextCorner, myPos);

            if (deltaY > 0.55f && deltaY <= profile.MaxJumpUpHeight && horizDist < 1.9f && character.IsOnGround())
            {
                character.Jump();
            }
        }

        [HarmonyPatch(typeof(BaseAI), "Follow")]
        [HarmonyPrefix]
        private static bool BaseAI_Follow_Prefix(
            BaseAI __instance,
            GameObject go,
            float dt,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!ModConfig.ModEnabled.Value || go == null) return true;

            Character character = _aiCharacter(__instance);
            if (character == null) return true;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);

            // 1. Check if the player is walking into us in a tight corridor/doorway -> step aside
            if (SteeringPipeline.TryGetPlayerYieldStep(character, profile, out Vector3 yieldDir))
            {
                __instance.MoveTowards(yieldDir, run: false);
                return false;
            }

            // 2. Use polar formation slot behind leader instead of converging onto leader's exact origin
            if (ModConfig.EnableFollowerFormations.Value && profile.UseFormationFollowing && _moveToMethod != null)
            {
                Vector3 slotPos = SteeringPipeline.ComputeFollowerSlotPosition(character, go);
                float distToSlot = Utils.DistanceXZ(slotPos, character.transform.position);
                float distToLeader = Vector3.Distance(go.transform.position, character.transform.position);
                bool run = distToLeader > 9.5f || distToSlot > 7.5f;

                if (distToSlot < 1.15f)
                {
                    __instance.StopMoving();
                }
                else
                {
                    _moveToMethod.Invoke(__instance, new object[] { dt, slotPos, 0.5f, run });
                }

                return false;
            }

            return true;
        }
    }
}
