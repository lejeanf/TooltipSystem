using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Optional per-candidate-position settings. Put this on a tooltip's candidate-position Transform to
    /// override the tooltip's icon side and/or billboarding when that position is the one being used (e.g.
    /// icon on the right when the tooltip sits on the player's left, and vice-versa).
    /// </summary>
    public class ToolTipAnchor : MonoBehaviour
    {
        /// <summary>Icon side for this position. <see cref="Inherit"/> = use the tooltip's default.</summary>
        public enum IconSide { Inherit, Left, Right }

        /// <summary>Billboarding for this position. <see cref="Inherit"/> = use the tooltip's setting.</summary>
        public enum Billboard { Inherit, Always, Never }

        [Tooltip("Icon side when this position is used. Inherit = use the tooltip's default icon side.")]
        public IconSide iconSide = IconSide.Inherit;

        [Tooltip("Billboarding when this position is used. Inherit = use the tooltip's Auto-orient mode.")]
        public Billboard billboard = Billboard.Inherit;

        [Tooltip("Use per-position billboard axis limits for this candidate instead of the controller's. Off = inherit. " +
                 "Each position sits at its own orientation, so the allowed facing arc is usually position-specific.")]
        public bool overrideBillboardConstraints = false;
        public BillboardConstraints billboardConstraints = new BillboardConstraints();

        /// <summary>null = inherit the controller's; otherwise this position's own billboard axis limits.</summary>
        public BillboardConstraints ConstraintsOverride => overrideBillboardConstraints ? billboardConstraints : null;

        /// <summary>null = inherit; otherwise the forced icon side (true = right).</summary>
        public bool? IconOnRightOverride =>
            iconSide == IconSide.Inherit ? (bool?)null : iconSide == IconSide.Right;

        /// <summary>null = inherit; otherwise the forced billboard state.</summary>
        public bool? BillboardOverride =>
            billboard == Billboard.Inherit ? (bool?)null : billboard == Billboard.Always;

#if UNITY_EDITOR
        // Editor-only marker so these otherwise-invisible candidate positions are visible in the Scene
        // view even when the tooltip controller isn't selected. Kept minimal (a small dot, no label) to
        // avoid clutter — the controller's inspector adds labels/handles when it's selected. Not built.
        private void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            float s = UnityEditor.HandleUtility.GetHandleSize(p) * 0.08f;
            Gizmos.color = new Color(1f, 0.78f, 0.2f, 0.6f); // amber, matches the tooltip
            Gizmos.DrawWireSphere(p, s);
        }
#endif
    }
}
