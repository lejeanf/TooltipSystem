using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    public class HelpToolTipLookService : IHelpToolTipService
    {
        private readonly Transform _cameraTransform;
        private readonly float _progressionAdded;
        
        private Quaternion _previousCameraRotation;

        public HelpToolTipLookService(float progressionAdded, Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
            _progressionAdded = progressionAdded;
            _previousCameraRotation = _cameraTransform.rotation;
        }

        public float Activate()
        {
            float progressionAdded = 0f;
            
            if (_previousCameraRotation != _cameraTransform.rotation)
                progressionAdded = _progressionAdded;
            
            _previousCameraRotation = _cameraTransform.rotation;
            
            return progressionAdded;
        }

        public void UpdateFromControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            
        }
    }
}