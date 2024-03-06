using jeanf.EventSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    public class TooltipManager : MonoBehaviour
    {
        [Header("Listening On")]
        [SerializeField] StringEventChannelSO tooltipListener;

        [Header("Broadcasting On")]
        [SerializeField] StringEventChannelSO tooltipSender;
        private bool DetectHMDStatus()
        {
            return true;
        }


        private void OnEnable()
        {
            tooltipListener.OnEventRaised += value => SendTooltip(value);
        }
        private void OnDisable() => Unsubscribe(); 
        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            tooltipListener.OnEventRaised -= value => SendTooltip(value);
        }

        public void SendTooltip(TooltipSO tooltipSO)
        {
            tooltipSender.RaiseEvent(tooltipSO.Tooltip);
        }

        public void SendTooltip(string tooltip)
        {
            tooltipSender.RaiseEvent(tooltip);
        }
    }
}
