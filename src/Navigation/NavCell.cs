using System;
using UnityEngine;

namespace VikingAI.Navigation
{
    [Flags]
    public enum NavSurfaceFlags : ushort
    {
        None         = 0,
        Walkable     = 1 << 0,
        Water        = 1 << 1,
        Structure    = 1 << 2,
        Doorway      = 1 << 3,
        Destructible = 1 << 4,
        Roofed       = 1 << 5,
        Hazard       = 1 << 6,
        SteepSlope   = 1 << 7
    }

    /// <summary>
    /// Unmanaged 16-byte 2.5D navigation span representing a walkable floor surface,
    /// its vertical ceiling clearance, true radial obstacle clearance, and structural breach cost.
    /// </summary>
    public struct NavCell
    {
        public float FloorY;
        public float CeilingClearance;
        public float TrueClearanceRadius;
        public ushort StructuralHp;
        public NavSurfaceFlags Flags;

        public bool IsWalkable => (Flags & NavSurfaceFlags.Walkable) != 0;
        public bool IsWater => (Flags & NavSurfaceFlags.Water) != 0;
        public bool IsDoorway => (Flags & NavSurfaceFlags.Doorway) != 0;
        public bool IsDestructible => (Flags & NavSurfaceFlags.Destructible) != 0;
    }
}
