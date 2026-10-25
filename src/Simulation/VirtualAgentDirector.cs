using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.Profiles;

namespace VikingAI.Simulation
{
    /// <summary>
    /// 1 Hz Macro-ZDO Virtual Agent Director.
    /// Manages:
    /// 1. Background ZDO simulation for tracked NPCs when their zone unloads.
    /// 2. Deterministic time-delta catch-up when a zone reloads.
    /// 3. Cross-zone and portal/dungeon companion transit when the player teleports.
    /// </summary>
    [HarmonyPatch]
    public static class VirtualAgentDirector
    {
        public static readonly int ZdoKeyLastSimTicks = "VAI_v1_LastTicks".GetStableHashCode();
        public static readonly int ZdoKeyPatrolAnchor = "VAI_v1_PatrolPos".GetStableHashCode();

        private static readonly List<IVirtualAgentTask> _tasks = new List<IVirtualAgentTask>
        {
            new PatrolVirtualTask()
        };

        private static readonly HashSet<ZDO> _trackedNpcZdos = new HashSet<ZDO>();
        private static readonly List<Character> _portalFollowersTemp = new List<Character>(8);
        private static float _virtualTickTimer;

        public static void RegisterTask(IVirtualAgentTask task)
        {
            if (task == null) return;
            _tasks.RemoveAll(t => t.Id == task.Id);
            _tasks.Add(task);
        }

        /// <summary>
        /// Spawns a persistent agent ZDO directly in ZDOMan (including inside unloaded sectors),
        /// initializes its role flags and appearance, and registers it with VirtualAgentDirector
        /// so it ticks at 1 Hz in the simulation layer before any GameObject is instantiated.
        /// </summary>
        public static ZDO? SpawnVirtualAgentZdo(
            string prefabName,
            Vector3 position,
            Quaternion rotation,
            NpcRoleFlags roles,
            bool isCustomNpc = true)
        {
            if (ZDOMan.instance == null || string.IsNullOrEmpty(prefabName)) return null;

            int prefabHash = prefabName.GetStableHashCode();
            ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefabHash);
            if (zdo == null) return null;

            zdo.SetPrefab(prefabHash);
            zdo.SetRotation(rotation);
            zdo.Persistent = true;
            zdo.Set(AgentProfileRegistry.ZdoKeyCustomNpc, isCustomNpc);
            zdo.Set(AgentProfileRegistry.ZdoKeyRoleFlags, (int)roles);
            zdo.Set(ZdoKeyPatrolAnchor, position);

            if (ZNet.instance != null)
            {
                zdo.Set(ZdoKeyLastSimTicks, ZNet.instance.GetTime().Ticks);
            }

            if (isCustomNpc)
            {
                int seed = (int)(zdo.m_uid.ID ^ zdo.m_uid.UserID) ^ Environment.TickCount;
                NPC.Prefabs.HumanoidPrefabBuilder.ApplyDeterministicAppearanceToZdo(zdo, seed);
            }

            _trackedNpcZdos.Add(zdo);
            return zdo;
        }

        public static void TrackZdoDirectly(ZDO zdo)
        {
            if (zdo != null && zdo.IsValid())
            {
                _trackedNpcZdos.Add(zdo);
            }
        }

        /// <summary>
        /// Raised for every persisted agent ZDO (role flags != None) re-registered after a world load.
        /// Downstream mods use this to rebuild their own in-memory indexes of agents.
        /// </summary>
        public static event Action<ZDO>? OnAgentRestored;

        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> _zdoObjectsById =
            AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

