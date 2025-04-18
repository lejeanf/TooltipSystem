using UnityEngine;

namespace jeanf.tooltip
{
    public abstract class TooltipSO : ScriptableObject
    {
        public enum TooltipType
        {
            ControlsTooltip,
            QuestTooltip
        }

        public TooltipType tooltipType;

        public abstract string Tooltip { get; }
    }
}

