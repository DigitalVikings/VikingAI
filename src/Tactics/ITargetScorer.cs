using UnityEngine;
using VikingAI.Profiles;

namespace VikingAI.Tactics
{
    /// <summary>
    /// Open extension contract for evaluating target threat and selection priority.
    /// Downstream mods can register custom threat/taunt/aggro scorers via CombatPackCoordinator.RegisterTargetScorer.
    /// </summary>
    public interface ITargetScorer
    {
        string Id { get; }
        float ScoreTarget(BaseAI ai, Character agent, AgentBehaviorProfile profile, Character candidateTarget);
    }

    public sealed class ProximityThreatScorer : ITargetScorer
    {
        public string Id => "VikingAI.ProximityThreat";

        public float ScoreTarget(BaseAI ai, Character agent, AgentBehaviorProfile profile, Character candidateTarget)
        {
            if (candidateTarget == null || candidateTarget.IsDead()) return -1000f;

            float dist = Vector3.Distance(agent.transform.position, candidateTarget.transform.position);
            float score = Mathf.Max(0f, 60.0f - dist);

            if (ai is MonsterAI mai && mai.GetTargetCreature() == candidateTarget)
            {
                score += 12.0f; // Hysteresis bonus to prevent rapid target thrashing
            }

            if (candidateTarget.GetHealthPercentage() < 0.25f)
            {
                score += 8.0f;
            }

            return score;
        }
    }
}
