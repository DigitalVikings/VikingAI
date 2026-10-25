using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingAI.Configuration;
using VikingAI.Core;
using VikingAI.Profiles;
using VikingAI.Telemetry;

namespace VikingAI.Tactics
{
    public enum DetectionStage : byte
    {
        Unaware    = 0,
        Suspicious = 1,
        Alerted    = 2,
        Engaged    = 3
    }

    public sealed class SensoryTrack
    {
        public Character? Target;
        public float DetectionLevel; // 0.0 -> 1.0
        public DetectionStage Stage;
        public Vector3 LastKnownPos;
        public float LastStimulusTime;
        public float SearchTimeRemaining;
    }

    /// <summary>
    /// Generalized Character-to-Character 4-stage sensory perception and stealth engine
    /// (superseding Shadows of Midgard). Integrates sunlight/shadow raycasting, foliage cover,
    /// mathematical equipment noise, a 32-slot AllyCall ring buffer, and InvestigateBrainModule.
    /// </summary>
    public static class SensoryMemoryController
    {
        private struct PendingAllyCall
        {
            public Character Caller;
            public Character Target;
            public Vector3 KnownPos;
            public float Radius;
        }

        private static readonly ConditionalWeakTable<BaseAI, SensoryTrack> _tracks =
            new ConditionalWeakTable<BaseAI, SensoryTrack>();

        private static readonly PendingAllyCall[] _allyCallRing = new PendingAllyCall[32];
        private static int _ringHead;
        private static int _ringTail;
        private static int _ringCount;
        private static readonly List<Character> _allyQueryTemp = new List<Character>(16);

        private static readonly AccessTools.FieldRef<BaseAI, Character> _aiCharacter =
            AccessTools.FieldRefAccess<BaseAI, Character>("m_character");
        private static readonly AccessTools.FieldRef<BaseAI, int> _viewBlockMask =
            AccessTools.FieldRefAccess<BaseAI, int>("m_viewBlockMask");

        public static SensoryTrack GetOrCreateTrack(BaseAI ai)
        {
            if (!_tracks.TryGetValue(ai, out SensoryTrack track))
            {
                track = new SensoryTrack();
                _tracks.Add(ai, track);
            }
            return track;
        }

        public static bool ShouldUseSensoryEngine(BaseAI ai, out Character? observer, out AgentBehaviorProfile profile)
        {
            observer = _aiCharacter(ai);
            if (observer == null || !ModConfig.ModEnabled.Value || !ModConfig.EnableSensoryPerceptionEngine.Value)
            {
                profile = new AgentBehaviorProfile();
                return false;
            }

            if (AgentProfileRegistry.IsPerceptionExempt(observer))
            {
                profile = new AgentBehaviorProfile();
                return false;
            }

            profile = AgentProfileRegistry.GetProfile(observer);
            return profile.UseSensoryEngine;
        }

