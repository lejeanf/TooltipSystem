using jeanf.vrplayer;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    [CreateAssetMenu(fileName = "ControlsTooltipSO", menuName = "Tooltips/ControlsTooltipSO", order = 1)]
    public class ControlsTooltipSO : TooltipSO
    {
        public string actionToAccomplish;

        public ActionSO actionSO;


        public override string Tooltip
        {
            get
            {
                if (actionSO != null)
                {
                    return $"{actionToAccomplish} {actionSO.bindings[0]} to {actionSO.name}";
                }
                else
                {
                    return $"{actionToAccomplish}";
                }
            }
        }
    }
}
