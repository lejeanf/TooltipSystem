using jeanf.vrplayer;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


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