        public static bool EvaluateVisualStimulus(BaseAI ai, Character observer, AgentBehaviorProfile profile, Character target)
        {
            if (target == null || target.IsDead()) return false;
            if (target is Player p && (p.InDebugFlyMode() || p.InGhostMode())) return false;

            Vector3 obsEye = observer.m_eye != null ? observer.m_eye.position : observer.GetCenterPoint();
            Vector3 tgtPoint = target.IsCrouching() ? target.GetCenterPoint() : (target.m_eye != null ? target.m_eye.position : target.GetCenterPoint());
            Vector3 toTarget = tgtPoint - obsEye;
            float dist = toTarget.magnitude;

            float maxRange = Mathf.Min(ai.m_viewRange, ModConfig.MaxVisualRange.Value);
            if (dist > maxRange) return false;

            SensoryTrack track = GetOrCreateTrack(ai);
            bool alreadyEngaged = track.Stage == DetectionStage.Engaged && track.Target == target;

            // Vision cone check when not already engaged
            if (!alreadyEngaged)
            {
                float angle = Vector3.Angle(toTarget, observer.transform.forward);
                float maxAngle = track.Stage >= DetectionStage.Suspicious
                    ? Mathf.Min(135f, ModConfig.VisionConeHalfAngle.Value * 1.45f)
                    : ModConfig.VisionConeHalfAngle.Value;

                if (angle > maxAngle)
                {
                    return false;
                }
            }

            // Line of sight raycast
            int mask = _viewBlockMask(ai);
            if (Physics.Raycast(obsEye, toTarget / Mathf.Max(0.01f, dist), dist, mask))
            {
                return false;
            }

            if (!ai.m_mistVision && ParticleMist.IsMistBlocked(obsEye, tgtPoint))
            {
                return false;
            }

            // Sample target's illumination, shadow, and foliage cover
            TargetCoverState cover = EnvironmentCoverSampler.GetOrSample(target);
            float stealthMult = target.GetStealthFactor() * cover.IlluminationFactor * (1.0f - cover.FoliageConcealment);
            float effectiveRange = maxRange * Mathf.Clamp(stealthMult, 0.15f, 1.3f);

            if (dist > effectiveRange)
            {
                return false;
            }

            float proximityFactor = Mathf.Clamp01(1.15f - (dist / Mathf.Max(1f, effectiveRange)));
            float gain = ModConfig.AlertGainRate.Value * proximityFactor * 0.25f;
            if (dist < 4.0f) gain *= 2.5f;

            ApplyStimulus(ai, observer, profile, target, target.transform.position, gain);
            return track.Stage >= DetectionStage.Engaged;
        }

        public static bool EvaluateAuditoryStimulus(BaseAI ai, Character observer, AgentBehaviorProfile profile, Character target)
        {
            if (target == null || target.IsDead()) return false;
            if (target is Player p && (p.InDebugFlyMode() || p.InGhostMode())) return false;

            float dist = Vector3.Distance(observer.transform.position, target.transform.position);
            float maxHear = Mathf.Min(ai.m_hearRange, ModConfig.MaxHearingRange.Value);
            if (Character.InInterior(observer.transform))
            {
                maxHear = Mathf.Min(12.0f, maxHear);
            }

            if (dist > maxHear) return false;

            TargetCoverState cover = EnvironmentCoverSampler.GetOrSample(target);
            if (dist > cover.AcousticRadius)
            {
                return false;
            }

            float loudness = Mathf.Clamp01(1.0f - (dist / Mathf.Max(1f, cover.AcousticRadius)));
            float gain = ModConfig.SuspicionGainRate.Value * loudness * 0.2f;

            ApplyStimulus(ai, observer, profile, target, target.transform.position, gain);
            SensoryTrack track = GetOrCreateTrack(ai);
            return track.Stage >= DetectionStage.Engaged;
        }

        public static void NotifyDamaged(BaseAI ai, Character? attacker)
        {
            if (attacker == null) return;
            Character? observer = _aiCharacter(ai);
            if (observer == null) return;

            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(observer);
            SensoryTrack track = GetOrCreateTrack(ai);
            track.Target = attacker;
            track.DetectionLevel = 1.0f;
            track.Stage = DetectionStage.Engaged;
            track.LastKnownPos = attacker.transform.position;
            track.LastStimulusTime = Time.time;
            track.SearchTimeRemaining = ModConfig.SearchDurationSeconds.Value;

            EnqueueAllyCall(observer, attacker, attacker.transform.position, profile.CallForHelpRadius);
        }

        public static void NotifySoundImpulse(BaseAI ai, Character? source, Vector3 soundPosition, float impulseAmount)
        {
            Character? observer = _aiCharacter(ai);
            if (observer == null) return;
            AgentBehaviorProfile profile = AgentProfileRegistry.GetProfile(observer);
            ApplyStimulus(ai, observer, profile, source, soundPosition, impulseAmount);
        }

