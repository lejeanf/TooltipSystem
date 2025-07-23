using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTip : MonoBehaviour
    {
        [Header("Tooltip debug")]
        public bool isDebug = false;
        [SerializeField] private InteractableToolTipFar _tooltipFar;
        [SerializeField] private GameObject _tooltipClose;
        
        public GameObject TooltipClose => _tooltipClose;

        public void UpdateDescription(string description)
        {
            if (_tooltipClose == null) return;
            var text = _tooltipClose.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = description;
                //Debug.Log(text.text);
            }
        }
        
        public void ArrangeRotation()
        {
            if(transform.parent != null)
                transform.localRotation = transform.parent.localRotation;
        }
        
        public void ShowFarTooltip()
        {
            if(isDebug) Debug.Log($"[InteractableToolTip] - ShowFarTooltip", this);
            if (_tooltipFar == null) return;
            _tooltipFar.UnFreeze();
            _tooltipFar.ShowImage();
        }

        public void ShowCloseTooltip()
        {
            if(isDebug) Debug.Log($"[InteractableToolTip] - ShowCloseTooltip", this);
            if (_tooltipClose == null) return;
            _tooltipClose.SetActive(true);
        }

        public void HideFarTooltip()
        {
            if(isDebug) Debug.Log($"[InteractableToolTip] - HideFarTooltip", this);
            if (_tooltipFar == null) return;
            _tooltipFar.HideImage();
            _tooltipFar.Freeze();
        }

        public void HideCloseTooltip()
        {
            if(isDebug) Debug.Log($"[InteractableToolTip] - HideCloseTooltip", this);
            if (_tooltipClose == null) return;
            _tooltipClose.SetActive(false);
        }

        public void LookTowardsTarget(Transform target)
        {
            if (_tooltipFar == null || _tooltipClose == null) return;
            if(_tooltipClose.activeSelf)
                _tooltipClose.transform.forward = target.forward;
            if(_tooltipFar.IsPlayerInRange)
                _tooltipFar.tooltipFarImageGameObject.transform.forward = target.forward;
        }
    }
}