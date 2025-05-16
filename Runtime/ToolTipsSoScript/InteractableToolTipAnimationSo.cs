using jeanf.propertyDrawer;
using UnityEngine;

namespace jeanf.tooltip
{
    [ScriptableObjectDrawer]
    [CreateAssetMenu(fileName = "InteractableToolTipAnimationSO", menuName = "Tooltips/InteractableToolTipAnimationSO", order = 1)]
    public class InteractableToolTipAnimationSo : ScriptableObject
    {
        public float iconSizeModifierWhenHidden = 0.5f;
        public float animationDuration = 0.075f;
        public ToolTipAnimationEnum animationType;
    }
}