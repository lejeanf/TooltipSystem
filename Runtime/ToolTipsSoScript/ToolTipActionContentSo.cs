using System;
using jeanf.propertyDrawer;
using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// One asset per action that holds the icon + text to show for that action in each control scheme
    /// (Mouse&amp;Keyboard / Gamepad / VR / Freecam). The tooltip picks the entry for the current scheme;
    /// any mode left empty falls back to Mouse&amp;Keyboard. Replaces wiring a separate glyph-map SO + a
    /// 4-reference input SO per tooltip.
    /// </summary>
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "ToolTipActionContentSO", menuName = "Tooltips/ToolTip Action Content", order = 1)]
    public class ToolTipActionContentSo : ScriptableObject
    {
        [Serializable]
        public class ModeContent
        {
            public Sprite icon;
            [TextArea] public string text;
        }

        public ModeContent keyboardMouse = new ModeContent();
        public ModeContent gamepad = new ModeContent();
        public ModeContent xr = new ModeContent();
        public ModeContent freecam = new ModeContent();

        public ModeContent GetContent(BroadcastControlsStatus.ControlScheme scheme)
        {
            switch (scheme)
            {
                case BroadcastControlsStatus.ControlScheme.XR: return xr;
                case BroadcastControlsStatus.ControlScheme.Gamepad: return gamepad;
                case BroadcastControlsStatus.ControlScheme.Freecam: return freecam;
                default: return keyboardMouse;
            }
        }

        public string GetText(BroadcastControlsStatus.ControlScheme scheme)
        {
            var content = GetContent(scheme);
            if (content != null && !string.IsNullOrEmpty(content.text)) return content.text;
            return keyboardMouse != null ? keyboardMouse.text : string.Empty; // fallback to M&K
        }

        public Sprite GetIcon(BroadcastControlsStatus.ControlScheme scheme)
        {
            var content = GetContent(scheme);
            if (content != null && content.icon != null) return content.icon;
            return keyboardMouse != null ? keyboardMouse.icon : null; // fallback to M&K
        }
    }
}
