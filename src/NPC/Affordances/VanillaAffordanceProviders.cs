using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingAI.Core;
using VikingAI.Profiles;

namespace VikingAI.NPC.Affordances
{
    public sealed class SmelterAffordanceProvider : IAffordanceProvider
    {
        public string Id => "VikingAI.SmelterTender";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.SmelterTender;

        private static readonly AccessTools.FieldRef<Smelter, ZNetView> _smelterNView =
            AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");

        public bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate)
        {
            candidate = default;
            Vector3 myPos = agent.transform.position;
            float rSq = searchRadius * searchRadius;
            float bestScore = 0f;

            foreach (Smelter smelter in PieceRegistry.Smelters)
            {
                if (smelter == null || AffordanceRegistry.IsReservedByOther(smelter, agent)) continue;

                float distSq = (smelter.transform.position - myPos).sqrMagnitude;
                if (distSq > rSq) continue;

                ZNetView nview = _smelterNView(smelter);
                if (nview == null || !nview.IsValid()) continue;
                ZDO zdo = nview.GetZDO();
                if (zdo == null) continue;

                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                int queued = zdo.GetInt(ZDOVars.s_queued);
                int ready = zdo.GetInt(ZDOVars.s_spawnAmount);

                bool needsFuel = smelter.m_maxFuel > 0 && fuel < smelter.m_maxFuel - 1 && queued > 0;
                bool hasReadyOutput = ready > 0;

                if (!needsFuel && !hasReadyOutput) continue;

                float dist = Mathf.Sqrt(distSq);
                float score = 45.0f - (dist * 0.5f);
                if (score > bestScore)
                {
                    bestScore = score;
                    Transform interactTr = (needsFuel && smelter.m_addWoodSwitch != null)
                        ? smelter.m_addWoodSwitch.transform
                        : smelter.transform;

                    candidate = new AffordanceCandidate
                    {
                        ProviderId = Id,
                        TargetComponent = smelter,
                        InteractionPoint = interactTr.position,
                        UtilityScore = score,
                        AnimationTrigger = "interact",
                        WorkDurationSeconds = 2.5f
                    };
                }
            }

            return bestScore > 0f;
        }

