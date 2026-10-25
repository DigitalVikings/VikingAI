using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.Patches;
using VikingAI.Profiles;
using VikingAI.Telemetry;

namespace VikingAI.Tactics
{
    /// <summary>
    /// Coordinates multi-agent melee engagement quotas (Attack Tokens), 45-degree flanking ring slots,
    /// and anti-perch tactical positioning.
    /// </summary>
    public static class CombatPackCoordinator
    {
        private sealed class TokenAllocation
        {
            public Character Holder = null!;
            public float Weight;
            public float GrantedAtTime;
            public float ExpiresAtTime;
        }

        private static readonly List<ITargetScorer> _scorers = new List<ITargetScorer>
        {
            new ProximityThreatScorer()
        };

        private static readonly Dictionary<Character, List<TokenAllocation>> _tokensByTarget =
            new Dictionary<Character, List<TokenAllocation>>(8);

        private static readonly List<Character> _nearbyPackTemp = new List<Character>(16);

        public static void RegisterTargetScorer(ITargetScorer scorer)
        {
            if (scorer == null) return;
            _scorers.RemoveAll(s => s.Id == scorer.Id);
            _scorers.Add(scorer);
        }

        public static float EvaluateTotalTargetScore(BaseAI ai, Character agent, AgentBehaviorProfile profile, Character candidate)
        {
            float total = 0f;
            for (int i = 0; i < _scorers.Count; i++)
            {
                total += _scorers[i].ScoreTarget(ai, agent, profile, candidate);
            }
            return total;
        }

        /// <summary>
        /// Requests or renews a melee Attack Token against <paramref name="target"/>.
        /// Returns true if the agent is authorized to step inside the inner melee ring and attack.
        /// </summary>
        public static bool RequestOrHoldAttackToken(
            Character attacker,
            AgentBehaviorProfile profile,
            Character target)
        {
            if (!profile.UsePackAttackTokens) return true;

            float now = Time.time;
            if (!_tokensByTarget.TryGetValue(target, out List<TokenAllocation> list))
            {
                list = new List<TokenAllocation>(6);
                _tokensByTarget[target] = list;
            }

            float usedBudget = 0f;
            TokenAllocation? existing = null;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                TokenAllocation alloc = list[i];
                if (alloc.Holder == null || alloc.Holder.IsDead() || now > alloc.ExpiresAtTime)
                {
                    list.RemoveAt(i);
                    continue;
                }

                if (alloc.Holder == attacker)
                {
                    existing = alloc;
                }
                else
                {
                    usedBudget += alloc.Weight;
                }
            }

            if (existing != null)
            {
                existing.ExpiresAtTime = now + 2.8f;
                return true;
            }

            float maxBudget = ModConfig.MaxSimultaneousAttackTokens.Value;
            float reqWeight = Mathf.Max(0.5f, profile.AttackTokenWeight);

            if (usedBudget + reqWeight <= maxBudget + 0.01f || list.Count == 0)
            {
                list.Add(new TokenAllocation
                {
                    Holder = attacker,
                    Weight = reqWeight,
                    GrantedAtTime = now,
                    ExpiresAtTime = now + 2.8f
                });
                return true;
            }

            // Preempt a distant holder if this attacker is significantly closer to the target
            float myDistSq = (attacker.transform.position - target.transform.position).sqrMagnitude;
            for (int i = 0; i < list.Count; i++)
            {
                TokenAllocation alloc = list[i];
                if (now - alloc.GrantedAtTime < 1.8f) continue;

                float holderDistSq = (alloc.Holder.transform.position - target.transform.position).sqrMagnitude;
                if (myDistSq + 9.0f < holderDistSq)
                {
                    list[i] = new TokenAllocation
                    {
                        Holder = attacker,
                        Weight = reqWeight,
                        GrantedAtTime = now,
                        ExpiresAtTime = now + 2.8f
                    };
                    return true;
                }
            }

            return false;
        }

