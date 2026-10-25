using UnityEngine;
using VikingAI.Profiles;

namespace VikingAI.NPC.Affordances
{
    public struct AffordanceCandidate
    {
        public string ProviderId;
        public Component TargetComponent;
        public Vector3 InteractionPoint;
        public float UtilityScore;
        public string AnimationTrigger;
        public float WorkDurationSeconds;
    }

    /// <summary>
    /// Open extension contract for Smart Object settlement and camp affordances.
    /// Downstream mods can register custom crafting stations, guard posts, or furniture via AffordanceRegistry.RegisterProvider.
    /// </summary>
    public interface IAffordanceProvider
    {
        string Id { get; }
        NpcRoleFlags RequiredRole { get; }

        /// <summary>
        /// Evaluates the best available smart-object task within <paramref name="searchRadius"/> for <paramref name="agent"/>.
        /// </summary>
        bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate);

        /// <summary>
        /// Executes the atomic interaction when <paramref name="agent"/> finishes working at the affordance point.
        /// </summary>
        void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate);
    }
}
