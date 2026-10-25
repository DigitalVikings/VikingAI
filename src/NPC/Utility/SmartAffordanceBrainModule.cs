using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingAI.NPC.Affordances;
using VikingAI.Profiles;
using VikingAI.Tactics;

namespace VikingAI.NPC.Utility
{
    /// <summary>
    /// IAIBrainModule that drives autonomous settlement work (smelter tending, fire refueling,
    /// structural repair, crop harvesting, and evening rest) for NPCs with NpcRoleFlags when not in combat.
    /// </summary>
    public sealed class SmartAffordanceBrainModule : IAIBrainModule
    {
        private sealed class AgentWorkState
        {
            public AffordanceCandidate CurrentCandidate;
            public bool HasActiveTask;
            public float WorkProgressSeconds;
            public float NextScanTime;
        }

        public string Id => "VikingAI.SmartAffordanceWork";

        private static readonly ConditionalWeakTable<Character, AgentWorkState> _workStates =
            new ConditionalWeakTable<Character, AgentWorkState>();

        private static readonly MethodInfo _moveToMethod =
            AccessTools.Method(typeof(BaseAI), "MoveTo");
        private static readonly MethodInfo _lookAtMethod =
            AccessTools.Method(typeof(BaseAI), "LookAt");

        public float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile)
        {
            if (profile.Roles == NpcRoleFlags.None) return 0f;

            // Never interrupt active combat or active player following outside a base
            if (ai.IsAlerted()) return 0f;
            if (ai is MonsterAI mai)
            {
                if (mai.GetTargetCreature() != null) return 0f;
                GameObject follow = mai.GetFollowTarget();
                if (follow != null && Vector3.Distance(follow.transform.position, character.transform.position) > 6.5f)
                {
                    return 0f;
                }
            }

            if (!_workStates.TryGetValue(character, out AgentWorkState state))
            {
                state = new AgentWorkState();
                _workStates.Add(character, state);
            }

            float now = Time.time;
            if (state.HasActiveTask)
            {
                if (state.CurrentCandidate.TargetComponent == null ||
                    AffordanceRegistry.IsReservedByOther(state.CurrentCandidate.TargetComponent, character))
                {
                    state.HasActiveTask = false;
                }
                else
                {
                    return 35.0f;
                }
            }

            if (now < state.NextScanTime)
            {
                return 0f;
            }

            state.NextScanTime = now + 2.5f;
            if (AffordanceRegistry.TrySelectBestTask(character, profile, 28.0f, out AffordanceCandidate candidate))
            {
                if (AffordanceRegistry.TryReserve(candidate.TargetComponent, character, 15.0f))
                {
                    state.CurrentCandidate = candidate;
                    state.HasActiveTask = true;
                    state.WorkProgressSeconds = 0f;
                    return 35.0f;
                }
            }

            return 0f;
        }

        public bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt)
        {
            if (!_workStates.TryGetValue(character, out AgentWorkState state) || !state.HasActiveTask)
            {
                return false;
            }

            AffordanceCandidate task = state.CurrentCandidate;
            if (task.TargetComponent == null)
            {
                state.HasActiveTask = false;
                return false;
            }

            AffordanceRegistry.TryReserve(task.TargetComponent, character, 10.0f);

            float dist = Utils.DistanceXZ(character.transform.position, task.InteractionPoint);
            if (dist > 2.1f)
            {
                _moveToMethod?.Invoke(ai, new object[] { dt, task.InteractionPoint, 1.5f, false });
                return true;
            }

            ai.StopMoving();
            _lookAtMethod?.Invoke(ai, new object[] { task.InteractionPoint });

            state.WorkProgressSeconds += dt;
            if (state.WorkProgressSeconds >= task.WorkDurationSeconds)
            {
                IAffordanceProvider? provider = AffordanceRegistry.GetProvider(task.ProviderId);
                provider?.CompleteInteraction(character, profile, in task);
                AffordanceRegistry.ReleaseReservation(task.TargetComponent, character);
                state.HasActiveTask = false;
                state.NextScanTime = Time.time + 2.0f;
            }

            return true;
        }
    }
}
