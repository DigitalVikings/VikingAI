using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingAI.Core;

namespace VikingAI.Tactics
{
    public sealed class TargetCoverState
    {
        public float IlluminationFactor = 1.0f;
        public float FoliageConcealment = 0.0f;
        public float AcousticRadius = 5.0f;
        public float LastSampleTime = -999f;
    }

    /// <summary>
    /// Time-sliced environmental stealth and acoustic sampler.
    /// Computes physical sunlight/shadow raycasts via RenderSettings.sun, round-robin bush/foliage cover,
    /// and mathematical equipment-weight acoustic profiles with zero per-frame allocations.
    /// </summary>
    public static class EnvironmentCoverSampler
    {
        private static readonly ConditionalWeakTable<Character, TargetCoverState> _states =
            new ConditionalWeakTable<Character, TargetCoverState>();

        private static readonly Collider[] _overlapBuffer = new Collider[48];
        private static int _shadowLayerMask;

        public static TargetCoverState GetOrSample(Character target)
        {
            if (!_states.TryGetValue(target, out TargetCoverState state))
            {
                state = new TargetCoverState();
                _states.Add(target, state);
            }

            float now = Time.time;
            if (now - state.LastSampleTime >= 0.25f)
            {
                state.LastSampleTime = now;
                UpdateTargetCover(target, state);
            }

            return state;
        }

        private static void UpdateTargetCover(Character target, TargetCoverState state)
        {
            if (_shadowLayerMask == 0)
            {
                _shadowLayerMask = LayerMask.GetMask("terrain", "Default", "static_solid", "piece", "piece_nonsolid");
            }

            Vector3 center = target.GetCenterPoint();

            // 1. Illumination & Physical Shadow Raycast
            float light = 1.0f;
            if (EnvMan.instance != null)
            {
                if (EnvMan.IsNight())
                {
                    light = 0.42f;
                }
                else if (EnvMan.IsAfternoon())
                {
                    light = 0.82f;
                }
            }

            Light? sun = RenderSettings.sun;
            if (sun != null)
            {
                Vector3 toSun = -sun.transform.forward;
                if (toSun.y > 0.05f)
                {
                    if (Physics.Raycast(center, toSun, 80.0f, _shadowLayerMask))
                    {
                        // Target is standing in cast physical shade from terrain, trees, or structures
                        light *= 0.65f;
                    }
                }
            }

            // Check if illuminated by a nearby burning fire or torch
            foreach (Fireplace fp in PieceRegistry.Fireplaces)
            {
                if (fp == null || !fp.IsBurning()) continue;
                float distSq = (fp.transform.position - center).sqrMagnitude;
                if (distSq < 81.0f) // within 9m of firelight
                {
                    float boost = (1.0f - Mathf.Sqrt(distSq) / 9.0f) * 0.55f;
                    light = Mathf.Min(1.25f, light + boost);
                }
            }

            state.IlluminationFactor = Mathf.Clamp(light, 0.2f, 1.25f);

            // 2. Bush & Foliage Concealment
            float foliage = 0f;
            int hitCount = Physics.OverlapSphereNonAlloc(center, 1.85f, _overlapBuffer, _shadowLayerMask);
            int maxChecks = Mathf.Min(hitCount, 12);
            for (int i = 0; i < maxChecks; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                string name = col.gameObject.name;
                if (IsFoliageColliderName(name))
                {
                    foliage = target.IsCrouching() ? 0.48f : 0.22f;
                    break;
                }
            }
            state.FoliageConcealment = foliage;

            // 3. Mathematical Equipment Weight & Movement Acoustic Radius
            float equippedWeight = 0f;
            if (target is Humanoid hum)
            {
                Inventory inv = hum.GetInventory();
                if (inv != null)
                {
                    List<ItemDrop.ItemData> allItems = inv.GetAllItems();
                    for (int i = 0; i < allItems.Count; i++)
                    {
                        ItemDrop.ItemData item = allItems[i];
                        if (item != null && item.m_equipped && item.m_shared != null)
                        {
                            equippedWeight += item.GetWeight();
                        }
                    }
                }
            }

            // Armor weight penalty: 0.0 at <= 8 weight, up to +45% noise at heavy plate weights
            float armorNoiseMult = 1.0f + Mathf.Clamp01((equippedWeight - 8.0f) / 36.0f) * 0.45f;
            Vector3 vel = target.GetVelocity();
            vel.y = 0f;
            float speed = vel.magnitude;

            float baseNoise;
            if (target.InAttack())
            {
                baseNoise = 18.0f;
            }
            else if (speed < 0.25f)
            {
                baseNoise = 0.5f;
            }
            else if (target.IsCrouching())
            {
                baseNoise = 2.2f * armorNoiseMult;
            }
            else if (target.IsRunning() || speed > 5.0f)
            {
                baseNoise = 14.0f * armorNoiseMult;
            }
            else
            {
                baseNoise = 6.5f * armorNoiseMult;
            }

            state.AcousticRadius = Mathf.Max(baseNoise, target.GetNoiseRange() * 0.35f);
        }

        private static bool IsFoliageColliderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Fern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Raspberry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Blueberry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Cloudberry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Sapling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Thistle", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
