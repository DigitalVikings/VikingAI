using System.Collections.Generic;
using UnityEngine;
using VikingAI.Profiles;
using VikingAI.Telemetry;

namespace VikingAI.NPC.Affordances
{
    /// <summary>
    /// Central registry and exclusive slot reservation manager for Smart Object affordances.
    /// Prevents multiple NPCs from crowding the same smelter, chest, fire, crop, or chair simultaneously.
    /// </summary>
    public static class AffordanceRegistry
    {
        private struct Reservation
        {
            public Character Holder;
            public float ExpiresAtTime;
        }

        private static readonly List<IAffordanceProvider> _providers = new List<IAffordanceProvider>(8);
        private static readonly Dictionary<Component, Reservation> _reservations =
            new Dictionary<Component, Reservation>(32);

        static AffordanceRegistry()
        {
            RegisterProvider(new SmelterAffordanceProvider());
            RegisterProvider(new FireplaceAffordanceProvider());
            RegisterProvider(new RepairPieceAffordanceProvider());
            RegisterProvider(new HarvestPickableAffordanceProvider());
            RegisterProvider(new LeisureChairAffordanceProvider());
        }

        public static void RegisterProvider(IAffordanceProvider provider)
        {
            if (provider == null) return;
            _providers.RemoveAll(p => p.Id == provider.Id);
            _providers.Add(provider);
        }

        public static IAffordanceProvider? GetProvider(string id)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].Id == id) return _providers[i];
            }
            return null;
        }

        public static bool IsReservedByOther(Component target, Character requester)
        {
            if (target == null) return true;
            if (!_reservations.TryGetValue(target, out Reservation res)) return false;

            if (res.Holder == null || res.Holder.IsDead() || Time.time > res.ExpiresAtTime)
            {
                _reservations.Remove(target);
                return false;
            }

            return res.Holder != requester;
        }

        public static bool TryReserve(Component target, Character holder, float durationSeconds = 12.0f)
        {
            if (target == null || IsReservedByOther(target, holder)) return false;

            _reservations[target] = new Reservation
            {
                Holder = holder,
                ExpiresAtTime = Time.time + durationSeconds
            };
            return true;
        }

        public static void ReleaseReservation(Component? target, Character holder)
        {
            if (target == null) return;
            if (_reservations.TryGetValue(target, out Reservation res) && res.Holder == holder)
            {
                _reservations.Remove(target);
            }
        }

        public static bool TrySelectBestTask(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate bestCandidate)
        {
            bestCandidate = default;
            if (profile.Roles == NpcRoleFlags.None) return false;

            float bestScore = 0.01f;
            bool found = false;

            for (int i = 0; i < _providers.Count; i++)
            {
                IAffordanceProvider provider = _providers[i];
                if (provider.RequiredRole != NpcRoleFlags.None &&
                    (profile.Roles & provider.RequiredRole) == 0)
                {
                    continue;
                }

                if (provider.TryFindBestCandidate(agent, profile, searchRadius, out AffordanceCandidate candidate))
                {
                    if (candidate.UtilityScore > bestScore &&
                        !IsReservedByOther(candidate.TargetComponent, agent))
                    {
                        bestScore = candidate.UtilityScore;
                        bestCandidate = candidate;
                        found = true;
                    }
                }
            }

            return found;
        }

        public static void DrawDebugTelemetry()
        {
            if (!NavDebugOverlay.IsModeActive(DebugDrawMode.Affordances)) return;

            float now = Time.time;
            foreach (var kvp in _reservations)
            {
                if (kvp.Key == null || kvp.Value.Holder == null || now > kvp.Value.ExpiresAtTime) continue;
                Vector3 pos = kvp.Key.transform.position + Vector3.up * 1.25f;
                NavDebugOverlay.AddLabel(pos, $"[Reserved: {kvp.Value.Holder.m_name}]", Color.magenta);
                NavDebugOverlay.AddLine(kvp.Value.Holder.GetCenterPoint(), pos, Color.magenta);
            }
        }
    }
}
