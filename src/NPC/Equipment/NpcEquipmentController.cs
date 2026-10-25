using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Profiles;

namespace VikingAI.NPC.Equipment
{
    /// <summary>
    /// Provides ZDO-persistent Inventory serialization ("VAI_v1_Inv"), automatic best-gear equipping,
    /// and full VisEquipment paperdoll synchronization (hair, beard, holstered back items) for non-Player Humanoid NPCs.
    /// </summary>
    [HarmonyPatch]
    public static class NpcEquipmentController
    {
        public static readonly int ZdoKeyInventory = "VAI_v1_Inv".GetStableHashCode();

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _hiddenLeft =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _hiddenRight =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
        private static readonly AccessTools.FieldRef<Humanoid, string> _beardItem =
            AccessTools.FieldRefAccess<Humanoid, string>("m_beardItem");
        private static readonly AccessTools.FieldRef<Humanoid, string> _hairItem =
            AccessTools.FieldRefAccess<Humanoid, string>("m_hairItem");

        /// <summary>
        /// Saves a non-Player Humanoid's Inventory to its ZDO under "VAI_v1_Inv" and equips optimal gear.
        /// </summary>
        public static void SaveNpcInventory(Humanoid npc)
        {
            if (npc == null || npc.IsPlayer()) return;
            ZNetView? nview = AgentProfileRegistry.GetNView(npc);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            Inventory inv = npc.GetInventory();
            if (inv == null) return;

            ZPackage pkg = new ZPackage();
            inv.Save(pkg);
            nview.GetZDO().Set(ZdoKeyInventory, pkg.GetBase64());
        }

        /// <summary>
        /// Loads a non-Player Humanoid's persisted Inventory from its ZDO ("VAI_v1_Inv") if present
        /// and equips its best weapons and armor.
        /// </summary>
        public static bool LoadNpcInventory(Humanoid npc)
        {
            if (npc == null || npc.IsPlayer()) return false;
            ZNetView? nview = AgentProfileRegistry.GetNView(npc);
            if (nview == null || !nview.IsValid()) return false;

            string b64 = nview.GetZDO().GetString(ZdoKeyInventory, "");
            if (string.IsNullOrEmpty(b64) || b64 == "none") return false;

            Inventory inv = npc.GetInventory();
            if (inv == null) return false;

            try
            {
                ZPackage pkg = new ZPackage(b64);
                inv.Load(pkg);
                EquipBestAvailableGear(npc);
                return true;
            }
            catch (Exception ex)
            {
                VikingAIPlugin.Log.LogWarning($"Failed to deserialize NPC inventory for {npc.name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scans the NPC's Inventory and equips the highest-tier weapon, shield, helmet, chest, legs, and cape.
        /// </summary>
        public static void EquipBestAvailableGear(Humanoid npc)
        {
            if (npc == null || npc.IsPlayer()) return;
            Inventory inv = npc.GetInventory();
            if (inv == null) return;

            List<ItemDrop.ItemData> items = inv.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null) continue;

                ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
                if (type == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                    type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                    type == ItemDrop.ItemData.ItemType.Bow ||
                    type == ItemDrop.ItemData.ItemType.Shield ||
                    type == ItemDrop.ItemData.ItemType.Helmet ||
                    type == ItemDrop.ItemData.ItemType.Chest ||
                    type == ItemDrop.ItemData.ItemType.Legs ||
                    type == ItemDrop.ItemData.ItemType.Shoulder)
                {
                    if (!item.m_equipped)
                    {
                        npc.EquipItem(item, triggerEquipEffects: false);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Humanoid), "Start")]
        [HarmonyPostfix]
        private static void Humanoid_Start_Postfix(Humanoid __instance)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null || __instance.IsPlayer()) return;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(__instance);
            if (profile.IsCustomNpc)
            {
                Prefabs.HumanoidPrefabBuilder.EnsureAppearanceInitialized(__instance);
                LoadNpcInventory(__instance);
            }
        }

        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        [HarmonyPostfix]
        private static void Humanoid_SetupVisEquipment_Postfix(Humanoid __instance, VisEquipment visEq, bool isRagdoll)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null || __instance.IsPlayer() || visEq == null)
            {
                return;
            }

            if (!isRagdoll)
            {
                ItemDrop.ItemData hLeft = _hiddenLeft(__instance);
                ItemDrop.ItemData hRight = _hiddenRight(__instance);

                visEq.SetLeftBackItem(
                    (hLeft != null && hLeft.m_dropPrefab != null) ? hLeft.m_dropPrefab.name.GetStableHashCode() : 0,
                    hLeft != null ? hLeft.m_variant : 0,
                    hLeft != null ? hLeft.m_quality : 0);

                visEq.SetRightBackItem(
                    (hRight != null && hRight.m_dropPrefab != null) ? hRight.m_dropPrefab.name.GetStableHashCode() : 0,
                    hRight != null ? hRight.m_quality : 0);
            }

            string beard = _beardItem(__instance);
            string hair = _hairItem(__instance);
            if (!string.IsNullOrEmpty(beard))
            {
                visEq.SetBeardItem(beard.GetStableHashCode());
            }
            if (!string.IsNullOrEmpty(hair))
            {
                visEq.SetHairItem(hair.GetStableHashCode());
            }
        }
    }
}
