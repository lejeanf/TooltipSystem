using jeanf.propertyDrawer;
using UnityEngine;

namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InteractableTooltipAnimationSO", menuName = "Tooltips/InteractableTooltipAnimationSO", order = 1)]
    public class InteractableTooltipAnimationSo : ScriptableObject
    {
        public float iconSizeModifierWhenHidden = 0.5f;
        public float animationDuration = 0.075f;
        public TooltipAnimationEnum animationType;
    }
}