using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using VikingAI.Profiles;

namespace VikingAI.NPC.Prefabs
{
    public sealed class HumanoidPrefabConfig
    {
        public string PrefabName { get; set; } = "VAI_HumanoidNpc";
        public string DisplayName { get; set; } = "Wayfarer";
        public Character.Faction Faction { get; set; } = Character.Faction.Players;
        public string Group { get; set; } = "VAI_Exiles";
        public float MaxHealth { get; set; } = 120f;
        public bool IsAggravatable { get; set; } = true;
        public bool AttackPlayerObjects { get; set; }
        public string[] DefaultItemNames { get; set; } = { "ArmorRagsChest", "ArmorRagsLegs" };
        public string[] RandomWeaponNames { get; set; } = Array.Empty<string>();
        public string[] RandomShieldNames { get; set; } = Array.Empty<string>();
        public AgentBehaviorProfile? BehaviorProfile { get; set; }
    }

    /// <summary>
    /// Reusable Zero-Asset Humanoid NPC Prefab Factory.
    /// Clones the vanilla "Player" rig during PrefabManager.OnVanillaPrefabsAvailable, strips Player-specific
    /// components, attaches Humanoid + MonsterAI + NpcTalk + VisEquipment, and registers the prefab with Jötunn
    /// and AgentProfileRegistry.
    /// </summary>
    public static class HumanoidPrefabBuilder
    {
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> _animEventChar =
            AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");

        private static readonly Vector3[] _skinTones =
        {
            new Vector3(1.00f, 0.92f, 0.84f),
            new Vector3(0.95f, 0.85f, 0.74f),
            new Vector3(0.88f, 0.76f, 0.65f),
            new Vector3(0.76f, 0.64f, 0.54f)
        };

        private static readonly Vector3[] _hairTones =
        {
            new Vector3(0.90f, 0.78f, 0.45f), // Blonde
            new Vector3(0.75f, 0.36f, 0.18f), // Auburn / Red
            new Vector3(0.38f, 0.26f, 0.16f), // Chestnut Brown
            new Vector3(0.16f, 0.14f, 0.12f), // Raven Black
            new Vector3(0.78f, 0.78f, 0.80f)  // Silver / Grey
        };

        /// <summary>
        /// Clones the vanilla "Player" prefab into a standalone MonsterAI-driven Humanoid NPC prefab
        /// and registers it with Jötunn PrefabManager and VikingAI's AgentProfileRegistry.
        /// Must be called inside or after PrefabManager.OnVanillaPrefabsAvailable.
        /// </summary>
        public static GameObject? CreateHumanoidNpcPrefab(HumanoidPrefabConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.PrefabName)) return null;

            GameObject existing = PrefabManager.Instance.GetPrefab(config.PrefabName);
            if (existing != null) return existing;

            GameObject playerTemplate = PrefabManager.Instance.GetPrefab("Player");
            GameObject dvergrTemplate = PrefabManager.Instance.GetPrefab("Dverger");
            if (playerTemplate == null || dvergrTemplate == null)
            {
                VikingAIPlugin.Log.LogError($"[HumanoidPrefabBuilder] Cannot build '{config.PrefabName}': Player or Dverger template missing.");
                return null;
            }

            GameObject go = PrefabManager.Instance.CreateClonedPrefab(config.PrefabName, playerTemplate);
            go.tag = "Untagged";

            Player oldPlayer = go.GetComponent<Player>();
            CapsuleCollider col = go.GetComponent<CapsuleCollider>();
            if (col != null)
            {
                col.radius = Mathf.Min(col.radius, 0.35f);
            }

            Humanoid hum = go.AddComponent<Humanoid>();
            if (oldPlayer != null)
            {
                CopyDeclaredFields(oldPlayer, hum, typeof(Character));
                CopyDeclaredFields(oldPlayer, hum, typeof(Humanoid));
            }

            hum.m_name = config.DisplayName;
            hum.m_faction = config.Faction;
            hum.m_group = config.Group;
            hum.m_health = config.MaxHealth;
            hum.m_boss = false;
            hum.m_defaultItems = ResolvePrefabs(config.DefaultItemNames);
            hum.m_randomWeapon = ResolvePrefabs(config.RandomWeaponNames);
            hum.m_randomShield = ResolvePrefabs(config.RandomShieldNames);
            hum.m_randomArmor = Array.Empty<GameObject>();
            hum.m_randomSets = Array.Empty<Humanoid.ItemSet>();
            hum.m_randomItems = Array.Empty<Humanoid.RandomItem>();

            CharacterAnimEvent animEvent = go.GetComponentInChildren<CharacterAnimEvent>();
            if (animEvent != null)
            {
                _animEventChar(animEvent) = hum;
            }

            MonsterAI ai = go.AddComponent<MonsterAI>();
            MonsterAI dvergrAi = dvergrTemplate.GetComponent<MonsterAI>();
            if (dvergrAi != null)
            {
                CopyDeclaredFields(dvergrAi, ai, typeof(BaseAI));
                CopyDeclaredFields(dvergrAi, ai, typeof(MonsterAI));
            }

            ai.m_attackPlayerObjects = config.AttackPlayerObjects;
            ai.m_aggravatable = config.IsAggravatable;
            ai.m_enableHuntPlayer = false;
            ai.m_sleeping = false;
            ai.m_fallAsleepDistance = 0f;
            ai.m_wakeupRange = 0f;
            ai.m_consumeItems = new List<ItemDrop>();
            ai.m_randomMoveRange = 6f;
            ai.m_randomMoveInterval = 8f;
            ai.m_viewRange = 28f;
            ai.m_hearRange = 18f;
            ai.m_alertRange = 14f;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_avoidWater = true;
            ai.m_avoidFire = false;
            ai.m_smoothMovement = true;
            ai.m_spawnMessage = "";
            ai.m_deathMessage = "";
            ai.m_alertedMessage = "";