        public static Vector3 GetFlankingRingPosition(Character attacker, Character target)
        {
            Vector3 targetPos = target.transform.position;
            _nearbyPackTemp.Clear();
            SpatialCharacterGrid.QueryRadius(targetPos, 18.0f, _nearbyPackTemp, exclude: target);

            int slot = 0;
            int myId = attacker.GetInstanceID();

            for (int i = 0; i < _nearbyPackTemp.Count; i++)
            {
                Character c = _nearbyPackTemp[i];
                if (c != null && c != attacker && c.GetInstanceID() < myId)
                {
                    slot++;
                }
            }

            // 8-slot 45-degree flanking ring with subtle slow orbital drift
            float baseAngle = (slot % 8) * 45.0f;
            float orbitOffset = Mathf.Sin(Time.time * 0.65f + slot * 1.3f) * 22.0f;
            float angleRad = (baseAngle + orbitOffset) * Mathf.Deg2Rad;
            float ringRadius = 4.4f + (slot / 8) * 1.5f + target.GetRadius();

            return targetPos + new Vector3(Mathf.Sin(angleRad) * ringRadius, 0f, Mathf.Cos(angleRad) * ringRadius);
        }
    }

    /// <summary>
    /// IAIBrainModule that manages flanking-ring strafing when waiting for an Attack Token,
    /// and anti-perch fallback positioning when a target is on an unreachable rock/structure.
    /// </summary>
    public sealed class PackFlankBrainModule : IAIBrainModule
    {
        public string Id => "VikingAI.PackFlankAndAntiPerch";

        private static readonly MethodInfo _moveToMethod =
            AccessTools.Method(typeof(BaseAI), "MoveTo");
        private static readonly MethodInfo _havePathMethod =
            AccessTools.Method(typeof(BaseAI), "HavePath");

        public float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile)
        {
            if (ai is not MonsterAI mai) return 0f;

            Character target = mai.GetTargetCreature();
            if (target == null || target.IsDead() || !ai.IsAlerted()) return 0f;

            // Check if the agent has a ranged weapon equipped/available — ranged attackers don't need melee tokens
            if (character is Humanoid hum)
            {
                ItemDrop.ItemData weapon = hum.GetCurrentWeapon();
                if (weapon != null && weapon.m_shared != null && weapon.m_shared.m_aiAttackRange > 5.0f)
                {
                    return 0f;
                }
            }

            // 1. Check Anti-Perch condition: target is > 2.2m above and unreachable by path
            float heightDelta = target.transform.position.y - character.transform.position.y;
            if (heightDelta > 2.2f && _havePathMethod != null)
            {
                bool hasPath = (bool)_havePathMethod.Invoke(ai, new object[] { target.transform.position });
                if (!hasPath)
                {
                    return 75.0f;
                }
            }

            // 2. Check Pack Attack Token quota
            if (!profile.UsePackAttackTokens) return 0f;

            bool hasToken = CombatPackCoordinator.RequestOrHoldAttackToken(character, profile, target);
            if (NavDebugOverlay.IsModeActive(DebugDrawMode.Tokens))
            {
                NavDebugOverlay.AddLabel(
                    character.GetTopPoint() + Vector3.up * 0.25f,
                    hasToken ? "[TOKEN: ENGAGE]" : "[FLANKING RING]",
                    hasToken ? Color.green : Color.cyan);
            }

            if (hasToken)
            {
                return 0f; // Let vanilla MonsterAI.UpdateAI close to melee and execute DoAttack
            }

            return 70.0f; // Hold on the flanking ring and strafe facing the target
        }

        public bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt)
        {
            if (ai is not MonsterAI mai || _moveToMethod == null) return false;
            Character target = mai.GetTargetCreature();
            if (target == null) return false;

            Vector3 toTarget = target.transform.position - character.transform.position;
            toTarget.y = 0f;
            Vector3 lookDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : character.transform.forward;

            // Lock facing onto the target while moving along the flanking ring or backing away from a perch
            LocomotionPatches.ActiveLookDirOverride = lookDir;
            try
            {
                Vector3 flankPos = CombatPackCoordinator.GetFlankingRingPosition(character, target);
                float distToFlank = Utils.DistanceXZ(character.transform.position, flankPos);

                if (distToFlank > 1.1f)
                {
                    _moveToMethod.Invoke(ai, new object[] { dt, flankPos, 0.5f, false });
                }
                else
                {
                    ai.StopMoving();
                    ai.LookTowards(lookDir);
                }
            }
            finally
            {
                LocomotionPatches.ActiveLookDirOverride = null;
            }

            return true;
        }
    }
}
