using jeanf.EventSystem;
using UnityEngine;

namespace jeanf.tooltip
{
    public class TooltipStringSender : MonoBehaviour
    {
        [Header("Broadcasting On")]
        [SerializeField] StringEventChannelSO tooltipChannelToBroadcastOn;


        public void SendTooltip(TooltipSO tooltipToSend)
        {
            tooltipChannelToBroadcastOn.RaiseEvent(tooltipToSend.Tooltip);
        }

    }
}

