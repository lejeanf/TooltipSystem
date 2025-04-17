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
        public Material iconMaterial;
        public float iconSizeModifierWhenHidden = 0.5f;
        public float iconSize = 0.5f;
        public float iconSpacing = -0.05f;
        public int iconPadding = 0;
        public string textInBetweenIcons = "ou";
        public float fontSizeForIconMode = 0.15f;
        public float canvasSizeX = 2;
        public float canvasSizeY = 1;
        
        [Header("Common Settings")]
        public TMP_FontAsset textFont;
        public float animationDuration = 0.075f;
        
        [Header("Temporary")]
        public ToolTipAnimationEnum animationType;
        
    }
}
