using jeanf.EventSystem;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace jeanf.tooltip
{
    public class TooltipControlSchemeManager : MonoBehaviour
    {
        public bool isDebug = false;
        [Tooltip("If disable all tooltips then no tooltip will be displayed except NavigationTooltip.")]
        [SerializeField] private bool _disableAllTooltips = false;
        
        [SerializeField] private BoolEventChannelSO hmdState;
        [SerializeField] private BoolEventChannelSO ipadState;
        [SerializeField] private ControlSchemeChannelSo controlSchemeChannelSo;
        
        public delegate void UpdateTooltipControlSchemeWithHmdDelegate(bool hmdSatus);
        public static UpdateTooltipControlSchemeWithHmdDelegate UpdateTooltipControlSchemeWithHmd;
        
        public delegate void UpdateTooltipControlSchemeDelegate(BroadcastControlsStatus.ControlScheme controlScheme);
        public static UpdateTooltipControlSchemeDelegate UpdateTooltipControlScheme;
        
        public delegate void UpdateShowTooltipDelegate(bool isShowing);
        public static UpdateShowTooltipDelegate UpdateShowTooltip;
        
        public delegate void DisableTipDelegate();
        public static DisableTipDelegate DisableTooltip;
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
        }

        private void UnSubscribe()
        {
            BroadcastControlsStatus.SendControlScheme -= UpdateControlScheme;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            controlSchemeChannelSo.OnEventRaised -= UpdateControlScheme;
            hmdState.OnEventRaised -= UpdateTooltip;
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            UpdateTooltipControlScheme?.Invoke(controlScheme);
        }

        private void UpdateTooltip(bool hmdState)
        {
            if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - UpdateTooltip: hmdState: {hmdState}, invoke event UpdateTooltipControlSchemeWithHmd.");
            UpdateTooltipControlSchemeWithHmd?.Invoke(hmdState);
        }

        private void OnIpadState(bool ipadState)
        {
            if (ipadState)
            {
                _punctualTooltipsWereActiveBeforeIpad = ArePunctualTooltipsCurrentlyActive();
                if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - OnIpadState: invoke event UpdateShowTooltip.");
                UpdateShowTooltip?.Invoke(false);
            }
            else
            {
                HandleTooltipResumption();
            }
        }

        private void HandleTooltipResumption()
        {
            if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - HandleTooltipResumption");
            var helpTooltipControls = FindObjectsByType<HelpTooltipControls>(FindObjectsSortMode.None);;
            var interactableTooltipControls = FindObjectsByType<InteractableTooltipController>(FindObjectsSortMode.None);;
            
            foreach (var control in interactableTooltipControls)
            {
                if (control.IsPermanentTooltip)
                {
                    if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - control.NotifyIpadHidden()");
                    control.NotifyIpadHidden();
                }
            }
            
            foreach (var control in helpTooltipControls)
            {
                if (!control.IsPermanentTooltip)
                {
                    if (_punctualTooltipsWereActiveBeforeIpad && control.HasIncompleteTooltip())
                    {
                        if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - control.ResumeTooltipAfterInterruption()");
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
                        if(isDebug) Debug.Log($"[TooltipControlSchemeManager] - control.ResumeTooltipAfterInterruption()");
                        control.ResumeTooltipAfterInterruption();
                    }
                }
            }
            
            _punctualTooltipsWereActiveBeforeIpad = false;
        }

        private bool ArePunctualTooltipsCurrentlyActive()
        {
            var helpTooltipControls = FindObjectsByType<HelpTooltipControls>(FindObjectsSortMode.None);
            foreach (var control in helpTooltipControls)
            {
                if (!control.IsPermanentTooltip && control.IsShowingTooltip)
                    return true;
            }
            
            var interactableTooltipControls = FindObjectsByType<InteractableTooltipController>(FindObjectsSortMode.None);
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
                DisableTooltip?.Invoke();
        }
    }
}