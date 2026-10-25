using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.Profiles;

namespace VikingAI.Locomotion
{
    /// <summary>
    /// Handles automatic non-destructive Door opening (via RPC "UseDoor") and delayed auto-closing
    /// for agents whose AgentBehaviorProfile has CanOpenDoors / AutoCloseDoors enabled.
    /// </summary>
    public static class DoorTraversalController
    {
        private struct PendingDoorClose
        {
            public Door Door;
            public Character Opener;
            public float OpenedAtTime;
        }

        private static readonly AccessTools.FieldRef<Door, ZNetView> _doorNView =
            AccessTools.FieldRefAccess<Door, ZNetView>("m_nview");

        private static readonly List<Door> _nearbyDoors = new List<Door>(8);
        private static readonly List<PendingDoorClose> _pendingCloses = new List<PendingDoorClose>(16);
        private static readonly Dictionary<Door, float> _lastToggleTime = new Dictionary<Door, float>(16);

        public static void CheckAndActuateDoors(Character agent, AgentBehaviorProfile profile, Vector3 moveDir)
        {
            if (!ModConfig.ModEnabled.Value || !profile.CanOpenDoors) return;

            float now = Time.time;
            Vector3 agentPos = agent.transform.position;

            _nearbyDoors.Clear();
            PieceRegistry.QueryDoorsInRadius(agentPos, 2.4f, _nearbyDoors);

            for (int i = 0; i < _nearbyDoors.Count; i++)
            {
                Door door = _nearbyDoors[i];
                if (door == null || door.m_keyItem != null) continue;

                ZNetView nview = _doorNView(door);
                if (nview == null || !nview.IsValid()) continue;

                ZDO zdo = nview.GetZDO();
                if (zdo == null) continue;

                int state = zdo.GetInt(ZDOVars.s_state);
                if (state != 0) continue; // Already open

                Vector3 toDoor = door.transform.position - agentPos;
                toDoor.y = 0f;
                if (toDoor.sqrMagnitude > 4.84f) continue;

                if (moveDir.sqrMagnitude > 0.01f && Vector3.Dot(moveDir.normalized, toDoor.normalized) < 0.1f)
                {
                    continue;
                }

                if (_lastToggleTime.TryGetValue(door, out float lastTime) && now - lastTime < 1.5f)
                {
                    continue;
                }

                _lastToggleTime[door] = now;
                Vector3 userDir = (agentPos - door.transform.position).normalized;
                bool forward = Vector3.Dot(door.transform.forward, userDir) < 0f;
                nview.InvokeRPC("UseDoor", forward);

                if (profile.AutoCloseDoors && !door.m_canNotBeClosed)
                {
                    QueueAutoClose(door, agent, now);
                }
            }
        }

        public static void UpdatePendingAutoCloses()
        {
            if (_pendingCloses.Count == 0) return;

            float now = Time.time;
            float delay = ModConfig.DoorAutoCloseDelay.Value;

            for (int i = _pendingCloses.Count - 1; i >= 0; i--)
            {
                PendingDoorClose item = _pendingCloses[i];
                if (item.Door == null)
                {
                    _pendingCloses.RemoveAt(i);
                    continue;
                }

                if (now - item.OpenedAtTime < delay) continue;

                // Wait until the opener has stepped at least 2.2m clear of the doorway
                if (item.Opener != null && !item.Opener.IsDead())
                {
                    float distSq = (item.Opener.transform.position - item.Door.transform.position).sqrMagnitude;
                    if (distSq < 4.84f && now - item.OpenedAtTime < delay + 4.0f)
                    {
                        continue;
                    }
                }

                ZNetView nview = _doorNView(item.Door);
                if (nview != null && nview.IsValid())
                {
                    ZDO zdo = nview.GetZDO();
                    if (zdo != null && zdo.GetInt(ZDOVars.s_state) != 0)
                    {
                        nview.InvokeRPC("UseDoor", false);
                        _lastToggleTime[item.Door] = now;
                    }
                }

                _pendingCloses.RemoveAt(i);
            }
        }

        private static void QueueAutoClose(Door door, Character opener, float now)
        {
            for (int i = 0; i < _pendingCloses.Count; i++)
            {
                if (_pendingCloses[i].Door == door)
                {
                    _pendingCloses[i] = new PendingDoorClose { Door = door, Opener = opener, OpenedAtTime = now };
                    return;
                }
            }
            _pendingCloses.Add(new PendingDoorClose { Door = door, Opener = opener, OpenedAtTime = now });
        }
    }
}
