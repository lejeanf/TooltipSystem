using UnityEngine;

namespace jeanf.tooltip
{
    public class TooltipGazeArbiter : MonoBehaviour
    {
        public bool isDebug = false;
        private float _currentDot = 0;
        private bool _isTooltipDisplayed = false;
        private InteractableTooltipController _interactableTooltipController;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            InteractableTooltipController.RequestShowTooltip += RequestShowTooltip;
            InteractableTooltipController.WarnHideTooltip += WarnHideTooltip;
        }

        private void UnSubscribe()
        {
            InteractableTooltipController.RequestShowTooltip -= RequestShowTooltip;
            InteractableTooltipController.WarnHideTooltip -= WarnHideTooltip;
        }
        
        private bool RequestShowTooltip(float dot, InteractableTooltipController interactableTooltipController)
        {
            if(_interactableTooltipController == interactableTooltipController)
                return true;
            
            if (_isTooltipDisplayed)
            {
                if (_currentDot > dot)
                    return false;
                
                ReplaceCurrentInteractableController(dot, interactableTooltipController);
                return true;
            }

            ReplaceCurrentInteractableController(dot, interactableTooltipController);
            return true;
        }

        private void WarnHideTooltip(InteractableTooltipController interactableTooltipController)
        {
            if (_interactableTooltipController != interactableTooltipController) return;
            if(isDebug) Debug.Log($"[TooltipGazeArbiter] - WarnHideTooltip");
            _isTooltipDisplayed = false;
            _interactableTooltipController = null;
            _currentDot = 0;
        }

        private void ReplaceCurrentInteractableController(float dot, InteractableTooltipController interactableTooltipController)
        {
            if(isDebug) Debug.Log($"[TooltipGazeArbiter] - ReplaceCurrentInteractableController");
            _isTooltipDisplayed = true;
            _interactableTooltipController = interactableTooltipController;
            _currentDot = dot;
        }
    }
    
}