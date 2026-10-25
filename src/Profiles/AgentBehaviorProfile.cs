using System;
using VikingAI.Configuration;

namespace VikingAI.Profiles
{
    [Flags]
    public enum NpcRoleFlags : uint
    {
        None          = 0,
        Companion     = 1 << 0,
        Guard         = 1 << 1,
        SmelterTender = 1 << 2,
        Lumberjack    = 1 << 3,
        Farmer        = 1 << 4,
        Builder       = 1 << 5,
        Hauler        = 1 << 6,
        AllSettlement = Guard | SmelterTender | Lumberjack | Farmer | Builder | Hauler
    }

    /// <summary>
    /// Declarative capability profile governing how VikingAI treats a given creature prefab or instance.
    /// Guarantees vanilla-equivalent behavior at baseline unless specific capabilities are enabled
    /// via downstream mod registration, instance overrides, or user configuration toggles.
    /// </summary>
    public sealed class AgentBehaviorProfile
    {
        public string PrefabName { get; set; } = "";
        public bool IsCustomNpc { get; set; }
        public NpcRoleFlags Roles { get; set; } = NpcRoleFlags.None;

        // Navigation & Door Capabilities
        public bool CanOpenDoors { get; set; }
        public bool AutoCloseDoors { get; set; }
        public float MaxJumpUpHeight { get; set; }
        public float MaxDropDownHeight { get; set; }
        public bool AllowSiegeBreachPathing { get; set; }
        public float StructuralDpsEstimate { get; set; } = 25.0f;

        // Locomotion & Follower Capabilities
        public bool StrafeInCombat { get; set; }
        public bool YieldToPlayers { get; set; }
        public bool UseFormationFollowing { get; set; }
        public bool CanTraversePortals { get; set; }

        // Tactical & Sensory Capabilities
        public bool UsePackAttackTokens { get; set; }
        public float AttackTokenWeight { get; set; } = 1.0f;
        public bool UseSensoryEngine { get; set; }
        public float CallForHelpRadius { get; set; } = 20.0f;

        public AgentBehaviorProfile Clone()
        {
            return (AgentBehaviorProfile)MemberwiseClone();
        }

        /// <summary>
        /// Creates a standard profile for a friendly humanoid NPC or companion with full door,
        /// formation, yielding, strafing, and sensory capabilities enabled.
        /// </summary>
        public static AgentBehaviorProfile CreateCompanionDefault(string prefabName = "")
        {
            return new AgentBehaviorProfile
            {
                PrefabName = prefabName,
                IsCustomNpc = true,
                Roles = NpcRoleFlags.Companion | NpcRoleFlags.Guard,
                CanOpenDoors = true,
                AutoCloseDoors = true,
                MaxJumpUpHeight = 1.5f,
                MaxDropDownHeight = 3.0f,
                AllowSiegeBreachPathing = false,
                StrafeInCombat = true,
                YieldToPlayers = true,
                UseFormationFollowing = true,
                CanTraversePortals = true,
                UsePackAttackTokens = true,
                AttackTokenWeight = 1.0f,
                UseSensoryEngine = true,
                CallForHelpRadius = 25.0f
            };
        }

        /// <summary>
        /// Creates a standard profile for a settlement villager/worker NPC.
        /// </summary>
        public static AgentBehaviorProfile CreateVillagerDefault(string prefabName = "", NpcRoleFlags roles = NpcRoleFlags.AllSettlement)
        {
            return new AgentBehaviorProfile
            {
                PrefabName = prefabName,
                IsCustomNpc = true,
                Roles = roles,
                CanOpenDoors = true,
                AutoCloseDoors = true,
                MaxJumpUpHeight = 1.2f,
                MaxDropDownHeight = 2.5f,
                AllowSiegeBreachPathing = false,
                StrafeInCombat = true,
                YieldToPlayers = true,
                UseFormationFollowing = true,
                CanTraversePortals = false,
                UsePackAttackTokens = true,
                AttackTokenWeight = 1.0f,
                UseSensoryEngine = true,
                CallForHelpRadius = 30.0f
            };
        }
    }
}
