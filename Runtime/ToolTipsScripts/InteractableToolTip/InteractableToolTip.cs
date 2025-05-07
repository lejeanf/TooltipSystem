using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTip : MonoBehaviour
    {
        
        [SerializeField] private InteractableToolTipFar _tooltipFar;
        [SerializeField] private GameObject _tooltipClose;
        
        public GameObject TooltipClose => _tooltipClose;

        private void Awake()
        {
            transform.rotation = transform.parent.rotation;
        }
        
        public void ShowFarTooltip()
        {
            _tooltipFar.ShowImage();
        }

        public void ShowCloseTooltip()
        {
            _tooltipClose.SetActive(true);
        }

        public void HideFarTooltip()
        {
            _tooltipFar.HideImage();
        }

        public void HideCloseTooltip()
        {
            _tooltipClose.SetActive(false);
        }

        public void LookTowardsTarget(Transform target)
        {
            if(_tooltipClose.activeSelf)
                _tooltipClose.transform.forward = target.forward;
            if(_tooltipFar.IsPlayerInRange)
                _tooltipFar.tooltipFarImageGameObject.transform.forward = target.forward;
        }
    }
}