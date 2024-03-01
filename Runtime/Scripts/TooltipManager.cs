using jeanf.EventSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    public class TooltipManager : MonoBehaviour
    {
        [Header("Broadcasting On")]
        [SerializeField] StringEventChannelSO stringEventChannelSO;
        private bool DetectHMDStatus()
        {
            return true;
        }

        public void SendTooltip(TooltipSO tooltipSO)
        {
            stringEventChannelSO.RaiseEvent(tooltipSO.Tooltip);
        }
    }
}
