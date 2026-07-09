using System.Collections.Generic;
using jeanf.universalplayer;
using UnityEngine.InputSystem;

namespace jeanf.tooltip
{
    public class HelpTooltipInputPressedService : IHelpTooltipService
    {
        private readonly Dictionary<BroadcastControlsStatus.ControlScheme, InputActionReference> _actions;
        private InputActionReference _currentAction;
        private readonly float _progressionAdded;
        
        public HelpTooltipInputPressedService(float progressionAdded, 
                                      Dictionary<BroadcastControlsStatus.ControlScheme, 
                                      InputActionReference> inputs, BroadcastControlsStatus.ControlScheme controlScheme)
        {
            _currentAction = inputs[controlScheme];
            _actions = inputs;
            _progressionAdded = progressionAdded;
        }
        
        public float Activate()
        {
            float progressionAdded = 0f;
            
            if (_currentAction.action.WasPerformedThisFrame())
                progressionAdded = _progressionAdded;
            
            return progressionAdded;
        }

        public void UpdateFromControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            _currentAction = _actions[controlScheme];
        }
    }
}