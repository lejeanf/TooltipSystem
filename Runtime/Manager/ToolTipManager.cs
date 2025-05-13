using jeanf.EventSystem;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jeanf.tooltip
{
    public class ToolTipManager : MonoBehaviour
    {
        [Tooltip("If disable all tooltips then no tooltip will be displayed except NavigationToolTip.")]
        [SerializeField] private bool _disableAllTooltips = false;
        
        [SerializeField] private BoolEventChannelSO hmdState;
        [SerializeField] private BoolEventChannelSO ipadState;
        [SerializeField] private ControlSchemeChannelSo controlSchemeChannelSo;
        
        public delegate void UpdateToolTipControlSchemeWithHmdDelegate(bool hmdSatus);
        public static UpdateToolTipControlSchemeWithHmdDelegate UpdateToolTipControlSchemeWithHmd;
        
        public delegate void UpdateToolTipControlSchemeDelegate(BroadcastControlsStatus.ControlScheme controlScheme);
        public static UpdateToolTipControlSchemeDelegate UpdateToolTipControlScheme;
        
        public delegate void UpdateShowToolTipDelegate(bool isShowing);
        public static UpdateShowToolTipDelegate UpdateShowToolTip;
        
        public delegate void DisableTipDelegate();
        public static DisableTipDelegate DisableToolTip;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            BroadcastControlsStatus.SendControlScheme += UpdateControlScheme;
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            
            controlSchemeChannelSo.OnEventRaised += UpdateControlScheme;
            hmdState.OnEventRaised += UpdateTooltip;
            ipadState.OnEventRaised += OnIpadState;
        }

        private void UnSubscribe()
        {
            BroadcastControlsStatus.SendControlScheme -= UpdateControlScheme;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            controlSchemeChannelSo.OnEventRaised -= UpdateControlScheme;
            hmdState.OnEventRaised -= UpdateTooltip;
            ipadState.OnEventRaised -= OnIpadState;
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            UpdateToolTipControlScheme?.Invoke(controlScheme);
        }

        private void UpdateTooltip(bool hmdState)
        {
            UpdateToolTipControlSchemeWithHmd?.Invoke(hmdState);
        }

        private void OnIpadState(bool ipadState)
        {
            UpdateShowToolTip?.Invoke(!ipadState);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if(_disableAllTooltips)
                DisableToolTip?.Invoke();
        }
        
    }
}