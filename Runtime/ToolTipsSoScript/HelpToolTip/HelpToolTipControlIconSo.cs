using System.Collections.Generic;
using System.Linq;
using jeanf.vrplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    [CreateAssetMenu(fileName = "HelpToolTipControlIconSo", menuName = "Tooltips/HelpToolTipControlIconSo", order = 1)]
    public class HelpToolTipControlIconSo : ScriptableObject
    {
        public Sprite mouseLook;
        public Sprite gamepadLook;
        public Sprite xrLook;
        public Sprite keyboardMove;
        public Sprite gamepadMove;
        public Sprite xrMove;
        public Sprite keyboardIpad;
        public Sprite gamepadIpad;
        public Sprite xrIpad;
        
        private Dictionary<HelpToolTipControlType, 
                                Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>> _iconDictionary;
        
        private Dictionary<BroadcastControlsStatus.ControlScheme, Sprite> _internDictionaryLook;
        private Dictionary<BroadcastControlsStatus.ControlScheme, Sprite> _internDictionaryMove;
        private Dictionary<BroadcastControlsStatus.ControlScheme, Sprite> _internDictionaryIpad;

        private void OnEnable()
        {
            _iconDictionary = new Dictionary<HelpToolTipControlType, Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>>();
            
            _internDictionaryLook = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>();
            _internDictionaryLook.Add(BroadcastControlsStatus.ControlScheme.KeyboardMouse, mouseLook);
            _internDictionaryLook.Add(BroadcastControlsStatus.ControlScheme.Gamepad, gamepadLook);
            _internDictionaryLook.Add(BroadcastControlsStatus.ControlScheme.XR, xrLook);
            _iconDictionary.Add(HelpToolTipControlType.HowToLook, _internDictionaryLook);
            
            _internDictionaryMove = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>();
            _internDictionaryMove.Add(BroadcastControlsStatus.ControlScheme.KeyboardMouse, keyboardMove);
            _internDictionaryMove.Add(BroadcastControlsStatus.ControlScheme.Gamepad, gamepadMove);
            _internDictionaryMove.Add(BroadcastControlsStatus.ControlScheme.XR, xrMove);
            _iconDictionary.Add(HelpToolTipControlType.HowToMove, _internDictionaryMove);
            
            _internDictionaryIpad = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>();
            _internDictionaryIpad.Add(BroadcastControlsStatus.ControlScheme.KeyboardMouse, keyboardIpad);
            _internDictionaryIpad.Add(BroadcastControlsStatus.ControlScheme.Gamepad, gamepadIpad);
            _internDictionaryIpad.Add(BroadcastControlsStatus.ControlScheme.XR, xrIpad);
            _iconDictionary.Add(HelpToolTipControlType.InputPressed, _internDictionaryIpad);
        }

        public Sprite GetIcon(HelpToolTipControlType controlType, BroadcastControlsStatus.ControlScheme controlScheme)
        {
            var internDictionary = _iconDictionary
                .Where(pair => controlType.Equals(pair.Key))
                .Select(pair => pair.Value)
                .FirstOrDefault();

            return internDictionary?
                .Where(pair => controlScheme.Equals(pair.Key))
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }
    }
}
