using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTipManager : MonoBehaviour
    {
        private float _currentDot = 0;
        private bool _isToolTipDisplayed = false;
        private InteractableToolTipController _interactableToolTipController;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            InteractableToolTipController.RequestShowToolTip += RequestShowToolTip;
            InteractableToolTipController.WarnHideToolTip += WarnHideToolTip;
        }

        private void UnSubscribe()
        {
            InteractableToolTipController.RequestShowToolTip -= RequestShowToolTip;
            InteractableToolTipController.WarnHideToolTip -= WarnHideToolTip;
        }
        
        private bool RequestShowToolTip(float dot, InteractableToolTipController interactableToolTipController)
        {
            if(_interactableToolTipController == interactableToolTipController)
                return true;
            
            if (_isToolTipDisplayed)
            {
                if (_currentDot > dot)
                    return false;
                
                ReplaceCurrentInteractableController(dot, interactableToolTipController);
                return true;
            }

            ReplaceCurrentInteractableController(dot, interactableToolTipController);
            return true;
        }

        private void WarnHideToolTip(InteractableToolTipController interactableToolTipController)
        {
            if (_interactableToolTipController == interactableToolTipController)
            {
                _isToolTipDisplayed = false;
                _interactableToolTipController = null;
                _currentDot = 0;
            }
        }

        private void ReplaceCurrentInteractableController(float dot, InteractableToolTipController interactableToolTipController)
        {
            _isToolTipDisplayed = true;
            _interactableToolTipController = interactableToolTipController;
            _currentDot = dot;
        }
    }
    
}