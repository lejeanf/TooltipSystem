using System.Collections.Generic;
using jeanf.propertyDrawer;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InteractableToolTipSO", menuName = "Tooltips/InteractableToolTipSO", order = 1)]
    public class InteractableToolTipInputSo : ScriptableObject
    {
        public InputActionReference XrInput;
        public InputActionReference GamepadInput;
        public InputActionReference KeyboardMouseInput;
        public InputActionReference freeCamInput;
        private Dictionary<BroadcastControlsStatus.ControlScheme, string> inputsDictionnary;
        
        private void OnEnable()
        {
            inputsDictionnary = new Dictionary<BroadcastControlsStatus.ControlScheme, string>();
            
            string xrInputName = GetBindingName(XrInput, BroadcastControlsStatus.ControlScheme.XR);
            inputsDictionnary.Add(BroadcastControlsStatus.ControlScheme.XR, xrInputName);
            
            string gamepadInputName = GetBindingName(GamepadInput, BroadcastControlsStatus.ControlScheme.Gamepad);
            inputsDictionnary.Add(BroadcastControlsStatus.ControlScheme.Gamepad, gamepadInputName);
            
            string keyboardMouseInputName = GetBindingName(KeyboardMouseInput, BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            inputsDictionnary.Add(BroadcastControlsStatus.ControlScheme.KeyboardMouse, keyboardMouseInputName);

            string freeCamInputName = GetBindingName(freeCamInput, BroadcastControlsStatus.ControlScheme.Freecam);
            inputsDictionnary.Add(BroadcastControlsStatus.controlScheme, freeCamInputName);
        }

        public string GetBindingName(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            if (inputsDictionnary.TryGetValue(controlScheme, out string bindingName))
                return bindingName;
            
            return "";
        }
        
        private string GetBindingName(InputActionReference inputAction, BroadcastControlsStatus.ControlScheme controlScheme)
        {
            if (inputAction is null || inputAction.action == null)
                return "Unassigned";

            string controlSchemeGroup = GetControlSchemeGroup(controlScheme);
            
            if (string.IsNullOrEmpty(controlSchemeGroup))
                return inputAction.action.GetBindingDisplayString();

            return inputAction.action.GetBindingDisplayString(InputBinding.MaskByGroup(controlSchemeGroup));
        }
        
        private string GetControlSchemeGroup(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            switch (controlScheme)
            {
                case BroadcastControlsStatus.ControlScheme.KeyboardMouse:
                    return "Keyboard&Mouse";
                case BroadcastControlsStatus.ControlScheme.Gamepad:
                    return "Gamepad";
                case BroadcastControlsStatus.ControlScheme.XR:
                    return "XR";
                case BroadcastControlsStatus.ControlScheme.Freecam:
                    return "FreeCam";
                default:
                    return null;
            }
        }
        
    }
}