        public static void TickAgentMemory(BaseAI ai, Character observer, AgentBehaviorProfile profile, float dt)
        {
            ProcessOneQueuedAllyCall();

            if (!profile.UseSensoryEngine || !ModConfig.EnableSensoryPerceptionEngine.Value) return;
            if (!_tracks.TryGetValue(ai, out SensoryTrack track)) return;

            float now = Time.time;
            if (now - track.LastStimulusTime > 1.25f && track.DetectionLevel > 0f)
            {
                track.DetectionLevel = Mathf.Max(0f, track.DetectionLevel - ModConfig.DetectionDecayRate.Value * dt);
                UpdateStageFromLevel(ai, observer, profile, track);
            }

            if (track.SearchTimeRemaining > 0f)
            {
                track.SearchTimeRemaining = Mathf.Max(0f, track.SearchTimeRemaining - dt);
            }

            if (NavDebugOverlay.IsModeActive(DebugDrawMode.Senses) && track.DetectionLevel > 0.01f)
            {
                Color col = track.Stage switch
                {
                    DetectionStage.Engaged => Color.red,
                    DetectionStage.Alerted => new Color(1f, 0.55f, 0f),
                    DetectionStage.Suspicious => Color.yellow,
                    _ => Color.white
                };
                NavDebugOverlay.AddLabel(
                    observer.GetTopPoint() + Vector3.up * 0.45f,
                    $"{track.Stage} ({track.DetectionLevel * 100f:F0}%)",
                    col);
            }
        }

        private static void ApplyStimulus(
            BaseAI ai,
            Character observer,
            AgentBehaviorProfile profile,
            Character? target,
            Vector3 stimulusPos,
            float deltaDetection)
        {
            SensoryTrack track = GetOrCreateTrack(ai);
            float now = Time.time;

            // Throttle repeated accumulation within the same 0.15s window
            if (now - track.LastStimulusTime < 0.12f && deltaDetection < 0.5f)
            {
                return;
            }

            if (target != null)
            {
                track.Target = target;
            }
            track.LastKnownPos = stimulusPos;
            track.LastStimulusTime = now;
            track.SearchTimeRemaining = ModConfig.SearchDurationSeconds.Value;
            track.DetectionLevel = Mathf.Clamp01(track.DetectionLevel + deltaDetection);

            DetectionStage prevStage = track.Stage;
            UpdateStageFromLevel(ai, observer, profile, track);

            if (prevStage < DetectionStage.Engaged && track.Stage == DetectionStage.Engaged && track.Target != null)
            {
                EnqueueAllyCall(observer, track.Target, stimulusPos, profile.CallForHelpRadius);
            }
        }

        private static void UpdateStageFromLevel(
            BaseAI ai,
            Character observer,
            AgentBehaviorProfile profile,
            SensoryTrack track)
        {
            if (track.DetectionLevel >= 0.85f)
            {
                track.Stage = DetectionStage.Engaged;
            }
            else if (track.DetectionLevel >= 0.50f)
            {
                track.Stage = DetectionStage.Alerted;
            }
            else if (track.DetectionLevel >= 0.20f)
            {
                track.Stage = DetectionStage.Suspicious;
            }
            else
            {
                track.Stage = DetectionStage.Unaware;
            }
        }

        private static void EnqueueAllyCall(Character caller, Character target, Vector3 pos, float radius)
        {
            if (radius <= 0.5f || _ringCount >= _allyCallRing.Length) return;

            _allyCallRing[_ringTail] = new PendingAllyCall
            {
                Caller = caller,
                Target = target,
                KnownPos = pos,
                Radius = radius
            };
            _ringTail = (_ringTail + 1) % _allyCallRing.Length;
            _ringCount++;
        }

