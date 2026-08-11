using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Chair-side sit/stand hint: drives an <see cref="InteractableTooltipController"/> on a seat so
    /// the player is told how to sit (and, once seated on THIS seat, how to stand) with the right
    /// input for the active control scheme — the per-mode wording comes from the two
    /// <see cref="TooltipActionContentSo"/> assets (M&amp;K / Gamepad / VR each get their own text+icon).
    ///
    /// The tooltip is FIXED in place: parked slightly above the Seat's sit anchor, label parallel to
    /// the sit anchor's facing (set the controller's Billboard mode to Never to keep it that way).
    /// Drop this next to the Seat, point it at the tooltip controller (auto-found in children), and
    /// assign the sit + stand content assets.
    /// </summary>
    public class SeatTooltip : MonoBehaviour
    {
        private const string LogPrefix = "[TooltipSystem]";

        [Tooltip("The seat this tooltip belongs to. Auto-found on this object / its parents when empty.")]
        [SerializeField] private Seat seat;
        [Tooltip("The tooltip to drive. Auto-found in children when empty.")]
        [SerializeField] private InteractableTooltipController tooltip;

        [Header("Placement (fixed, above the sit anchor)")]
        [Tooltip("How far above the sit anchor the tooltip sits.")]
        [SerializeField] private float heightAboveSitAnchor = 0.45f;

        [Header("Content")]
        [Tooltip("Per-mode 'how to sit' text/icon — shown while the player is NOT seated here.")]
        [SerializeField] private TooltipActionContentSo sitContent;
        [Tooltip("Per-mode 'how to stand' text/icon — shown while the player IS seated on this seat.")]
        [SerializeField] private TooltipActionContentSo standContent;

        private void OnEnable()
        {
            if (seat == null) seat = GetComponentInParent<Seat>();
            if (tooltip == null) tooltip = GetComponentInChildren<InteractableTooltipController>(true);

            if (seat == null || tooltip == null)
            {
                Debug.LogWarning($"{LogPrefix} SeatTooltip on '{name}': " +
                    (seat == null ? "no Seat on this object or its parents" : "no InteractableTooltipController in children") +
                    " — the sit/stand hint is disabled. Put this next to the chair's Seat and give it a tooltip child.", this);
                enabled = false;
                return;
            }

            PositionAboveSitAnchor();
            PlayerEvents.SeatedChanged += OnSeatedChanged;
            ApplyContent();
        }

        private void OnDisable()
        {
            PlayerEvents.SeatedChanged -= OnSeatedChanged;
        }

        private void OnSeatedChanged(bool _) => ApplyContent();

        // Park the tooltip above the sit anchor, label parallel to the sit anchor's facing.
        private void PositionAboveSitAnchor()
        {
            var anchor = seat.SitAnchor;
            tooltip.transform.SetPositionAndRotation(
                anchor.position + Vector3.up * heightAboveSitAnchor,
                Quaternion.Euler(0f, anchor.eulerAngles.y, 0f));
        }

        // Not seated (or seated elsewhere) -> how to sit; seated on THIS seat -> how to stand.
        private void ApplyContent()
        {
            var controller = SitController.Instance;
            var seatedHere = controller != null && controller.IsSeated
                             && controller.CurrentSeatId == seat.GetSeatData().SeatId;
            tooltip.SetActionContent(seatedHere ? standContent : sitContent);
        }

#if UNITY_EDITOR
        // Live repositioning while tuning the height in the inspector.
        private void OnValidate()
        {
            if (seat == null || tooltip == null || !isActiveAndEnabled) return;
            PositionAboveSitAnchor();
        }
#endif
    }
}
