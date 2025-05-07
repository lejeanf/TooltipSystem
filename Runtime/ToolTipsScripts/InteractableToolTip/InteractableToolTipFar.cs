using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipFar : MonoBehaviour
    {
        [SerializeField] public GameObject tooltipFarImageGameObject;
        
        private bool _isPlayerInRange = false;
        public bool IsPlayerInRange => _isPlayerInRange;
        
        private Image _tooltipFarImage;

        private void Awake()
        {
            _tooltipFarImage = tooltipFarImageGameObject.GetComponent<Image>();
            _tooltipFarImage.enabled = false;
        }

        public void HideImage()
        {
            _tooltipFarImage.enabled = false;
        }

        public void ShowImage()
        {
            if(_isPlayerInRange)
                _tooltipFarImage.enabled = true;
        }

        public void UpdatePlayerInRange(bool isPlayerInRange)
        {
            _isPlayerInRange = isPlayerInRange;
        }
    }
}
