using jeanf.EventSystem;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jeanf.tooltip
{
    public class ToolTipManager : MonoBehaviour
    {
        public bool isDebug = false;
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
        private bool _punctualTooltipsWereActiveBeforeIpad = false;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            BroadcastControlsStatus.SendControlScheme += UpdateControlScheme;
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            controlSchemeChannelSo.OnEventRaised += UpdateControlScheme;
            hmdState.OnEventRaised += UpdateTooltip;
            //ipadState.OnEventRaised += OnIpadState;
        }

        private void UnSubscribe()
        {
            BroadcastControlsStatus.SendControlScheme -= UpdateControlScheme;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            controlSchemeChannelSo.OnEventRaised -= UpdateControlScheme;
            hmdState.OnEventRaised -= UpdateTooltip;
            //ipadState.OnEventRaised -= OnIpadState;
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            UpdateToolTipControlScheme?.Invoke(controlScheme);
        }

        private void UpdateTooltip(bool hmdState)
        {
            if(isDebug) Debug.Log($"[ToolTipManager] - UpdateTooltip: hmdState: {hmdState}, invoke event UpdateToolTipControlSchemeWithHmd.");
            UpdateToolTipControlSchemeWithHmd?.Invoke(hmdState);
        }

        private void OnIpadState(bool ipadState)
        {
            if (ipadState)
            {
                // iPad is being shown - remember if punctual tooltips were active and hide ALL tooltips
                _punctualTooltipsWereActiveBeforeIpad = ArePunctualTooltipsCurrentlyActive();
                if(isDebug) Debug.Log($"[ToolTipManager] - OnIpadState: invoke event UpdateShowToolTip.");
                UpdateShowToolTip?.Invoke(false);
            }
            else
            {
                // iPad is being hidden - handle different tooltip types differently
                HandleTooltipResumption();
            }
        }

        private void HandleTooltipResumption()
        {
            if(isDebug) Debug.Log($"[ToolTipManager] - HandleTooltipResumption");
            var helpTooltipControls = FindObjectsOfType<HelpToolTipControls>();
            var interactableTooltipControls = FindObjectsOfType<InteractableToolTipController>();
            
            // Handle permanent tooltips
            foreach (var control in interactableTooltipControls)
            {
                if (control.IsPermanentTooltip)
                {
                    if(isDebug) Debug.Log($"[ToolTipManager] - control.NotifyIpadHidden()");
                    control.NotifyIpadHidden();
                }
            }
            
            // Handle punctual tooltips
            foreach (var control in helpTooltipControls)
            {
                if (!control.IsPermanentTooltip)
                {
                    if (_punctualTooltipsWereActiveBeforeIpad && control.HasIncompleteTooltip())
                    {
                        if(isDebug) Debug.Log($"[ToolTipManager] - control.ResumeTooltipAfterInterruption()");
                        control.ResumeTooltipAfterInterruption();
                    }
                }
            }
            
            foreach (var control in interactableTooltipControls)
            {
                if (!control.IsPermanentTooltip)
                {
                    if (_punctualTooltipsWereActiveBeforeIpad && control.HasIncompleteTooltip())
                    {
                        if(isDebug) Debug.Log($"[ToolTipManager] - control.ResumeTooltipAfterInterruption()");
                        control.ResumeTooltipAfterInterruption();
                    }
                }
            }
            
            _punctualTooltipsWereActiveBeforeIpad = false;
        }

        private bool ArePunctualTooltipsCurrentlyActive()
        {
            var helpTooltipControls = FindObjectsOfType<HelpToolTipControls>();
            foreach (var control in helpTooltipControls)
            {
                if (!control.IsPermanentTooltip && control.IsShowingTooltip)
                    return true;
            }
            
            var interactableTooltipControls = FindObjectsOfType<InteractableToolTipController>();
            foreach (var control in interactableTooltipControls)
            {
                if (!control.IsPermanentTooltip && control.IsShowingTooltip)
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