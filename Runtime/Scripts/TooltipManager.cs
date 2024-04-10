using jeanf.EventSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    public class TooltipManager : MonoBehaviour
    {
        [Header("Listening On")]
        //This most likely needs to be changed into a TooltipEventSender
        [SerializeField] StringEventChannelSO tooltipListener;
        [SerializeField] BoolEventChannelSO hmdStatusEventChannel;

        [Header("Broadcasting On")]
        [SerializeField] StringBoolEventChannelSO tooltipSender;

        private bool hmdStatus;

        //PlayerInput component will be needed here, get it on player

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
            hmdStatusEventChannel.OnEventRaised -= status => SetHMDStatus(status);

        }

        public void SendTooltip(TooltipSO tooltipSO)
        {
            tooltipSender.RaiseEvent(CleanString(tooltipSO.Tooltip), hmdStatus);

            //Tooltip received, check type in switchCase
            //Depending on type, either send tooltip directly or send tooltip but with GetBindingsInput as a value
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

        ////Call this function if tooltip is a control-type tooltip, checks what controlScheme we have then get only the right inputs to send in the string
        //private void GetBindingsInput()
        //{

        //}
    }
}
