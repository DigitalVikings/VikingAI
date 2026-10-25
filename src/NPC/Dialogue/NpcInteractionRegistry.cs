using System;
using System.Collections.Generic;
using HarmonyLib;
using VikingAI.Configuration;
using VikingAI.Profiles;

namespace VikingAI.NPC.Dialogue
{
    public interface INpcInteractionHandler
    {
        string Id { get; }
        bool CanInteract(Character npc, AgentBehaviorProfile profile, Player player);
        string GetHoverSuffix(Character npc, AgentBehaviorProfile profile, Player player);
        bool OnInteract(Character npc, AgentBehaviorProfile profile, Player player, bool hold, bool alt);
    }

    /// <summary>
    /// Open registry for contextual NPC dialogue, trade, role assignment, and companion order interactions.
    /// Hooks Tameable.GetHoverText and Tameable.Interact cleanly.
    /// </summary>
    [HarmonyPatch]
    public static class NpcInteractionRegistry
    {
        private static readonly List<INpcInteractionHandler> _handlers = new List<INpcInteractionHandler>(4);

        public static void RegisterHandler(INpcInteractionHandler handler)
        {
            if (handler == null) return;
            _handlers.RemoveAll(h => h.Id == handler.Id);
            _handlers.Add(handler);
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        [HarmonyPostfix]
        private static void Tameable_GetHoverText_Postfix(Tameable __instance, ref string __result)
        {
            if (!ModConfig.ModEnabled.Value || _handlers.Count == 0 || __instance == null) return;
            Player player = Player.m_localPlayer;
            if (player == null) return;

            Character character = __instance.GetComponent<Character>();
            if (character == null) return;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            for (int i = 0; i < _handlers.Count; i++)
            {
                INpcInteractionHandler handler = _handlers[i];
                if (handler.CanInteract(character, profile, player))
                {
                    string suffix = handler.GetHoverSuffix(character, profile, player);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        __result += "\n" + Localization.instance.Localize(suffix);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
        [HarmonyPrefix]
        private static bool Tameable_Interact_Prefix(
            Tameable __instance,
            Humanoid user,
            bool hold,
            bool alt,
            ref bool __result,
            bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!ModConfig.ModEnabled.Value || _handlers.Count == 0 || __instance == null) return true;
            if (user is not Player player) return true;

            Character character = __instance.GetComponent<Character>();
            if (character == null) return true;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(character);
            for (int i = 0; i < _handlers.Count; i++)
            {
                INpcInteractionHandler handler = _handlers[i];
                if (handler.CanInteract(character, profile, player))
                {
                    if (handler.OnInteract(character, profile, player, hold, alt))
                    {
                        __result = true;
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
