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
        
        // NEW: Track tooltip state before iPad interruption
        private bool _tooltipsWereActiveBeforeIpad = false;
        
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
            if (ipadState)
            {
                // iPad is being shown - remember if tooltips were active and hide them
                _tooltipsWereActiveBeforeIpad = AreTooltipsCurrentlyActive();
                UpdateShowToolTip?.Invoke(false);
            }
            else
            {
                // iPad is being hidden - only show tooltips if they were active before AND still needed
                if (_tooltipsWereActiveBeforeIpad && ShouldResumeTooltips())
                {
                    UpdateShowToolTip?.Invoke(true);
                }
                _tooltipsWereActiveBeforeIpad = false;
            }
        }

        private bool AreTooltipsCurrentlyActive()
        {
            // Check if any HelpToolTipControls are currently showing tooltips
            var helpTooltipControls = FindObjectsOfType<HelpToolTipControls>();
            foreach (var control in helpTooltipControls)
            {
                if (control.IsShowingTooltip)
                    return true;
            }
            return false;
        }

        private bool ShouldResumeTooltips()
        {
            // Check if any HelpToolTipControls have incomplete tooltips that should resume
            var helpTooltipControls = FindObjectsOfType<HelpToolTipControls>();
            foreach (var control in helpTooltipControls)
            {
                if (control.HasIncompleteTooltip())
                    return true;
            }
            return false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if(_disableAllTooltips)
                DisableToolTip?.Invoke();
        }
    }
}