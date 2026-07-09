using jeanf.propertyDrawer;
using UnityEngine;


namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InteractableTooltipSettingsSO", menuName = "Tooltips/InteractableTooltipSettingsSO", order = 1)]
    public class InteractableTooltipSettingsSo : ScriptableObject
    {
        public string tooltipName = "Tooltip";
        public string description = "";
        [Tooltip("1 : Need player to look directly at the target | 0 : Accept that player doesn't look the target")]
        public float fieldOfViewThreshold = 0.9855f;
        public InteractableTooltipAnimationSo animationSo;
    }
}
