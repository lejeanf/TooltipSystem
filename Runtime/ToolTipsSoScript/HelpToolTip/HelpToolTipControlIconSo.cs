using System.Collections.Generic;
using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    [CreateAssetMenu(fileName = "HelpToolTipControlIconSo", menuName = "Tooltips/HelpToolTipControlIconSo", order = 1)]
    public class HelpToolTipControlIconSo : ScriptableObject
    {
        [Header("Look Icons")]
        public Sprite mouseLook;
        public Sprite gamepadLook;
        public Sprite xrLook;

        [Header("Move Icons")]
        public Sprite keyboardMove;
        public Sprite gamepadMove;
        public Sprite xrMove;

        [Header("Input Icons")]
        public Sprite keyboardIpad;
        public Sprite gamepadIpad;
        public Sprite xrIpad;

        private Dictionary<HelpToolTipControlType, Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>> _iconDictionary;

        private void OnEnable() => InitializeDictionaries();

        private void InitializeDictionaries()
        {
            _iconDictionary = new Dictionary<HelpToolTipControlType, Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>>();

            // Look icons
            var lookDict = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>
            {
                { BroadcastControlsStatus.ControlScheme.KeyboardMouse, mouseLook },
                { BroadcastControlsStatus.ControlScheme.Gamepad, gamepadLook },
                { BroadcastControlsStatus.ControlScheme.XR, xrLook }
            };
            _iconDictionary.Add(HelpToolTipControlType.HowToLook, lookDict);

            // Move icons
            var moveDict = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>
            {
                { BroadcastControlsStatus.ControlScheme.KeyboardMouse, keyboardMove },
                { BroadcastControlsStatus.ControlScheme.Gamepad, gamepadMove },
                { BroadcastControlsStatus.ControlScheme.XR, xrMove }
            };
            _iconDictionary.Add(HelpToolTipControlType.HowToMove, moveDict);

            // Input icons
            var inputDict = new Dictionary<BroadcastControlsStatus.ControlScheme, Sprite>
            {
                { BroadcastControlsStatus.ControlScheme.KeyboardMouse, keyboardIpad },
                { BroadcastControlsStatus.ControlScheme.Gamepad, gamepadIpad },
                { BroadcastControlsStatus.ControlScheme.XR, xrIpad }
            };
            _iconDictionary.Add(HelpToolTipControlType.InputPressed, inputDict);
        }

        public Sprite GetIcon(HelpToolTipControlType controlType, BroadcastControlsStatus.ControlScheme controlScheme)
        {
            if (_iconDictionary == null) 
            {
                InitializeDictionaries();
            }

            if (!_iconDictionary.ContainsKey(controlType))
            {
                return null;
            }

            var schemeDict = _iconDictionary[controlType];
            if (!schemeDict.ContainsKey(controlScheme))
            {
                return null;
            }

            Sprite sprite = schemeDict[controlScheme];

            return sprite;
        }
    }
}   