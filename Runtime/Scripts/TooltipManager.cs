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
        [SerializeField] BoolEventChannelSO hmdStatusEventChannel;

        [Header("Broadcasting On")]
        [SerializeField] StringBoolEventChannelSO tooltipSender;

        private bool hmdStatus;

        private void OnEnable()
        {
            tooltipListener.OnEventRaised += value => SendTooltip(value);
            hmdStatusEventChannel.OnEventRaised += status => SetHMDStatus(status);
        }
        private void OnDisable() => Unsubscribe(); 
        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            tooltipListener.OnEventRaised -= value => SendTooltip(value);
        }

        public void SendTooltip(TooltipSO tooltipSO)
        {
            tooltipSender.RaiseEvent(CleanString(tooltipSO.Tooltip), hmdStatus);
        }

        public void SendTooltip(string tooltip)
        {
            tooltipSender.RaiseEvent(CleanString(tooltip), hmdStatus);
        }

        public string CleanString(string str)
        {
            return str.Trim();
        }

        private void SetHMDStatus(bool status)
        {
            hmdStatus = status;
        }
    }
}