        /// <summary>
        /// Rescans all ZDOs after a world load and re-tracks every agent with persisted role flags, so agents
        /// that are in unloaded zones resume 1 Hz simulation without waiting for their zone to load.
        /// </summary>
        public static int RestoreTrackedAgents()
        {
            _trackedNpcZdos.Clear();
            if (ZDOMan.instance == null) return 0;

            int restored = 0;
            foreach (ZDO zdo in _zdoObjectsById(ZDOMan.instance).Values)
            {
                if (zdo == null || zdo.GetInt(AgentProfileRegistry.ZdoKeyRoleFlags, 0) == 0) continue;
                _trackedNpcZdos.Add(zdo);
                restored++;
                try
                {
                    OnAgentRestored?.Invoke(zdo);
                }
                catch (Exception ex)
                {
                    VikingAIPlugin.Log.LogError($"[VirtualAgentDirector] OnAgentRestored handler failed: {ex}");
                }
            }

            VikingAIPlugin.Log.LogInfo($"[VirtualAgentDirector] Restored {restored} persisted agent ZDO(s) after world load.");
            return restored;
        }

        [HarmonyPatch(typeof(ZNet), "ServerLoadWorld")]
        [HarmonyPostfix]
        private static void ZNet_ServerLoadWorld_Postfix()
        {
            if (!ModConfig.ModEnabled.Value) return;
            RestoreTrackedAgents();
        }

        /// <summary>
        /// Advances an unloaded ZDO toward <paramref name="targetPos"/> at <paramref name="speedMetersPerSec"/>,
        /// snapping Y elevation to WorldGenerator terrain height and updating ZDOMan sector registration.
        /// Returns true when the ZDO is within <paramref name="arrivalRadius"/> of <paramref name="targetPos"/>.
        /// </summary>
        public static bool MoveZdoAlongTerrain(
            ZDO zdo,
            Vector3 targetPos,
            float speedMetersPerSec,
            float dtSeconds,
            float arrivalRadius = 3.0f)
        {
            if (zdo == null || !zdo.IsValid() || !zdo.IsOwner()) return false;

            Vector3 curr = zdo.GetPosition();
            Vector3 toTarget = targetPos - curr;
            toTarget.y = 0f;
            float horizDist = toTarget.magnitude;

            if (horizDist <= arrivalRadius)
            {
                return true;
            }

            Vector3 dir = toTarget / horizDist;
            float step = Mathf.Min(horizDist, speedMetersPerSec * dtSeconds);
            Vector3 next = curr + dir * step;

            if (WorldGenerator.instance != null)
            {
                float terrainY = WorldGenerator.instance.GetHeight(next.x, next.z);
                next.y = Mathf.Max(30.0f, terrainY);
            }
            else
            {
                next.y = Mathf.Lerp(curr.y, targetPos.y, step / horizDist);
            }

            zdo.SetPosition(next);
            if (dir.sqrMagnitude > 0.01f)
            {
                zdo.SetRotation(Quaternion.LookRotation(dir, Vector3.up));
            }

            return (horizDist - step) <= arrivalRadius;
        }

        public static void TrackNpcZdo(Character npc, AgentBehaviorProfile profile)
        {
            if (npc == null) return;
            ZNetView? nview = AgentProfileRegistry.GetNView(npc);
            if (nview == null || !nview.IsValid()) return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null) return;

