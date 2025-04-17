using jeanf.vrplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    public class HelpToolTipMoveService : IHelpToolTipService
    {
        private readonly Transform _cameraTransform;
        private readonly float _progressionAdded;
        
        private Vector3 _previousCameraPosition;
        
        public HelpToolTipMoveService(float progressionAdded, Transform cameraTransform)
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