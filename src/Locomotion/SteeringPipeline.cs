using System.Collections.Generic;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.Profiles;
using VikingAI.Telemetry;

namespace VikingAI.Locomotion
{
    /// <summary>
    /// Orchestrates 16-ray Context Steering contributors and polar follower formation slots.
    /// </summary>
    public static class SteeringPipeline
    {
        private static readonly List<ISteeringContributor> _contributors = new List<ISteeringContributor>
        {
            new PathAndFlowSteeringContributor(),
            new NeighborSeparationContributor(),
            new ObstacleWhiskerContributor(),
            new HazardAvoidanceContributor(),
            new PlayerYieldContributor()
        };

        private static readonly ContextSteeringMap _sharedMap = new ContextSteeringMap();
        private static readonly List<Character> _formationPeers = new List<Character>(16);

        public static void RegisterContributor(ISteeringContributor contributor)
        {
            if (contributor == null) return;
            _contributors.RemoveAll(c => c.Id == contributor.Id);
            _contributors.Add(contributor);
            _contributors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public static Vector3 Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredDir)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableContextSteering.Value)
            {
                return desiredDir;
            }

            _sharedMap.Clear();

            for (int i = 0; i < _contributors.Count; i++)
            {
                _contributors[i].Evaluate(agent, profile, desiredDir, _sharedMap);
            }

            Vector3 resolved = _sharedMap.ResolveBestVector(desiredDir);

            if (NavDebugOverlay.IsModeActive(DebugDrawMode.Steering))
            {
                Vector3 origin = agent.transform.position + Vector3.up * 1.1f;
                for (int i = 0; i < ContextSteeringMap.RayCount; i++)
                {
                    Vector3 rayDir = ContextSteeringMap.Directions[i];
                    float interest = _sharedMap.Interest[i];
                    float danger = _sharedMap.Danger[i];
                    if (danger > 0.05f)
                    {
                        NavDebugOverlay.AddLine(origin, origin + rayDir * (danger * 1.4f), Color.red);
                    }
                    else if (interest > 0.05f)
                    {
                        NavDebugOverlay.AddLine(origin, origin + rayDir * (interest * 1.2f), Color.green);
                    }
                }
                NavDebugOverlay.AddLine(origin, origin + resolved * 1.8f, Color.cyan);
            }

            return resolved;
        }

        /// <summary>
        /// Computes a deterministic polar V-wedge / flank-arc formation slot behind the leader
        /// so multiple tamed followers and companions do not converge onto a single point.
        /// </summary>
        public static Vector3 ComputeFollowerSlotPosition(Character follower, GameObject leaderObj)
        {
            Transform leaderTr = leaderObj.transform;
            Vector3 leaderPos = leaderTr.position;
            Vector3 leaderFwd = leaderTr.forward;
            leaderFwd.y = 0f;
            if (leaderFwd.sqrMagnitude < 0.01f) leaderFwd = Vector3.forward;
            leaderFwd.Normalize();

            _formationPeers.Clear();
            SpatialCharacterGrid.QueryRadius(leaderPos, 24.0f, _formationPeers);

            int slotIndex = 0;
            int myId = follower.GetInstanceID();

            for (int i = 0; i < _formationPeers.Count; i++)
            {
                Character c = _formationPeers[i];
                if (c == null || c == follower) continue;
                if (c.GetBaseAI() is MonsterAI mai && mai.GetFollowTarget() == leaderObj)
                {
                    if (c.GetInstanceID() < myId)
                    {
                        slotIndex++;
                    }
                }
            }

            // Alternate left/right in a widening V-wedge behind the leader
            int rank = (slotIndex / 2) + 1;
            int side = (slotIndex % 2 == 0) ? -1 : 1;
            float angleDeg = 180f + side * Mathf.Min(75f, 32f + rank * 14f);
            float radius = 2.6f + (rank - 1) * 1.6f + Mathf.Max(0f, follower.GetRadius() - 0.5f);

            Quaternion rot = Quaternion.Euler(0f, angleDeg, 0f);
            Vector3 offset = rot * leaderFwd * radius;
            return leaderPos + offset;
        }

        /// <summary>
        /// Evaluates whether an idle/following friendly NPC or tamed animal is blocking an approaching player
        /// and returns a lateral step-aside direction if so.
        /// </summary>
        public static bool TryGetPlayerYieldStep(Character agent, AgentBehaviorProfile profile, out Vector3 yieldDir)
        {
            yieldDir = Vector3.zero;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnablePlayerYieldOnApproach.Value || !profile.YieldToPlayers)
            {
                return false;
            }

            Player player = Player.GetClosestPlayer(agent.transform.position, 2.4f);
            if (player == null) return false;

            Vector3 toAgent = agent.transform.position - player.transform.position;
            toAgent.y = 0f;
            float dist = toAgent.magnitude;
            if (dist < 0.05f || dist > 2.2f) return false;

            Vector3 playerVel = player.GetVelocity();
            playerVel.y = 0f;
            bool playerMovingTowards = playerVel.sqrMagnitude > 0.25f &&
                                      Vector3.Dot(playerVel.normalized, toAgent / dist) > 0.35f;

            if (!playerMovingTowards && dist > 1.35f)
            {
                return false;
            }

            Vector3 radialOut = toAgent / dist;
            Vector3 lateral = Vector3.Cross(Vector3.up, radialOut);
            if (playerVel.sqrMagnitude > 0.1f && Vector3.Dot(lateral, playerVel) > 0f)
            {
                lateral = -lateral;
            }

            yieldDir = (radialOut * 0.55f + lateral * 0.85f).normalized;
            return true;
        }
    }
}
