using System.Collections.Generic;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.FlowField;
using VikingAI.Profiles;

namespace VikingAI.Locomotion
{
    /// <summary>
    /// Open extension contract for modular 16-ray Context Steering contributors.
    /// Downstream mods can register custom steering forces via SteeringPipeline.RegisterContributor.
    /// </summary>
    public interface ISteeringContributor
    {
        string Id { get; }
        int Priority { get; }
        void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map);
    }

    public sealed class PathAndFlowSteeringContributor : ISteeringContributor
    {
        public string Id => "VikingAI.PathAndFlow";
        public int Priority => 10;

        public void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map)
        {
            map.AddDirectionalInterest(desiredMoveDir, 1.0f, -0.2f);

            // Blend tactical flow field when pursuing a target in a pack
            if (profile.UsePackAttackTokens && agent.GetBaseAI() is MonsterAI mai)
            {
                Character target = mai.GetTargetCreature();
                if (target != null)
                {
                    TacticalFlowField field = TacticalFlowField.GetOrUpdateForTarget(target);
                    if (field.TrySampleFlow(agent.transform.position, out Vector3 flowDir))
                    {
                        map.AddDirectionalInterest(flowDir, 0.45f, 0.0f);
                    }
                }
            }
        }
    }

    public sealed class NeighborSeparationContributor : ISteeringContributor
    {
        public string Id => "VikingAI.NeighborSeparation";
        public int Priority => 20;

        private static readonly List<Character> _neighbors = new List<Character>(16);

        public void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map)
        {
            if (!ModConfig.EnableCrowdSeparation.Value) return;

            float agentRadius = Mathf.Max(0.45f, agent.GetRadius());
            float queryRadius = agentRadius * 3.2f;
            Vector3 myPos = agent.transform.position;

            _neighbors.Clear();
            SpatialCharacterGrid.QueryRadius(myPos, queryRadius, _neighbors, exclude: agent);

            for (int i = 0; i < _neighbors.Count; i++)
            {
                Character other = _neighbors[i];
                if (other == null || other is Player) continue;

                Vector3 toOther = other.transform.position - myPos;
                toOther.y = 0f;
                float dist = toOther.magnitude;
                float minSep = agentRadius + Mathf.Max(0.45f, other.GetRadius()) + 0.35f;

                if (dist > 0.01f && dist < minSep)
                {
                    float urgency = Mathf.Clamp01(1.0f - (dist / minSep)) * 0.85f;
                    map.AddDirectionalDanger(toOther / dist, urgency, 0.25f);
                }
            }
        }
    }

    public sealed class ObstacleWhiskerContributor : ISteeringContributor
    {
        public string Id => "VikingAI.ObstacleWhisker";
        public int Priority => 30;

        public void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map)
        {
            if (Pathfinding.instance == null) return;
            int solidMask = Pathfinding.instance.m_layers.value;

            Vector3 origin = agent.transform.position + Vector3.up * 0.85f;
            Vector3 fwd = desiredMoveDir;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = agent.transform.forward;
            fwd.Normalize();

            Vector3 leftWhisker = Quaternion.Euler(0f, -30f, 0f) * fwd;
            Vector3 rightWhisker = Quaternion.Euler(0f, 30f, 0f) * fwd;

            ProbeWhisker(origin, leftWhisker, 1.4f, solidMask, map);
            ProbeWhisker(origin, rightWhisker, 1.4f, solidMask, map);
        }

        private static void ProbeWhisker(
            Vector3 origin,
            Vector3 dir,
            float length,
            int mask,
            ContextSteeringMap map)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, length, mask))
            {
                if (hit.collider != null && hit.collider.GetComponentInParent<Door>() != null)
                {
                    return;
                }

                float closeness = 1.0f - (hit.distance / length);
                map.AddDirectionalDanger(dir, closeness * 0.75f, 0.5f);
            }
        }
    }

    public sealed class HazardAvoidanceContributor : ISteeringContributor
    {
        public string Id => "VikingAI.HazardAvoidance";
        public int Priority => 40;

        public void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map)
        {
            Vector3 myPos = agent.transform.position;
            foreach (Fireplace fp in PieceRegistry.Fireplaces)
            {
                if (fp == null || !fp.IsBurning()) continue;
                Vector3 toFire = fp.transform.position - myPos;
                toFire.y = 0f;
                float distSq = toFire.sqrMagnitude;
                if (distSq < 9.0f && distSq > 0.01f)
                {
                    float dist = Mathf.Sqrt(distSq);
                    float danger = Mathf.Clamp01(1.0f - (dist / 3.0f)) * 0.95f;
                    map.AddDirectionalDanger(toFire / dist, danger, 0.2f);
                }
            }
        }
    }

    public sealed class PlayerYieldContributor : ISteeringContributor
    {
        public string Id => "VikingAI.PlayerYield";
        public int Priority => 50;

        public void Evaluate(
            Character agent,
            AgentBehaviorProfile profile,
            Vector3 desiredMoveDir,
            ContextSteeringMap map)
        {
            if (!ModConfig.EnablePlayerYieldOnApproach.Value || !profile.YieldToPlayers) return;

            Player closest = Player.GetClosestPlayer(agent.transform.position, 2.8f);
            if (closest == null) return;

            Vector3 toPlayer = closest.transform.position - agent.transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            if (dist < 0.05f || dist > 2.6f) return;

            map.AddDirectionalDanger(toPlayer / dist, 0.9f, 0.25f);
        }
    }
}