        public void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate)
        {
            if (candidate.TargetComponent is not Smelter smelter) return;
            ZNetView nview = _smelterNView(smelter);
            if (nview == null || !nview.IsValid()) return;
            ZDO zdo = nview.GetZDO();
            if (zdo == null) return;

            if (zdo.GetInt(ZDOVars.s_spawnAmount) > 0)
            {
                nview.InvokeRPC("RPC_EmptyProcessed");
            }

            if (smelter.m_maxFuel > 0 && zdo.GetFloat(ZDOVars.s_fuel) < smelter.m_maxFuel)
            {
                // Strictly consume fuel from the NPC's inventory; never conjure free resources
                if (agent is Humanoid hum && smelter.m_fuelItem != null)
                {
                    Inventory inv = hum.GetInventory();
                    string fuelName = smelter.m_fuelItem.m_itemData.m_shared.m_name;
                    if (inv != null && inv.HaveItem(fuelName))
                    {
                        inv.RemoveItem(fuelName, 1);
                        nview.InvokeRPC("RPC_AddFuel");
                    }
                }
            }
        }
    }

    public sealed class FireplaceAffordanceProvider : IAffordanceProvider
    {
        public string Id => "VikingAI.FireplaceTender";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.Guard | NpcRoleFlags.SmelterTender | NpcRoleFlags.Hauler;

        private static readonly AccessTools.FieldRef<Fireplace, ZNetView> _fireNView =
            AccessTools.FieldRefAccess<Fireplace, ZNetView>("m_nview");

        public bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate)
        {
            candidate = default;
            Vector3 myPos = agent.transform.position;
            float rSq = searchRadius * searchRadius;
            float bestScore = 0f;

            foreach (Fireplace fp in PieceRegistry.Fireplaces)
            {
                if (fp == null || fp.m_infiniteFuel || !fp.m_canRefill) continue;
                if (AffordanceRegistry.IsReservedByOther(fp, agent)) continue;

                float distSq = (fp.transform.position - myPos).sqrMagnitude;
                if (distSq > rSq) continue;

                ZNetView nview = _fireNView(fp);
                if (nview == null || !nview.IsValid()) continue;
                ZDO zdo = nview.GetZDO();
                if (zdo == null) continue;

                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                if (fuel > fp.m_maxFuel * 0.45f) continue;

                float score = 40.0f - Mathf.Sqrt(distSq) * 0.4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    candidate = new AffordanceCandidate
                    {
                        ProviderId = Id,
                        TargetComponent = fp,
                        InteractionPoint = fp.transform.position,
                        UtilityScore = score,
                        AnimationTrigger = "interact",
                        WorkDurationSeconds = 2.0f
                    };
                }
            }

            return bestScore > 0f;
        }

        public void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate)
        {
            if (candidate.TargetComponent is not Fireplace fp) return;
            ZNetView nview = _fireNView(fp);
            if (nview == null || !nview.IsValid()) return;

            if (agent is Humanoid hum && fp.m_fuelItem != null)
            {
                Inventory inv = hum.GetInventory();
                string fuelName = fp.m_fuelItem.m_itemData.m_shared.m_name;
                if (inv != null && inv.HaveItem(fuelName))
                {
                    inv.RemoveItem(fuelName, 1);
                    nview.InvokeRPC("RPC_AddFuel");
                }
            }
        }
    }

    public sealed class RepairPieceAffordanceProvider : IAffordanceProvider
    {
        public string Id => "VikingAI.BuilderRepair";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.Builder;

        public bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate)
        {
            candidate = default;
            Vector3 myPos = agent.transform.position;
            float rSq = searchRadius * searchRadius;
            float bestScore = 0f;

            foreach (WearNTear wnt in PieceRegistry.WearNTears)
            {
                if (wnt == null || AffordanceRegistry.IsReservedByOther(wnt, agent)) continue;
                float distSq = (wnt.transform.position - myPos).sqrMagnitude;
                if (distSq > rSq) continue;

                float hpPct = wnt.GetHealthPercentage();
                if (hpPct >= 0.85f || hpPct <= 0f) continue;

                float score = 48.0f * (1.0f - hpPct) + 15.0f - Mathf.Sqrt(distSq) * 0.35f;
                if (score > bestScore)
                {
                    bestScore = score;
                    candidate = new AffordanceCandidate
                    {
                        ProviderId = Id,
                        TargetComponent = wnt,
                        InteractionPoint = wnt.transform.position,
                        UtilityScore = score,
                        AnimationTrigger = "swing_hammer",
                        WorkDurationSeconds = 2.2f
                    };
                }
            }

            return bestScore > 0f;
        }

        public void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate)
        {
            if (candidate.TargetComponent is WearNTear wnt && wnt != null)
            {
                wnt.Repair();
            }
        }
    }

    public sealed class HarvestPickableAffordanceProvider : IAffordanceProvider
    {
        public string Id => "VikingAI.FarmerHarvest";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.Farmer;

        private static readonly AccessTools.FieldRef<Pickable, ZNetView> _pickNView =
            AccessTools.FieldRefAccess<Pickable, ZNetView>("m_nview");

        public bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate)
        {
            candidate = default;
            Vector3 myPos = agent.transform.position;
            float rSq = searchRadius * searchRadius;
            float bestScore = 0f;

            foreach (Pickable pick in PieceRegistry.Pickables)
            {
                if (pick == null || !pick.CanBePicked() || AffordanceRegistry.IsReservedByOther(pick, agent))
                {
                    continue;
                }

                float distSq = (pick.transform.position - myPos).sqrMagnitude;
                if (distSq > rSq) continue;

                float score = 36.0f - Mathf.Sqrt(distSq) * 0.45f;
                if (score > bestScore)
                {
                    bestScore = score;
                    candidate = new AffordanceCandidate
                    {
                        ProviderId = Id,
                        TargetComponent = pick,
                        InteractionPoint = pick.transform.position,
                        UtilityScore = score,
                        AnimationTrigger = "interact",
                        WorkDurationSeconds = 1.6f
                    };
                }
            }

            return bestScore > 0f;
        }

        public void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate)
        {
            if (candidate.TargetComponent is not Pickable pick || !pick.CanBePicked()) return;
            ZNetView nview = _pickNView(pick);
            if (nview != null && nview.IsValid())
            {
                nview.InvokeRPC("RPC_Pick", 0);
            }
        }
    }

    public sealed class LeisureChairAffordanceProvider : IAffordanceProvider
    {
        public string Id => "VikingAI.LeisureRest";
        public NpcRoleFlags RequiredRole => NpcRoleFlags.AllSettlement;

        public bool TryFindBestCandidate(
            Character agent,
            AgentBehaviorProfile profile,
            float searchRadius,
            out AffordanceCandidate candidate)
        {
            candidate = default;
            if (!EnvMan.IsNight() && !EnvMan.IsAfternoon()) return false;

            Vector3 myPos = agent.transform.position;
            float rSq = searchRadius * searchRadius;
            float bestScore = 0f;

            foreach (Chair chair in PieceRegistry.Chairs)
            {
                if (chair == null || chair.IsInUse() || AffordanceRegistry.IsReservedByOther(chair, agent))
                {
                    continue;
                }

                float distSq = (chair.transform.position - myPos).sqrMagnitude;
                if (distSq > rSq) continue;

                float score = 18.0f - Mathf.Sqrt(distSq) * 0.3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    candidate = new AffordanceCandidate
                    {
                        ProviderId = Id,
                        TargetComponent = chair,
                        InteractionPoint = chair.transform.position,
                        UtilityScore = score,
                        AnimationTrigger = "interact",
                        WorkDurationSeconds = 6.0f
                    };
                }
            }

            return bestScore > 0f;
        }

        public void CompleteInteraction(Character agent, AgentBehaviorProfile profile, in AffordanceCandidate candidate)
        {
            // Resting at a chair heals a small amount of health on the NPC
            if (!agent.IsDead() && agent.GetHealthPercentage() < 1.0f)
            {
                agent.Heal(agent.GetMaxHealth() * 0.05f);
            }
        }
    }
}
