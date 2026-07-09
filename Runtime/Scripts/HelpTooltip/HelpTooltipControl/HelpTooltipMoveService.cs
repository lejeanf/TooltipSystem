using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    public class HelpTooltipMoveService : IHelpTooltipService
    {
        private readonly Transform _cameraTransform;
        private readonly float _progressionAdded;
        
        private Vector3 _previousCameraPosition;
        
        public HelpTooltipMoveService(float progressionAdded, Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
            _progressionAdded = progressionAdded;
            _previousCameraPosition = _cameraTransform.position;
        }
        
        public float Activate()
        {
            float progressionAdded = 0f;
            
            if (_previousCameraPosition != _cameraTransform.position)
                progressionAdded = _progressionAdded;
            
            _previousCameraPosition = _cameraTransform.position;
            
            return progressionAdded;
        }

        public void UpdateFromControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            
        }
    }
}