            NpcTalk talk = go.GetComponent<NpcTalk>() ?? go.AddComponent<NpcTalk>();
            NpcTalk dvergrTalk = dvergrTemplate.GetComponent<NpcTalk>();
            if (dvergrTalk != null)
            {
                CopyDeclaredFields(dvergrTalk, talk, typeof(NpcTalk));
            }
            talk.m_name = config.DisplayName;
            talk.m_maxRange = 12f;
            talk.m_greetRange = 6f;
            talk.m_byeRange = 10f;
            talk.m_minTalkInterval = 8f;
            talk.m_randomTalkInterval = 25f;

            // Remove Player-only components
            DestroyIfPresent<PlayerController>(go);
            DestroyIfPresent<Talker>(go);
            DestroyIfPresent<Skills>(go);
            if (oldPlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(oldPlayer);
            }

            VisEquipment visEq = go.GetComponent<VisEquipment>();
            if (visEq != null)
            {
                visEq.m_isPlayer = true;
                visEq.m_playerComponent = null;
            }

            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null)
            {
                nview.m_persistent = true;
            }

            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, fixReference: false));

            if (config.BehaviorProfile != null)
            {
                AgentProfileRegistry.RegisterPrefabProfile(config.PrefabName, config.BehaviorProfile);
            }

            return go;
        }

        /// <summary>
        /// Assigns a deterministic Norse appearance (male/female model, hair, beard, skin color, hair color)
        /// to a spawned Humanoid NPC or raw ZDO based on a seed.
        /// </summary>
        public static void ApplyDeterministicAppearanceToZdo(ZDO zdo, int seed)
        {
            if (zdo == null || !zdo.IsValid() || !zdo.IsOwner()) return;

            System.Random rng = new System.Random(seed == 0 ? Environment.TickCount : seed);
            int modelIdx = rng.Next(0, 2); // 0 = Male, 1 = Female
            int hairIdx = rng.Next(1, 24);
            int beardIdx = modelIdx == 0 ? rng.Next(0, 17) : 0;

            Vector3 skin = _skinTones[rng.Next(0, _skinTones.Length)];
            Vector3 hair = _hairTones[rng.Next(0, _hairTones.Length)];

            zdo.Set(ZDOVars.s_modelIndex, modelIdx);
            zdo.Set(ZDOVars.s_skinColor, skin);
            zdo.Set(ZDOVars.s_hairColor, hair);
            zdo.Set(ZDOVars.s_hairItem, $"Hair{hairIdx}".GetStableHashCode());
            zdo.Set(ZDOVars.s_beardItem, beardIdx > 0 ? $"Beard{beardIdx}".GetStableHashCode() : 0);
        }

        public static void EnsureAppearanceInitialized(Humanoid npc)
        {
            if (npc == null || npc.IsPlayer()) return;
            ZNetView? nview = AgentProfileRegistry.GetNView(npc);
            if (nview == null || !nview.IsValid()) return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null) return;

            int hairHash = zdo.GetInt(ZDOVars.s_hairItem, 0);
            if (hairHash == 0 && nview.IsOwner())
            {
                int seed = (int)(zdo.m_uid.ID ^ zdo.m_uid.UserID);
                ApplyDeterministicAppearanceToZdo(zdo, seed);
            }

            string hairName = FindItemNameByHash(zdo.GetInt(ZDOVars.s_hairItem, 0), "Hair", 31);
            string beardName = FindItemNameByHash(zdo.GetInt(ZDOVars.s_beardItem, 0), "Beard", 21);
            npc.SetHair(hairName);
            npc.SetBeard(beardName);

            VisEquipment visEq = npc.GetComponent<VisEquipment>();
            if (visEq != null)
            {
                visEq.SetModel(zdo.GetInt(ZDOVars.s_modelIndex, 0));
                visEq.SetSkinColor(zdo.GetVec3(ZDOVars.s_skinColor, Vector3.one));
                visEq.SetHairColor(zdo.GetVec3(ZDOVars.s_hairColor, Vector3.one));
                visEq.SetHairItem(zdo.GetInt(ZDOVars.s_hairItem, 0));
                visEq.SetBeardItem(zdo.GetInt(ZDOVars.s_beardItem, 0));
            }
        }

        private static string FindItemNameByHash(int hash, string prefix, int maxIndex)
        {
            if (hash == 0) return "";
            for (int i = 1; i <= maxIndex; i++)
            {
                string candidate = $"{prefix}{i}";
                if (candidate.GetStableHashCode() == hash)
                {
                    return candidate;
                }
            }
            return "";
        }

        private static GameObject[] ResolvePrefabs(string[] names)
        {
            if (names == null || names.Length == 0) return Array.Empty<GameObject>();
            List<GameObject> list = new List<GameObject>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                GameObject p = PrefabManager.Instance.GetPrefab(names[i]);
                if (p != null) list.Add(p);
            }
            return list.ToArray();
        }

        private static void DestroyIfPresent<T>(GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp != null)
            {
                UnityEngine.Object.DestroyImmediate(comp);
            }
        }

        private static void CopyDeclaredFields(object source, object target, Type declaringType)
        {
            FieldInfo[] fields = declaringType.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.IsStatic || f.IsNotSerialized) continue;
                try
                {
                    f.SetValue(target, f.GetValue(source));
                }
                catch
                {
                    // Ignore non-copyable runtime fields
                }
            }
        }
    }
}
