using jeanf.EventSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using jeanf;

namespace jeanf.tooltip
{
    public class TooltipManager : MonoBehaviour
    {
        [Header("Listening On")]
        //This most likely needs to be changed into a TooltipEventSender
        [SerializeField] TooltipEventChannelSO tooltipListener;
        [SerializeField] BoolEventChannelSO hmdStatusEventChannel;

        [Header("Broadcasting On")]
        [SerializeField] StringBoolEventChannelSO tooltipSender;

        private bool hmdStatus;

        [SerializeField] PlayerInput playerInput;

        private void OnEnable()
        {
            tooltipListener.OnEventRaised += value => SendTooltip(value);
            hmdStatusEventChannel.OnEventRaised += status => SetHMDStatus(status);
        }

        private void Start()
        {
            playerInput = FindObjectOfType<PlayerInput>();
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
            switch (tooltipSO.tooltipType)
            {
                case TooltipSO.TooltipType.ControlsTooltip:
                    tooltipSender.RaiseEvent(CleanString(tooltipSO.Tooltip.Replace("Bindings", GetBindingsInput(tooltipSO))), hmdStatus);
                    break;
                case TooltipSO.TooltipType.QuestTooltip:
                    break;
                default:
                    break;
            }
        }


        public string CleanString(string str)
        {
            return str.Trim();
        }

        private void SetHMDStatus(bool status)
        {
            hmdStatus = status;
        }

        //Call this function if tooltip is a control-type tooltip, checks what controlScheme we have then get only the right inputs to send in the string
        private string GetBindingsInput(TooltipSO tooltipSO)
        {
            string bindingsToDisplay = "";
            foreach(InputAction inputAction in playerInput.currentActionMap.actions)
            {
                if (inputAction.name == $"{CutTooltip(tooltipSO)}")
                {
                    foreach(InputControl control in inputAction.controls)
                    {
                        bindingsToDisplay += $"{control.name}";
                    }
                }
            }
            return bindingsToDisplay;
        }


        private string CutTooltip(TooltipSO tooltipSO)
        {
            Debug.Log(tooltipSO.Tooltip.IndexOf("to"));
            int position = tooltipSO.Tooltip.IndexOf("to");
            if (position >= 0)
            {
                string beforeTo = tooltipSO.Tooltip.Remove(0, position + 3);
                Debug.Log(beforeTo);
                return beforeTo;
            }
            return null;
        }
    }
}
