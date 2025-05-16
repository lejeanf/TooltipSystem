using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTip : MonoBehaviour
    {
        
        [SerializeField] private InteractableToolTipFar _tooltipFar;
        [SerializeField] private GameObject _tooltipClose;
        
        public GameObject TooltipClose => _tooltipClose;

        public void UpdateDescription(string description)
        {
            var text = _tooltipClose.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = description;
                Debug.Log(text.text);
            }
        }
        
        public void ArrangeRotation()
        {
            transform.localRotation = transform.parent.localRotation;
        }
        
        public void ShowFarTooltip()
        {
            _tooltipFar.UnFreeze();
            _tooltipFar.ShowImage();
        }

        public void ShowCloseTooltip()
        {
            _tooltipClose.SetActive(true);
        }

        public void HideFarTooltip()
        {
            _tooltipFar.HideImage();
            _tooltipFar.Freeze();
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