using VikingAI.Profiles;

namespace VikingAI.Simulation
{
    /// <summary>
    /// Open extension contract for 1 Hz background ZDO simulation and deterministic catch-up
    /// when a registered NPC or patrol is in an unloaded zone.
    /// Downstream mods can register custom tasks via VirtualAgentDirector.RegisterTask.
    /// </summary>
    public interface IVirtualAgentTask
    {
        string Id { get; }
        NpcRoleFlags RequiredRole { get; }

        /// <summary>
        /// Executed at the virtual tick interval (default 1 Hz) while the agent's ZDO is in an unloaded sector.
        /// </summary>
        void TickUnloadedZdo(ZDO agentZdo, NpcRoleFlags roles, float dtSeconds);

        /// <summary>
        /// Executed when an NPC's zone reloads after being unloaded for <paramref name="elapsedOfflineSeconds"/>.
        /// </summary>
        void ApplyCatchUpOnLoad(Character agent, AgentBehaviorProfile profile, double elapsedOfflineSeconds);
    }

    public sealed class PatrolVirtualTask : IVirtualAgentTask
    {
        public string Id => "VikingAI.VirtualPatrol";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.Guard;

        private static readonly int _zdoPatrolCenter = "VAI_v1_PatrolPos".GetStableHashCode();
        private static readonly int _zdoPatrolPhase = "VAI_v1_PatrolPhase".GetStableHashCode();

        public void TickUnloadedZdo(ZDO agentZdo, NpcRoleFlags roles, float dtSeconds)
        {
            if (!agentZdo.IsValid() || !agentZdo.IsOwner()) return;

            UnityEngine.Vector3 center = agentZdo.GetVec3(_zdoPatrolCenter, agentZdo.GetPosition());
            float phase = agentZdo.GetFloat(_zdoPatrolPhase, 0f) + dtSeconds * 0.25f;
            agentZdo.Set(_zdoPatrolPhase, phase);

            // Orbit a 6m guard circuit around the patrol anchor in ZDO space
            float rx = UnityEngine.Mathf.Sin(phase) * 6.0f;
            float rz = UnityEngine.Mathf.Cos(phase) * 6.0f;
            UnityEngine.Vector3 nextPos = new UnityEngine.Vector3(center.x + rx, center.y, center.z + rz);
            agentZdo.SetPosition(nextPos);
        }

        public void ApplyCatchUpOnLoad(Character agent, AgentBehaviorProfile profile, double elapsedOfflineSeconds)
        {
            if (elapsedOfflineSeconds > 30.0 && !agent.IsDead() && agent.GetHealthPercentage() < 1.0f)
            {
                // Passive out-of-combat guard recovery while unloaded
                agent.Heal(agent.GetMaxHealth() * 0.25f);
            }
        }
    }
}
