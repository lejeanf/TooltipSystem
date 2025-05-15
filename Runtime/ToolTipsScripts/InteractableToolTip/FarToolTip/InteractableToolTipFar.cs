using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipFar : MonoBehaviour
    {
        [SerializeField] public GameObject tooltipFarImageGameObject;
        
        private bool _isPlayerInRange = false;
        public bool IsPlayerInRange => _isPlayerInRange;
        
        private bool _isFrozen = false;
        
        private Image _tooltipFarImage;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            ToolTipManager.DisableToolTip += Freeze;
        }

        private void UnSubscribe()
        {
            ToolTipManager.DisableToolTip -= Freeze;
        }
        
        private void Awake()
        {
            _tooltipFarImage = tooltipFarImageGameObject.GetComponent<Image>();
            _tooltipFarImage.enabled = false;
        }

        public void HideImage()
        {
            if(!_isFrozen)
                _tooltipFarImage.enabled = false;
        }

        public void Freeze()
        {
            _isFrozen = true;
        }

        public void UnFreeze()
        {
            _isFrozen = false;
        }

        public void ShowImage()
        {
            if(_isPlayerInRange && !_isFrozen)
                _tooltipFarImage.enabled = true;
        }

        public void UpdatePlayerInRange(bool isPlayerInRange)
        {
            _isPlayerInRange = isPlayerInRange;
        }
    }
}
