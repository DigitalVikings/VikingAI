using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VikingAI.Configuration;

namespace VikingAI.Profiles
{
    /// <summary>
    /// Central registry mapping creature prefabs and individual runtime Character instances to their AgentBehaviorProfile.
    /// Dynamically applies baseline vanilla-equivalence or opt-in config toggles for unregistered vanilla creatures.
    /// </summary>
    public static class AgentProfileRegistry
    {
        public static readonly int ZdoKeyRoleFlags = "VAI_v1_Roles".GetStableHashCode();
        public static readonly int ZdoKeyCustomNpc = "VAI_v1_IsNpc".GetStableHashCode();
        public static readonly int ZdoKeyPerceptionExempt = "VAI_PerceptionExempt".GetStableHashCode();
        public static readonly int ZdoKeySoMExempt = "SoMStealthExempt".GetStableHashCode();

        private static readonly AccessTools.FieldRef<Character, ZNetView> _charNView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        private static readonly Dictionary<string, AgentBehaviorProfile> _prefabProfiles =
            new Dictionary<string, AgentBehaviorProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly ConditionalWeakTable<Character, AgentBehaviorProfile> _instanceProfiles =
            new ConditionalWeakTable<Character, AgentBehaviorProfile>();

        public static ZNetView? GetNView(Character? character)
        {
            return character != null ? _charNView(character) : null;
        }

        /// <summary>
        /// Registers a default behavior profile for a specific prefab name (e.g. "Skeleton", "Dverger", or custom NPC prefab).
        /// </summary>
        public static void RegisterPrefabProfile(string prefabName, AgentBehaviorProfile profile)
        {
            if (string.IsNullOrEmpty(prefabName) || profile == null) return;
            profile.PrefabName = prefabName;
            _prefabProfiles[CleanName(prefabName)] = profile;
        }

        /// <summary>
        /// Assigns a custom runtime profile directly to a specific Character instance (and optionally persists NPC flags on its ZDO).
        /// </summary>
        public static void SetInstanceProfile(Character character, AgentBehaviorProfile profile, bool persistRoleToZdo = false)
        {
            if (character == null || profile == null) return;
            _instanceProfiles.Remove(character);
            _instanceProfiles.Add(character, profile);

            ZNetView? nview = GetNView(character);
            if (persistRoleToZdo && nview != null && nview.IsValid() && nview.IsOwner())
            {
                ZDO zdo = nview.GetZDO();
                zdo.Set(ZdoKeyCustomNpc, profile.IsCustomNpc);
                zdo.Set(ZdoKeyRoleFlags, (int)profile.Roles);
            }
        }

        /// <summary>
        /// Resolves the effective AgentBehaviorProfile for a character, combining any explicit instance/prefab registration
        /// with live tamed status and server-synced vanilla enhancement toggles.
        /// </summary>
        public static AgentBehaviorProfile GetProfile(Character character)
        {
            if (character == null)
            {
                return new AgentBehaviorProfile();
            }

            if (_instanceProfiles.TryGetValue(character, out var existing))
            {
                SyncDynamicTamedFlags(character, existing);
                return existing;
            }

            string prefabName = CleanName(character.gameObject.name);
            AgentBehaviorProfile resolved;

            if (_prefabProfiles.TryGetValue(prefabName, out var template))
            {
                resolved = template.Clone();
            }
            else
            {
                resolved = BuildDynamicDefaultProfile(character, prefabName);
            }

            // Check if ZDO marks this entity as a persistent NPC/Villager across reloads
            ZNetView? nview = GetNView(character);
            if (nview != null && nview.IsValid())
            {
                ZDO zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool(ZdoKeyCustomNpc))
                {
                    int roleBits = zdo.GetInt(ZdoKeyRoleFlags);
                    resolved = AgentBehaviorProfile.CreateVillagerDefault(prefabName, (NpcRoleFlags)roleBits);
                    if ((resolved.Roles & NpcRoleFlags.Companion) != 0)
                    {
                        resolved.CanTraversePortals = true;
                    }
                }
            }

            SyncDynamicTamedFlags(character, resolved);
            _instanceProfiles.Add(character, resolved);
            return resolved;
        }

        public static bool IsPerceptionExempt(Character character)
        {
            ZNetView? nview = GetNView(character);
            if (nview == null || !nview.IsValid()) return false;
            ZDO zdo = nview.GetZDO();
            if (zdo == null) return false;
            return zdo.GetInt(ZdoKeyPerceptionExempt) != 0 || zdo.GetInt(ZdoKeySoMExempt) != 0;
        }

        private static AgentBehaviorProfile BuildDynamicDefaultProfile(Character character, string prefabName)
        {
            bool isHumanoid = character is Humanoid;
            BaseAI baseAi = character.GetBaseAI();
            Pathfinding.AgentType agentType = baseAi != null ? baseAi.m_pathAgentType : Pathfinding.AgentType.Humanoid;

            float tokenWeight = (agentType == Pathfinding.AgentType.TrollSize ||
                                 agentType == Pathfinding.AgentType.Abomination ||
                                 agentType == Pathfinding.AgentType.HugeSize ||
                                 agentType == Pathfinding.AgentType.GoblinBruteSize) ? 1.5f : 1.0f;

            return new AgentBehaviorProfile
            {
                PrefabName = prefabName,
                IsCustomNpc = false,
                Roles = NpcRoleFlags.None,
                CanOpenDoors = false,
                AutoCloseDoors = false,
                MaxJumpUpHeight = ModConfig.VanillaEnemiesJumpLedges.Value ? 1.4f : 0f,
                MaxDropDownHeight = ModConfig.VanillaEnemiesJumpLedges.Value ? 2.5f : 0f,
                AllowSiegeBreachPathing = ModConfig.VanillaEnemiesBreachWeakWalls.Value,
                StrafeInCombat = isHumanoid && ModConfig.VanillaHumanoidsStrafeInCombat.Value,
                YieldToPlayers = character.IsTamed(),
                UseFormationFollowing = character.IsTamed(),
                CanTraversePortals = false,
                UsePackAttackTokens = ModConfig.VanillaEnemiesUsePackTokens.Value,
                AttackTokenWeight = tokenWeight,
                UseSensoryEngine = ModConfig.VanillaEnemiesUseSensoryEngine.Value,
                CallForHelpRadius = 20.0f
            };
        }

        private static void SyncDynamicTamedFlags(Character character, AgentBehaviorProfile profile)
        {
            if (character.IsTamed())
            {
                profile.YieldToPlayers = ModConfig.EnablePlayerYieldOnApproach.Value;
                profile.UseFormationFollowing = ModConfig.EnableFollowerFormations.Value;
            }
            else if (!profile.IsCustomNpc)
            {
                profile.UsePackAttackTokens = ModConfig.VanillaEnemiesUsePackTokens.Value;
                profile.StrafeInCombat = (character is Humanoid) && ModConfig.VanillaHumanoidsStrafeInCombat.Value;
                profile.MaxJumpUpHeight = ModConfig.VanillaEnemiesJumpLedges.Value ? 1.4f : 0f;
                profile.AllowSiegeBreachPathing = ModConfig.VanillaEnemiesBreachWeakWalls.Value;
                profile.UseSensoryEngine = ModConfig.VanillaEnemiesUseSensoryEngine.Value;
            }
        }

        private static string CleanName(string name)
        {
            int idx = name.IndexOf('(');
            return idx > 0 ? name.Substring(0, idx).Trim() : name.Trim();
        }
    }
}
