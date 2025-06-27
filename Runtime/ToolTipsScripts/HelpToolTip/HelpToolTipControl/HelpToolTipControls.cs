using System;
using System.Collections.Generic;
using jeanf.universalplayer;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class HelpToolTipControls : ToolTip
    {
        [SerializeField] private List<HelpToolTipControlSo> helpToolTipControls;
        [Tooltip("In seconds")]
        [SerializeField] private float helpSwitchCooldown = 1.5f;
        [SerializeField] private GameObject helpGameObject;
        [SerializeField] private GameObject successGameObject;
        [SerializeField] private TMP_Text helpToolTipText;
        [SerializeField] private Image helpToolTipImage;
        [SerializeField] private Slider helpToolTipSlider;
        [SerializeField] private HelpToolTipControlIconSo helpToolTipControlIconSo;
        [SerializeField] private BroadcastControlsStatus.ControlScheme startingControlScheme;
        [Header("For testing")]
        [SerializeField] private InputAction inputToContinue;
        public static event Action ShowLookTooltipEvent;
        public static event Action ShowMoveTooltipEvent;
        public static event Action ShowInputTooltipEvent;
        public static event Action<HelpToolTipControlType> ShowSpecificTooltipEvent;
        public static event Action StopAllTooltipsEvent;
        
        private string _currentText;
        private Transform _cameraTransform;
        
        private BroadcastControlsStatus.ControlScheme _currentControlScheme;
        
        private ToolTipTimer _toolTipServiceTimerCooldown;
        private ToolTipTimer _toolTipSuccessTimerCooldown;
        private HelpToolTipControlSo _activeHelpToolTipControl;
        
        private IHelpToolTipService _currentHelpToolTipService;
        private HelpToolTipLookService _helpToolTipLookService;
        private HelpToolTipMoveService _helpToolTipMoveService;
        private HelpToolTipInputPressedService _helpToolTipInputPressedService;

        private Queue<HelpToolTipControlSo> _tooltipQueue;
        private bool _isShowingSequentialTooltips = false;
        private bool _isShowingSingleTooltip = false;
        
        private Dictionary<HelpToolTipControlType, IHelpToolTipService> _servicesByType;
        private Dictionary<HelpToolTipControlType, HelpToolTipControlSo> _tooltipsByType;
        
        private bool _isInitialized = false;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();
        
        private void Awake()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (_isInitialized) return;
            
            _cameraTransform = Camera.main?.transform;
            
            _toolTipServiceTimerCooldown = new ToolTipTimer();
            _toolTipSuccessTimerCooldown = new ToolTipTimer();
            
            _currentControlScheme = startingControlScheme;
            
            InitializeTooltipSystem();
            
            if (inputToContinue != null)
            {
                inputToContinue.Enable();
                inputToContinue.performed += OnContinuePressed;
            }
            
            _isInitialized = true;
        }

        private void InitializeTooltipSystem()
        {
            _tooltipQueue = new Queue<HelpToolTipControlSo>();
            _servicesByType = new Dictionary<HelpToolTipControlType, IHelpToolTipService>();
            _tooltipsByType = new Dictionary<HelpToolTipControlType, HelpToolTipControlSo>();
            
            SetUpAllServices();
            CacheTooltipsByType();
            
            HideHelpToolTip();
        }

        private void SetUpAllServices()
        {
            foreach (var tooltip in helpToolTipControls)
            {
                switch (tooltip.helpToolTipControlType)
                {
                    case HelpToolTipControlType.HowToLook:
                        if (_helpToolTipLookService == null)
                        {
                            _helpToolTipLookService = new HelpToolTipLookService(tooltip.progressionAdded, _cameraTransform);
                            _servicesByType[HelpToolTipControlType.HowToLook] = _helpToolTipLookService;
                        }
                        break;
                    case HelpToolTipControlType.HowToMove:
                        if (_helpToolTipMoveService == null)
                        {
                            _helpToolTipMoveService = new HelpToolTipMoveService(tooltip.progressionAdded, _cameraTransform);
                            _servicesByType[HelpToolTipControlType.HowToMove] = _helpToolTipMoveService;
                        }
                        break;
                    case HelpToolTipControlType.InputPressed:
                        if (_helpToolTipInputPressedService == null)
                        {
                            tooltip.actionRequiredWhenKeyBoard.action?.Enable();
                            tooltip.actionRequiredWhenGamepad.action?.Enable();
                            tooltip.actionRequiredWhenXr.action?.Enable();
                            
                            var inputs = new Dictionary<BroadcastControlsStatus.ControlScheme, InputActionReference>
                            {
                                { BroadcastControlsStatus.ControlScheme.KeyboardMouse, tooltip.actionRequiredWhenKeyBoard },
                                { BroadcastControlsStatus.ControlScheme.Gamepad, tooltip.actionRequiredWhenGamepad },
                                { BroadcastControlsStatus.ControlScheme.XR, tooltip.actionRequiredWhenXr }
                            };
                            _helpToolTipInputPressedService = new HelpToolTipInputPressedService(tooltip.progressionAdded, inputs, startingControlScheme);
                            _servicesByType[HelpToolTipControlType.InputPressed] = _helpToolTipInputPressedService;
                        }
                        break;
                }
            }
        }

        private void CacheTooltipsByType()
        {
            foreach (var tooltip in helpToolTipControls)
            {
                if (!_tooltipsByType.ContainsKey(tooltip.helpToolTipControlType))
                {
                    _tooltipsByType[tooltip.helpToolTipControlType] = tooltip;
                }
            }
        }

        private void Update()
        {
            if (!showToolTip) 
            { 
                HideHelpToolTip(); 
                return; 
            }
            
            // Ensure we have valid components before proceeding
            if (helpToolTipSlider == null || _activeHelpToolTipControl == null)
            {
                Debug.LogWarning("Update: Missing required components for tooltip display");
                return;
            }
            
            ShowHelpToolTip();
            
            // Handle VR skipping
            if (_currentControlScheme == BroadcastControlsStatus.ControlScheme.XR 
               && _activeHelpToolTipControl != null
               && !_activeHelpToolTipControl.canBeShownInVR)
            {
                if (_isShowingSequentialTooltips)
                    SetUpNextHelpToolTipWithoutTransition();
                else
                    StopCurrentTooltip();
            }
            
            // Handle completion
            if (Mathf.Approximately(helpToolTipSlider.value, 1)) 
            {
                Debug.Log("Tooltip completed (slider value = 1)");
                if (_isShowingSequentialTooltips)
                    SetUpNextHelpToolTip();
                else
                    StopCurrentTooltip();
            }
            
            // Update tooltip facing direction
            if (_cameraTransform != null)
                transform.forward = _cameraTransform.forward;
                
            // Debug current slider value
            if (Time.frameCount % 60 == 0) // Log every 60 frames to avoid spam
            {
                Debug.Log($"Current slider value: {helpToolTipSlider.value}, Service active: {_currentHelpToolTipService != null}");
            }
        }

        private void OnContinuePressed(InputAction.CallbackContext context)
        {
            if (_isShowingSequentialTooltips)
                SetUpNextHelpToolTip();
            else if (_isShowingSingleTooltip)
                StopCurrentTooltip();
        }

        private void ShowHelpToolTip()
        {
            if (_toolTipSuccessTimerCooldown.IsTimerRunning)
            {
                helpGameObject?.SetActive(false);
                successGameObject?.SetActive(true);
            }
            else
            {
                helpGameObject?.SetActive(true);
                successGameObject?.SetActive(false);
            }
        }

        private void HideHelpToolTip()
        {
            helpGameObject?.SetActive(false);
            successGameObject?.SetActive(false);
        }

        private void OnShowLookTooltip()
        {
            ShowSingleTooltip(HelpToolTipControlType.HowToLook);
        }

        private void OnShowMoveTooltip()
        {
            ShowSingleTooltip(HelpToolTipControlType.HowToMove);
        }

        private void OnShowInputTooltip()
        {
            ShowSingleTooltip(HelpToolTipControlType.InputPressed);
        }

        private void OnShowSpecificTooltip(HelpToolTipControlType tooltipType)
        {
            ShowSingleTooltip(tooltipType);
        }

        private void OnStopAllTooltips()
        {
            StopAllTooltips();
        }

        public void ShowSingleTooltip(HelpToolTipControlType tooltipType)
        {
            InitializeComponents();
            
            if (!_tooltipsByType.ContainsKey(tooltipType) || !_servicesByType.ContainsKey(tooltipType))
            {
                Debug.LogWarning($"Tooltip type {tooltipType} not found or not properly initialized.");
                return;
            }

            if (helpToolTipText == null || helpToolTipImage == null || helpToolTipSlider == null)
            {
                Debug.LogError("Cannot show tooltip: UI components are missing. Make sure the tooltip UI is properly set up in the scene.");
                return;
            }

            if (!Application.isPlaying)
            {
                Debug.LogWarning("Tooltips can only be shown during play mode.");
                return;
            }

            Debug.Log($"Showing tooltip for: {tooltipType}");

            StopAllTooltipsNow(); // Stop any current tooltips
            
            _isShowingSingleTooltip = true;
            _isShowingSequentialTooltips = false;
            
            _activeHelpToolTipControl = _tooltipsByType[tooltipType];
            _currentHelpToolTipService = _servicesByType[tooltipType];
            
            SetupTooltipDisplay();
            showToolTip = true;
            StartTooltipService();
        }

        public void ShowAllTooltipsSequentially()
        {
            // Ensure components are initialized
            InitializeComponents();
            
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Tooltips can only be shown during play mode.");
                return;
            }
            
            StopAllTooltipsNow();
            
            _isShowingSequentialTooltips = true;
            _isShowingSingleTooltip = false;
            
            // Populate queue with all tooltips
            _tooltipQueue.Clear();
            foreach (var tooltip in helpToolTipControls)
            {
                _tooltipQueue.Enqueue(tooltip);
            }
            
            if (_tooltipQueue.Count > 0)
            {
                showToolTip = true;
                SetUpNextHelpToolTip();
            }
        }

        private void SetupTooltipDisplay()
        {
            if (_activeHelpToolTipControl == null) 
            {
                Debug.LogWarning("SetupTooltipDisplay: _activeHelpToolTipControl is null");
                return;
            }
            
            Debug.Log($"Setting up tooltip display for: {_activeHelpToolTipControl.helpToolTipControlType}");
            
            // Add null checks for UI components with logging
            if (helpToolTipSlider != null)
            {
                helpToolTipSlider.value = 0f;
                Debug.Log("Slider value reset to 0");
            }
            else
            {
                Debug.LogError("SetupTooltipDisplay: _helpToolTipSlider is null!");
            }
                
            _currentText = _activeHelpToolTipControl.helpingMessage;
            
            if (helpToolTipText != null)
            {
                helpToolTipText.text = _currentText;
                Debug.Log($"Text set to: {_currentText}");
            }
            else
            {
                Debug.LogError("SetupTooltipDisplay: _helpToolTipText is null!");
            }
                
            if (helpToolTipImage != null)
            {
                helpToolTipImage.sprite = _activeHelpToolTipControl.HelpingImage;
                Debug.Log($"Image sprite set: {_activeHelpToolTipControl.HelpingImage?.name ?? "null"}");
            }
            else
            {
                Debug.LogError("SetupTooltipDisplay: _helpToolTipImage is null!");
            }
        }

        private void StartTooltipService()
        {
            if (_activeHelpToolTipControl != null && _toolTipServiceTimerCooldown != null)
            {
                _toolTipServiceTimerCooldown.StartTimer(_activeHelpToolTipControl.actionCooldown, ActivateHelpToolTipService);
            }
        }

        private void SetUpNextHelpToolTip()
        {
            if (!_isShowingSequentialTooltips || _tooltipQueue.Count == 0)
            {
                StopAllTooltipsNow();
                return;
            }
            
            _activeHelpToolTipControl = _tooltipQueue.Dequeue();
            SetupTooltipDisplay();
            UpdateHelpToolTipCurrentService();
            StartTransition();
        }
        
        private void SetUpNextHelpToolTipWithoutTransition()
        {
            if (!_isShowingSequentialTooltips || _tooltipQueue.Count == 0)
            {
                StopAllTooltipsNow();
                return;
            }
            
            _activeHelpToolTipControl = _tooltipQueue.Dequeue();
            SetupTooltipDisplay();
            UpdateHelpToolTipCurrentService();
            StartTooltipService();
        }

        private void UpdateHelpToolTipCurrentService()
        {
            if (_activeHelpToolTipControl != null && _servicesByType.ContainsKey(_activeHelpToolTipControl.helpToolTipControlType))
            {
                _currentHelpToolTipService = _servicesByType[_activeHelpToolTipControl.helpToolTipControlType];
            }
        }

        private void StartTransition()
        {
            if (_toolTipSuccessTimerCooldown != null)
                _toolTipSuccessTimerCooldown.StartTimer(helpSwitchCooldown, StartTooltipService);
        }

        private void ActivateHelpToolTipService()
        {
            if (_toolTipSuccessTimerCooldown != null && _toolTipSuccessTimerCooldown.IsTimerRunning) return;

            if (_currentHelpToolTipService != null && helpToolTipSlider != null && _activeHelpToolTipControl != null && _toolTipServiceTimerCooldown != null)
            {
                helpToolTipSlider.value += _currentHelpToolTipService.Activate();
                _toolTipServiceTimerCooldown.StartTimer(_activeHelpToolTipControl.actionCooldown, ActivateHelpToolTipService);
            }
        }

        private void StopCurrentTooltip()
        {
            _isShowingSingleTooltip = false;
            showToolTip = false;
            _toolTipServiceTimerCooldown?.StopTimer();
            _toolTipSuccessTimerCooldown?.StopTimer();
            _activeHelpToolTipControl = null;
            _currentHelpToolTipService = null;
        }

        private void StopAllTooltipsNow()
        {
            _isShowingSequentialTooltips = false;
            _isShowingSingleTooltip = false;
            _tooltipQueue?.Clear();
            showToolTip = false;
            _toolTipServiceTimerCooldown?.StopTimer();
            _toolTipSuccessTimerCooldown?.StopTimer();
            _activeHelpToolTipControl = null;
            _currentHelpToolTipService = null;
        }
        
        private void Subscribe()
        {
            ToolTipManager.UpdateShowToolTip += UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateAllHelpToolTipSo;
            ToolTipManager.UpdateToolTipControlScheme += UpdateAllHelpToolTipSo;
            ToolTipManager.DisableToolTip += DisableToolTip;
            
            // Subscribe to manual tooltip events
            ShowLookTooltipEvent += OnShowLookTooltip;
            ShowMoveTooltipEvent += OnShowMoveTooltip;
            ShowInputTooltipEvent += OnShowInputTooltip;
            ShowSpecificTooltipEvent += OnShowSpecificTooltip;
            StopAllTooltipsEvent += OnStopAllTooltips;
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateAllHelpToolTipSo;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateAllHelpToolTipSo;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            
            // Unsubscribe from manual tooltip events
            ShowLookTooltipEvent -= OnShowLookTooltip;
            ShowMoveTooltipEvent -= OnShowMoveTooltip;
            ShowInputTooltipEvent -= OnShowInputTooltip;
            ShowSpecificTooltipEvent -= OnShowSpecificTooltip;
            StopAllTooltipsEvent -= OnStopAllTooltips;
            
            _toolTipServiceTimerCooldown?.StopTimer();
            _toolTipSuccessTimerCooldown?.StopTimer();
            
            if (inputToContinue != null)
            {
                inputToContinue.performed -= OnContinuePressed;
                inputToContinue.Disable();
            }
        }

        private void UpdateAllHelpToolTipSo(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            // Update all cached tooltips
            foreach (var tooltip in helpToolTipControls)
            {
                Sprite newSprite = helpToolTipControlIconSo.GetIcon(tooltip.helpToolTipControlType, controlScheme);
                tooltip.UpdateSprite(newSprite);
            }
            
            // Update active tooltip if exists
            if (_activeHelpToolTipControl != null)
            {
                Sprite newActualSprite = helpToolTipControlIconSo.GetIcon(_activeHelpToolTipControl.helpToolTipControlType, controlScheme);
                _activeHelpToolTipControl.UpdateSprite(newActualSprite);
                if (helpToolTipImage != null)
                    helpToolTipImage.sprite = _activeHelpToolTipControl.HelpingImage;
            }
            
            // Update services
            _helpToolTipInputPressedService?.UpdateFromControlScheme(controlScheme);
            _currentHelpToolTipService?.UpdateFromControlScheme(controlScheme);
            
            _currentControlScheme = controlScheme;
        }
        
        private void UpdateAllHelpToolTipSo(bool hmdStatus)
        {
            var scheme = hmdStatus ? BroadcastControlsStatus.ControlScheme.XR : BroadcastControlsStatus.ControlScheme.KeyboardMouse;
            UpdateAllHelpToolTipSo(scheme);
        }
        
        public static void TriggerLookTooltip()
        {
            ShowLookTooltipEvent?.Invoke();
        }

        public static void TriggerMoveTooltip()
        {
            ShowMoveTooltipEvent?.Invoke();
        }

        public static void TriggerInputTooltip()
        {
            ShowInputTooltipEvent?.Invoke();
        }
        
        public static void TriggerSpecificTooltip(HelpToolTipControlType tooltipType)
        {
            ShowSpecificTooltipEvent?.Invoke(tooltipType);
        }

        public static void StopAllTooltips()
        {
            StopAllTooltipsEvent?.Invoke();
        }
    }

    #if UNITY_EDITOR
    [CustomEditor(typeof(HelpToolTipControls))]
    public class HelpToolTipControlsEditor : Editor 
    {
        public override void OnInspectorGUI() 
        {
            var tooltipController = (HelpToolTipControls)target;
            
            EditorGUILayout.Space();
            GUILayout.Label("Individual Tooltip Controls", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Show All Sequential", GUILayout.Height(30))) 
            {
                if (Application.isPlaying)
                    tooltipController.ShowAllTooltipsSequentially();
                else
                    Debug.LogWarning("Tooltips can only be shown during play mode.");
            }
            if(GUILayout.Button("Stop All Tooltips", GUILayout.Height(30))) 
            {
                HelpToolTipControls.StopAllTooltips();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            GUILayout.Label("Static Event Triggers", EditorStyles.boldLabel);
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Tooltip triggers only work during play mode.", MessageType.Info);
            }
            
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Trigger Look Event", GUILayout.Height(25))) 
            {
                if (Application.isPlaying)
                    HelpToolTipControls.TriggerLookTooltip();
                else
                    Debug.LogWarning("Tooltips can only be triggered during play mode.");
            }
            if(GUILayout.Button("Trigger Move Event", GUILayout.Height(25))) 
            {
                if (Application.isPlaying)
                    HelpToolTipControls.TriggerMoveTooltip();
                else
                    Debug.LogWarning("Tooltips can only be triggered during play mode.");
            }
            if(GUILayout.Button("Trigger Input Event", GUILayout.Height(25))) 
            {
                if (Application.isPlaying)
                    HelpToolTipControls.TriggerInputTooltip();
                else
                    Debug.LogWarning("Tooltips can only be triggered during play mode.");
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            DrawDefaultInspector();
        }
    }
    #endif
}