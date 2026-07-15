using UnityEngine;
using UnityEngine.Events;

namespace jeanf.tooltip
{
    /// <summary>
    /// Makes a tooltip clickable while keeping the package free of any game-project dependency.
    /// When a click is detected it invokes a <see cref="UnityEvent"/>; wire the game-side interaction to it
    /// in the Inspector.
    ///
    /// A <see cref="BoxCollider"/> is required and auto-sized to the tooltip's RectTransform, so the
    /// click detectors have something to hit. You only need to wire the <c>On Click</c> event by hand.
    ///
    /// Click DETECTION is wired in the scene/prefab, reusing existing components:
    ///   - Mouse &amp; Keyboard: the interactable's click hook (e.g. Highlight_Interactionable.TriggerFunction)
    ///   - VR: an <c>XRSimpleInteractable</c>'s <c>Select Entered</c> event
    /// Point either UnityEvent at <see cref="RaiseClick"/> in the Inspector.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TooltipClickRelay : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool isDebug = false;

        [Header("Click event (invoked on click)")]
        [Tooltip("Invoked when this tooltip is clicked. Wire the game-side interaction here.")]
        [SerializeField] private UnityEvent onClick;

        [Header("Auto collider")]
        [Tooltip("Depth (Z) of the auto-sized box collider. X/Y match the tooltip RectTransform.")]
        [SerializeField] private float colliderDepth = 0.01f;
        [Tooltip("Uniform padding added to the auto-sized collider's width/height.")]
        [SerializeField] private float colliderPadding = 0f;

        private void Awake() => EnsureColliderSized();

#if UNITY_EDITOR
        private void Reset() => EnsureColliderSized();
        private void OnValidate() => EnsureColliderSized();
#endif

        /// <summary>Wire this to your M&amp;K and VR click detectors (UnityEvents) in the Inspector.</summary>
        public void RaiseClick()
        {
            if (isDebug) Debug.Log($"[TooltipClickRelay] Click on '{name}'.", this);
            onClick?.Invoke();
        }

        private void EnsureColliderSized()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return; // RequireComponent guarantees one in the editor

            box.isTrigger = false; // physics-raycast click systems select solid colliders

            var rt = transform as RectTransform;
            if (rt == null) return;

            Rect r = rt.rect;
            box.size = new Vector3(r.width + colliderPadding, r.height + colliderPadding, colliderDepth);
            box.center = new Vector3(r.center.x, r.center.y, 0f);
        }
    }
}
