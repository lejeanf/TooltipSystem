using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Optional per-candidate-position settings. Put this on a tooltip's candidate-position Transform to
    /// override the tooltip's icon side and/or billboarding when that position is the one being used (e.g.
    /// icon on the right when the tooltip sits on the player's left, and vice-versa).
    /// </summary>
    public class TooltipAnchor : MonoBehaviour
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

        // The candidate positions are visualised centrally by InteractableTooltipController.OnDrawGizmos
        // (amber spheres + lines from the root when the controller isn't selected), so no per-anchor gizmo
        // is needed here — that avoids a doubled wire sphere at each position.
    }
}
