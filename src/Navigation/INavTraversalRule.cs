using UnityEngine;
using VikingAI.Profiles;

namespace VikingAI.Navigation
{
    /// <summary>
    /// Open extension contract for evaluating whether an agent can traverse between two adjacent 2.5D NavCells
    /// and computing the traversal cost penalty. Downstream mods can register custom rules via NavGraphManager.RegisterTraversalRule.
    /// </summary>
    public interface INavTraversalRule
    {
        string Id { get; }

        /// <summary>
        /// Evaluates whether <paramref name="agent"/> with <paramref name="profile"/> can step from <paramref name="from"/> to <paramref name="to"/>.
        /// Returns true if traversable, populating <paramref name="costMultiplier"/> (1.0 = normal ground cost).
        /// </summary>
        bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier);
    }

    public sealed class GroundWalkRule : INavTraversalRule
    {
        public string Id => "VikingAI.GroundWalk";

        public bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier)
        {
            costMultiplier = 1.0f;
            if (!to.IsWalkable || to.IsDoorway || to.IsDestructible) return false;

            float deltaY = to.FloorY - from.FloorY;
            if (Mathf.Abs(deltaY) > 0.65f) return false;

            if ((to.Flags & NavSurfaceFlags.Hazard) != 0)
            {
                costMultiplier = 8.0f;
            }
            else if ((to.Flags & NavSurfaceFlags.SteepSlope) != 0)
            {
                costMultiplier = 1.6f;
            }

            return true;
        }
    }

    public sealed class WaterSwimRule : INavTraversalRule
    {
        public string Id => "VikingAI.WaterSwim";

        public bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier)
        {
            costMultiplier = 3.5f;
            if (!to.IsWater) return false;

            if (agent != null)
            {
                BaseAI ai = agent.GetBaseAI();
                if (ai != null && (ai.m_pathAgentType == Pathfinding.AgentType.HumanoidNoSwim ||
                                   ai.m_pathAgentType == Pathfinding.AgentType.HumanoidBigNoSwim))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class DoorTraversalRule : INavTraversalRule
    {
        public string Id => "VikingAI.DoorTraversal";

        public bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier)
        {
            costMultiplier = 1.25f;
            if (!to.IsDoorway) return false;

            float deltaY = Mathf.Abs(to.FloorY - from.FloorY);
            if (deltaY > 1.2f) return false;

            return profile.CanOpenDoors;
        }
    }

    public sealed class LedgeJumpRule : INavTraversalRule
    {
        public string Id => "VikingAI.LedgeJump";

        public bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier)
        {
            costMultiplier = 2.0f;
            if (!to.IsWalkable) return false;

            float deltaY = to.FloorY - from.FloorY;
            if (deltaY > 0.65f && deltaY <= profile.MaxJumpUpHeight)
            {
                costMultiplier = 2.2f;
                return true;
            }

            if (deltaY < -0.65f && -deltaY <= profile.MaxDropDownHeight)
            {
                costMultiplier = 1.5f;
                return true;
            }

            return false;
        }
    }

    public sealed class SiegeBreachRule : INavTraversalRule
    {
        public string Id => "VikingAI.SiegeBreach";

        public bool TryEvaluateEdge(
            Character? agent,
            AgentBehaviorProfile profile,
            in NavCell from,
            in NavCell to,
            float horizontalDistance,
            out float costMultiplier)
        {
            costMultiplier = 1.0f;
            if (!to.IsDestructible || !profile.AllowSiegeBreachPathing) return false;

            float dps = Mathf.Max(5.0f, profile.StructuralDpsEstimate);
            float breachPenalty = to.StructuralHp / dps;
            costMultiplier = 2.5f + breachPenalty;
            return true;
        }
    }
}
