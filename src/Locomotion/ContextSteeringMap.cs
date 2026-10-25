using UnityEngine;

namespace VikingAI.Locomotion
{
    /// <summary>
    /// 16-ray radial Context Steering map (Interest[16] and Danger[16]) with sub-ray parabolic interpolation.
    /// Produces smooth, natural cornering and doorway alignment without jitter.
    /// </summary>
    public sealed class ContextSteeringMap
    {
        public const int RayCount = 16;
        public static readonly Vector3[] Directions = new Vector3[RayCount];

        public readonly float[] Interest = new float[RayCount];
        public readonly float[] Danger = new float[RayCount];

        static ContextSteeringMap()
        {
            for (int i = 0; i < RayCount; i++)
            {
                float angleRad = (i * 22.5f) * Mathf.Deg2Rad;
                Directions[i] = new Vector3(Mathf.Sin(angleRad), 0f, Mathf.Cos(angleRad));
            }
        }

        public void Clear()
        {
            for (int i = 0; i < RayCount; i++)
            {
                Interest[i] = 0f;
                Danger[i] = 0f;
            }
        }

        public void AddDirectionalInterest(Vector3 desiredDir, float weight, float falloffDot = 0.0f)
        {
            desiredDir.y = 0f;
            if (desiredDir.sqrMagnitude < 0.0001f) return;
            desiredDir.Normalize();

            for (int i = 0; i < RayCount; i++)
            {
                float dot = Vector3.Dot(Directions[i], desiredDir);
                if (dot > falloffDot)
                {
                    float scaled = ((dot - falloffDot) / (1f - falloffDot)) * weight;
                    if (scaled > Interest[i])
                    {
                        Interest[i] = scaled;
                    }
                }
            }
        }

        public void AddDirectionalDanger(Vector3 hazardDir, float dangerStrength, float spreadCos = 0.35f)
        {
            hazardDir.y = 0f;
            if (hazardDir.sqrMagnitude < 0.0001f) return;
            hazardDir.Normalize();

            for (int i = 0; i < RayCount; i++)
            {
                float dot = Vector3.Dot(Directions[i], hazardDir);
                if (dot > spreadCos)
                {
                    float scaled = ((dot - spreadCos) / (1f - spreadCos)) * dangerStrength;
                    if (scaled > Danger[i])
                    {
                        Danger[i] = scaled;
                    }
                }
            }
        }

        public Vector3 ResolveBestVector(Vector3 fallbackDir)
        {
            int bestIdx = -1;
            float bestScore = 0.001f;
            float[] masked = new float[RayCount];

            for (int i = 0; i < RayCount; i++)
            {
                float net = Mathf.Max(0f, Interest[i] - Danger[i]);
                masked[i] = net;
                if (net > bestScore)
                {
                    bestScore = net;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                return fallbackDir;
            }

            int leftIdx = (bestIdx - 1 + RayCount) % RayCount;
            int rightIdx = (bestIdx + 1) % RayCount;
            float leftVal = masked[leftIdx];
            float rightVal = masked[rightIdx];

            // Sub-slot interpolation between neighbors for smooth continuous angles
            Vector3 blended =
                Directions[bestIdx] * bestScore +
                Directions[leftIdx] * (leftVal * 0.35f) +
                Directions[rightIdx] * (rightVal * 0.35f);

            blended.y = fallbackDir.y;
            return blended.sqrMagnitude > 0.001f ? blended.normalized : fallbackDir;
        }
    }
}
