using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    [RequireComponent(typeof(RectTransform))]
    public class FarImageNoClip : MonoBehaviour
    {
        [SerializeField] private LayerMask obstacleLayers;
        [Tooltip("How many frames between occlusion raycasts. Higher = cheaper (checks are staggered across tooltips so they don't all raycast on the same frame).")]
        [SerializeField, Min(1)] private int framesBetweenChecks = 5;

        private Camera mainCam;
        private RectTransform rectTransform;
        private Image image;
        private InteractableToolTipFar tooltipFar;
        private int _frameOffset;

        void Start()
        {
            mainCam = Camera.main;
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            tooltipFar = transform.parent.GetComponent<InteractableToolTipFar>();
            // Spread per-tooltip raycasts across frames so they don't all fire on the same frame.
            _frameOffset = (GetInstanceID() & 0x7fffffff) % Mathf.Max(1, framesBetweenChecks);
        }

        void Update()
        {
            if (image == null || mainCam == null)
                return;

            // Throttled + staggered: only this tooltip's slice of frames runs the raycast.
            if ((Time.frameCount + _frameOffset) % Mathf.Max(1, framesBetweenChecks) != 0)
                return;

            Vector3 worldPos = rectTransform.position;
            Vector3 dir = worldPos - mainCam.transform.position;
            float dist = dir.magnitude;

            if (Physics.Raycast(mainCam.transform.position, dir.normalized, out RaycastHit hit, dist, obstacleLayers))
            {
                tooltipFar.HideImage();
            }
            else
            {
                tooltipFar.ShowImage();
            }
        }
    }
}