            _trackedNpcZdos.Add(zdo);
            if (nview.IsOwner() && zdo.GetVec3(ZdoKeyPatrolAnchor, Vector3.zero) == Vector3.zero)
            {
                zdo.Set(ZdoKeyPatrolAnchor, npc.transform.position);
            }
        }

        public static void UpdateDirector(float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableUnloadedZoneSimulation.Value)
            {
                return;
            }

            float interval = Mathf.Max(0.25f, ModConfig.VirtualTickIntervalSeconds.Value);
            _virtualTickTimer += dt;
            if (_virtualTickTimer < interval) return;

            float stepDt = _virtualTickTimer;
            _virtualTickTimer = 0f;

            if (ZNetScene.instance == null || ZNet.instance == null) return;

            long nowTicks = ZNet.instance.GetTime().Ticks;
            bool hasInvalid = false;

            foreach (ZDO zdo in _trackedNpcZdos)
            {
                if (zdo == null || !zdo.IsValid())
                {
                    hasInvalid = true;
                    continue;
                }

                // Skip ZDOs that currently have a live instantiated GameObject in the active scene
                if (ZNetScene.instance.FindInstance(zdo) != null)
                {
                    if (zdo.IsOwner())
                    {
                        zdo.Set(ZdoKeyLastSimTicks, nowTicks);
                    }
                    continue;
                }

                NpcRoleFlags roles = (NpcRoleFlags)zdo.GetInt(AgentProfileRegistry.ZdoKeyRoleFlags);
                if (roles == NpcRoleFlags.None) continue;

                for (int i = 0; i < _tasks.Count; i++)
                {
                    IVirtualAgentTask task = _tasks[i];
                    if ((roles & task.RequiredRole) != 0)
                    {
                        task.TickUnloadedZdo(zdo, roles, stepDt);
                    }
                }

                if (zdo.IsOwner())
                {
                    zdo.Set(ZdoKeyLastSimTicks, nowTicks);
                }
            }

            if (hasInvalid)
            {
                _trackedNpcZdos.RemoveWhere(z => z == null || !z.IsValid());
            }
        }

        [HarmonyPatch(typeof(Character), "Start")]
        [HarmonyPostfix]
        private static void Character_Start_Postfix(Character __instance)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null || __instance.IsPlayer()) return;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(__instance);
            if (!profile.IsCustomNpc && profile.Roles == NpcRoleFlags.None) return;

            TrackNpcZdo(__instance, profile);

            if (!ModConfig.EnableUnloadedZoneSimulation.Value || ZNet.instance == null) return;
            ZNetView? nview = AgentProfileRegistry.GetNView(__instance);
            if (nview == null || !nview.IsValid()) return;

            ZDO zdo = nview.GetZDO();
            long lastTicks = zdo.GetLong(ZdoKeyLastSimTicks, 0L);
            long nowTicks = ZNet.instance.GetTime().Ticks;

            if (lastTicks > 0L && nowTicks > lastTicks)
            {
                double elapsedSeconds = TimeSpan.FromTicks(nowTicks - lastTicks).TotalSeconds;
                if (elapsedSeconds >= 2.0)
                {
                    for (int i = 0; i < _tasks.Count; i++)
                    {
                        IVirtualAgentTask task = _tasks[i];
                        if ((profile.Roles & task.RequiredRole) != 0)
                        {
                            task.ApplyCatchUpOnLoad(__instance, profile, elapsedSeconds);
                        }
                    }
                }
            }

            if (nview.IsOwner())
            {
                zdo.Set(ZdoKeyLastSimTicks, nowTicks);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        [HarmonyPostfix]
        private static void Player_TeleportTo_Postfix(
            Player __instance,
            Vector3 pos,
            Quaternion rot,
            bool __result)
        {
            if (!__result || !ModConfig.ModEnabled.Value || !ModConfig.EnableFollowerPortalTransit.Value)
            {
                return;
            }

            if (__instance == null) return;

            _portalFollowersTemp.Clear();
            SpatialCharacterGrid.QueryRadius(__instance.transform.position, 20.0f, _portalFollowersTemp, exclude: __instance);

            int companionIndex = 0;
            for (int i = 0; i < _portalFollowersTemp.Count; i++)
            {
                Character c = _portalFollowersTemp[i];
                if (c == null || c.IsDead() || c.IsPlayer()) continue;

                if (c.GetBaseAI() is not MonsterAI mai || mai.GetFollowTarget() != __instance.gameObject)
                {
                    continue;
                }

                AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(c);
                if (!profile.CanTraversePortals) continue;

                ZNetView? nview = AgentProfileRegistry.GetNView(c);
                if (nview == null || !nview.IsValid()) continue;

                if (!nview.IsOwner())
                {
                    nview.ClaimOwnership();
                }

                // Place companion in an arc behind the teleport destination
                float angleRad = (150f + companionIndex * 30f) * Mathf.Deg2Rad;
                Vector3 localOffset = new Vector3(Mathf.Sin(angleRad) * 1.8f, 0.25f, Mathf.Cos(angleRad) * 1.8f);
                Vector3 destPos = pos + (rot * localOffset);

                nview.GetZDO().SetPosition(destPos);
                c.transform.position = destPos;
                c.transform.rotation = rot;
                companionIndex++;
            }
        }
    }
}
