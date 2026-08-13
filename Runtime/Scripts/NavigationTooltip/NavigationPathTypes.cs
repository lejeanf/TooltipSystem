using System;
using UnityEngine;

namespace jeanf.tooltip
{
    public enum NavigationPathMode
    {
        Shortest = 0,
        Orthogonal = 1
    }

    // Int-compatible with the retired NavigationTooltipType (LineRenderer=0 -> Line, SpriteLine=1 -> Dots)
    // so existing serialized scenes migrate without data loss.
    public enum NavigationPathStyle
    {
        Line = 0,
        Dots = 1,
        Arrows = 2
    }

    public enum NavigationPulseMode
    {
        Single = 0,
        Train = 1
    }

    [Serializable]
    public struct NavigationPathSettings
    {
        [Tooltip("Shortest follows the raw NavMesh path; Orthogonal rectilinearizes it into 90° legs (world X/Z), falling back per-segment when the geometry is diagonal.")]
        public NavigationPathMode mode;
        [Tooltip("Corner rounding radius in meters. Each corner's arc is validated against the navmesh and shrunk if it would clip geometry.")]
        [Range(0f, 2f)] public float cornerRadius;
        [Tooltip("Distance between markers in meters (drives the marker count).")]
        [Range(0.2f, 3f)] public float spacing;
        [Tooltip("First marker appears this many meters after the path start.")]
        [Range(0f, 3f)] public float startMargin;
        [Tooltip("Markers stop this many meters before the target.")]
        [Range(0f, 3f)] public float endMargin;
        [Tooltip("Re-center orthogonal legs inside openings (doorways, corridors) so the path crosses where a human would walk.")]
        public bool centerLegs;
        [Tooltip("Maximum sideways shift when re-centering a leg, in meters.")]
        [Range(0f, 2f)] public float maxLegShift;

        public static NavigationPathSettings Default => new NavigationPathSettings
        {
            mode = NavigationPathMode.Orthogonal,
            cornerRadius = 1.5f,
            spacing = 0.5f,
            startMargin = 0.79f,
            endMargin = 1.13f,
            centerLegs = true,
            maxLegShift = 1f
        };
    }
}
