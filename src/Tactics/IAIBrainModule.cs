using VikingAI.Profiles;

namespace VikingAI.Tactics
{
    /// <summary>
    /// Open extension contract for modular utility-scored AI brain layers.
    /// Evaluated by BrainPipeline before falling back to vanilla MonsterAI.UpdateAI (Priority 0).
    /// Downstream mods can register custom modules (e.g. Boss phases, Bard aura, Dialogues) via BrainPipeline.RegisterModule.
    /// </summary>
    public interface IAIBrainModule
    {
        string Id { get; }

        /// <summary>
        /// Returns a utility score &gt; 0 if this module wants to claim control of the agent on this tick,
        /// or &lt;= 0 to yield to lower-scored modules or vanilla MonsterAI.UpdateAI.
        /// </summary>
        float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile);

        /// <summary>
        /// Executes the module's behavior for the current tick.
        /// Return true to skip vanilla MonsterAI's default movement/attack block for this tick,
        /// or false to allow vanilla MonsterAI.UpdateAI to proceed after this pre-processing step.
        /// </summary>
        bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt);
    }
}
