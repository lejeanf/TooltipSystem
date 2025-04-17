using jeanf.EventSystem;
using jeanf.vrplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    public class ToolTipManager : MonoBehaviour
    {
        [SerializeField] private BoolEventChannelSO hmdState; 
        [SerializeField] private ControlSchemeChannelSo controlSchemeChannelSo;
        
        public delegate void UpdateToolTipControlSchemeWithHmdDelegate(bool hmdSatus);
        public static UpdateToolTipControlSchemeWithHmdDelegate UpdateToolTipControlSchemeWithHmd;
        
        public delegate void UpdateToolTipControlSchemeDelegate(BroadcastControlsStatus.ControlScheme controlScheme);
        public static UpdateToolTipControlSchemeDelegate UpdateToolTipControlScheme;
        
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            BroadcastControlsStatus.SendControlScheme += UpdateControlScheme;
            
            controlSchemeChannelSo.OnEventRaised += UpdateControlScheme;
            hmdState.OnEventRaised += UpdateTooltip;
        }

        private void UnSubscribe()
        {
            BroadcastControlsStatus.SendControlScheme -= UpdateControlScheme;
            
            controlSchemeChannelSo.OnEventRaised -= UpdateControlScheme;
            hmdState.OnEventRaised -= UpdateTooltip;
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            UpdateToolTipControlScheme.Invoke(controlScheme);
        }

        private void UpdateTooltip(bool hmdState)
        {
            UpdateToolTipControlSchemeWithHmd.Invoke(hmdState);
        }
        
    }
}