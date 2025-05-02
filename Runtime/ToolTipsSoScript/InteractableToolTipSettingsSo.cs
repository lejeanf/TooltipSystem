using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    [CreateAssetMenu(fileName = "InteractableToolTipSettingsSO", menuName = "Tooltips/InteractableToolTipSettingsSO", order = 1)]
    public class InteractableToolTipSettingsSo : ScriptableObject
    {
        [Header("Text Mode Only")]
        public float fontSizeModifierWhenHidden = 0.5f;
        public float fontSizeForTextMode = 0.5f;
        
        [Header("Icon Mode Only")]
        public float iconSizeModifierWhenHidden = 0.5f;
        
        [Header("Common Settings")]
        public TMP_FontAsset textFont;
        public float animationDuration = 0.075f;
        
        [Header("Temporary")]
        public ToolTipAnimationEnum animationType;
        
    }
}