        private static void ProcessOneQueuedAllyCall()
        {
            if (_ringCount <= 0) return;

            PendingAllyCall call = _allyCallRing[_ringHead];
            _ringHead = (_ringHead + 1) % _allyCallRing.Length;
            _ringCount--;

            if (call.Caller == null || call.Caller.IsDead() || call.Target == null) return;

            _allyQueryTemp.Clear();
            SpatialCharacterGrid.QueryRadius(call.Caller.transform.position, call.Radius, _allyQueryTemp, exclude: call.Caller);

            for (int i = 0; i < _allyQueryTemp.Count; i++)
            {
                Character ally = _allyQueryTemp[i];
                if (ally == null || ally.IsDead() || !BaseAI.IsEnemy(ally, call.Target)) continue;
                if (BaseAI.IsEnemy(call.Caller, ally)) continue;

                BaseAI allyAi = ally.GetBaseAI();
                if (allyAi == null) continue;

                SensoryTrack allyTrack = GetOrCreateTrack(allyAi);
                if (allyTrack.Stage < DetectionStage.Alerted)
                {
                    allyTrack.Target = call.Target;
                    allyTrack.LastKnownPos = call.KnownPos;
                    allyTrack.LastStimulusTime = Time.time;
                    allyTrack.SearchTimeRemaining = ModConfig.SearchDurationSeconds.Value;
                    allyTrack.DetectionLevel = Mathf.Max(allyTrack.DetectionLevel, 0.65f);
                    allyTrack.Stage = DetectionStage.Alerted;
                }
            }
        }
    }

    /// <summary>
    /// First-class IAIBrainModule that drives Suspicious (look/turn toward sound) and Alerted (walk to last known position)
    /// investigation states inside the main BrainPipeline, preventing Postfix double-pathing.
    /// </summary>
    public sealed class InvestigateBrainModule : IAIBrainModule
    {
        public string Id => "VikingAI.SensoryInvestigate";

        private static readonly MethodInfo _moveToMethod =
            AccessTools.Method(typeof(BaseAI), "MoveTo");
        private static readonly MethodInfo _lookAtMethod =
            AccessTools.Method(typeof(BaseAI), "LookAt");
        private static readonly MethodInfo _randomMovementMethod =
            AccessTools.Method(typeof(BaseAI), "RandomMovement");

        public float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile)
        {
            if (!ModConfig.EnableSensoryPerceptionEngine.Value || !profile.UseSensoryEngine)
            {
                return 0f;
            }

            if (ai.IsAlerted() && ai is MonsterAI mai && mai.GetTargetCreature() != null)
            {
                SensoryTrack activeTrack = SensoryMemoryController.GetOrCreateTrack(ai);
                if (activeTrack.Stage == DetectionStage.Engaged)
                {
                    return 0f; // Yield to combat / pack module
                }
            }

            SensoryTrack track = SensoryMemoryController.GetOrCreateTrack(ai);
            if ((track.Stage == DetectionStage.Suspicious || track.Stage == DetectionStage.Alerted) &&
                track.SearchTimeRemaining > 0.1f)
            {
                return 55.0f;
            }

            return 0f;
        }

        public bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt)
        {
            SensoryTrack track = SensoryMemoryController.GetOrCreateTrack(ai);

            if (track.Stage == DetectionStage.Suspicious)
            {
                ai.StopMoving();
                _lookAtMethod?.Invoke(ai, new object[] { track.LastKnownPos + Vector3.up * 1.2f });
                return true;
            }

            if (track.Stage == DetectionStage.Alerted && _moveToMethod != null)
            {
                float dist = Utils.DistanceXZ(character.transform.position, track.LastKnownPos);
                if (dist > 2.2f)
                {
                    _moveToMethod.Invoke(ai, new object[] { dt, track.LastKnownPos, 1.5f, false });
                }
                else
                {
                    _randomMovementMethod?.Invoke(ai, new object[] { dt, track.LastKnownPos, true });
                }
                return true;
            }

            return false;
        }
    }
}
