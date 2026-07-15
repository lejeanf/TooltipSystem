using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Quick play-mode test: toggles a target GameObject on/off when EITHER the linked tooltip is clicked OR
    /// the object that tooltip points at is clicked. You only link the tooltip — this tester is a pure listener
    /// and can live anywhere (on the target, a test manager, etc.); it does NOT need to sit on the object.
    ///
    /// How it works: it subscribes to the tooltip's own <see cref="InteractableTooltipController.Clicked"/>
    /// event (so the tooltip click toggles), and at play time it adds a tiny forwarder to the tooltip's
    /// looked-at object so a mouse click there raises the SAME click — both flow through one path. (Object
    /// mouse-click is M&K/editor only; in VR the object would be a real interactable.)
    ///
    /// Setup: link Tooltip = the InteractableTooltipController, Target = the GameObject to show/hide. Done.
    /// </summary>
    public class TooltipClickTester : MonoBehaviour
    {
        [Tooltip("The tooltip whose click (and whose object's click) should toggle the target.")]
        [SerializeField] private InteractableTooltipController tooltip;

        [Tooltip("The GameObject shown/hidden on each click.")]
        [SerializeField] private GameObject target;

        [Tooltip("Log each toggle to the console.")]
        [SerializeField] private bool logToConsole = true;

        private void OnEnable()
        {
            if (tooltip == null)
            {
                Debug.LogWarning("[TooltipClickTester] No tooltip linked.", this);
                return;
            }

            // Tooltip click -> the tooltip's own Clicked event.
            tooltip.Clicked += OnClicked;

            // Object click -> make the looked-at object raise the SAME click (via the controller's RaiseClick),
            // so we never have to live on that object. M&K/editor only (OnMouseDown).
            var obj = tooltip.ObjectToBeViewed;
            if (obj != null)
            {
                var fwd = obj.GetComponent<TooltipObjectClickForwarder>();
                if (fwd == null) fwd = obj.AddComponent<TooltipObjectClickForwarder>();
                fwd.tooltip = tooltip;
            }
        }

        private void OnDisable()
        {
            if (tooltip != null) tooltip.Clicked -= OnClicked;
        }

        private void OnClicked()
        {
            if (target == null)
            {
                if (logToConsole) Debug.LogWarning("[TooltipClickTester] No target assigned.", this);
                return;
            }

            bool show = !target.activeSelf;
            target.SetActive(show);
            if (logToConsole)
                Debug.Log($"[TooltipClickTester] click → '{target.name}' {(show ? "SHOWN" : "HIDDEN")}", this);
        }
    }

    /// <summary>
    /// Forwards a mouse click on the tooltip's looked-at object to the controller's RaiseClick, so clicking the
    /// object raises the same channel as clicking the tooltip. Added automatically by TooltipClickTester at play
    /// time — not meant to be added by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public class TooltipObjectClickForwarder : MonoBehaviour
    {
        [HideInInspector] public InteractableTooltipController tooltip;
        private void OnMouseDown() { if (tooltip != null) tooltip.RaiseClick(); }
    }
}
