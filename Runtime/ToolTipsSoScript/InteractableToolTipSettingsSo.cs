using jeanf.propertyDrawer;
using UnityEngine;


namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InteractableToolTipSettingsSO", menuName = "Tooltips/InteractableToolTipSettingsSO", order = 1)]
    public class InteractableToolTipSettingsSo : ScriptableObject
    {
        public string tooltipName = "Tooltip";
        public string description = "";
        [Tooltip("1 : Need player to look directly at the target | 0 : Accept that player doesn't look the target")]
        public float fieldOfViewThreshold = 0.9855f;
        public float iconSizeModifierWhenHidden = 0.5f;
        public float animationDuration = 0.075f;
        public ToolTipAnimationEnum animationType;
        
    }
}
