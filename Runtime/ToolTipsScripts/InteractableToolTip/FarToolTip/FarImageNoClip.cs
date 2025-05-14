using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    [RequireComponent(typeof(RectTransform))]
    public class FarImageNoClip : MonoBehaviour
    {
        [SerializeField] private LayerMask obstacleLayers;

        private Camera mainCam;
        private RectTransform rectTransform;
        private Image image;
        private InteractableToolTipFar tooltipFar;

        void Start()
        {
            mainCam = Camera.main;
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();
            tooltipFar = transform.parent.GetComponent<InteractableToolTipFar>();
        }

        void Update()
        {
            if (image == null || mainCam == null)
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
