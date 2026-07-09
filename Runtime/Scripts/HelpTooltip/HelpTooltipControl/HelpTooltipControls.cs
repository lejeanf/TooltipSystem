using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using jeanf.universalplayer;
using LitMotion;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class HelpTooltipControls : Tooltip
    {
        [FormerlySerializedAs("helpToolTipControls")]
        [SerializeField] private List<HelpTooltipControlSo> helpTooltipControls;
        [SerializeField] private float helpSwitchCooldown = 1.5f;
        [SerializeField] private GameObject helpGameObject;
        [SerializeField] private GameObject successGameObject;
        [FormerlySerializedAs("helpToolTipText")]
        [SerializeField] private TMP_Text helpTooltipText;
        [FormerlySerializedAs("helpToolTipImage")]
        [SerializeField] private Image helpTooltipImage;
        [FormerlySerializedAs("helpToolTipSlider")]
        [SerializeField] private Slider helpTooltipSlider;
        [FormerlySerializedAs("helpToolTipControlIconSo")]
        [SerializeField] private HelpTooltipControlIconSo helpTooltipControlIconSo;
        [SerializeField] private BroadcastControlsStatus.ControlScheme startingControlScheme;
        [SerializeField] private InputAction inputToContinue;
        
        [Header("Tooltip Behavior")]
        [SerializeField] private bool isPermanentTooltip = true; // True for permanent markers, false for punctual help
        
        [Header("Fade Settings")]
        [SerializeField] private CanvasGroup helpCanvasGroup;
        [SerializeField] private CanvasGroup successCanvasGroup;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.2f;
        
        // Static Events
        public static event Action ShowLookTooltipEvent;
        public static event Action ShowMoveTooltipEvent;
        public static event Action ShowInputTooltipEvent;
        public static event Action<HelpTooltipControlType> ShowSpecificTooltipEvent;
        public static event Action StopAllTooltipsEvent;
        
        // Private fields
        private Transform _cameraTransform;
        private BroadcastControlsStatus.ControlScheme _currentControlScheme;
        private HelpTooltipControlSo _currentTooltip;
        private IHelpTooltipService _currentService;
        private Queue<HelpTooltipControlSo> _tooltipQueue = new Queue<HelpTooltipControlSo>();
        private bool _isSequentialMode;
        
        private bool _wasInterruptedByIpad;
        private bool _currentTooltipCompleted;
        private bool _tooltipWasShowingBeforeIpad;
        
        // Services cache
        private Dictionary<HelpTooltipControlType, IHelpTooltipService> _services = new Dictionary<HelpTooltipControlType, IHelpTooltipService>();
        private Dictionary<HelpTooltipControlType, HelpTooltipControlSo> _tooltipLookup = new Dictionary<HelpTooltipControlType, HelpTooltipControlSo>();
        
        // Cancellation tokens
        private CancellationTokenSource _tooltipCancellation;
        private CancellationTokenSource _transitionCancellation;
        private CancellationTokenSource _lifetimeCancellation;
        
        // Fade state tracking
        private bool _isCurrentlyVisible;
        private bool _isTransitioningVisibility;

        private void Awake()
        {
            _cameraTransform = Camera.main?.transform;
            _currentControlScheme = startingControlScheme;
            _lifetimeCancellation = new CancellationTokenSource();

            if (helpTooltipControlIconSo != null)
            {
                var testCombinations = new[]
                {
                    (HelpTooltipControlType.HowToLook, BroadcastControlsStatus.ControlScheme.KeyboardMouse),
                    (HelpTooltipControlType.HowToLook, BroadcastControlsStatus.ControlScheme.Gamepad),
                    (HelpTooltipControlType.HowToLook, BroadcastControlsStatus.ControlScheme.XR),
                    (HelpTooltipControlType.HowToMove, BroadcastControlsStatus.ControlScheme.KeyboardMouse),
                    (HelpTooltipControlType.HowToMove, BroadcastControlsStatus.ControlScheme.Gamepad),
                    (HelpTooltipControlType.HowToMove, BroadcastControlsStatus.ControlScheme.XR),
                    (HelpTooltipControlType.InputPressed, BroadcastControlsStatus.ControlScheme.KeyboardMouse),
                    (HelpTooltipControlType.InputPressed, BroadcastControlsStatus.ControlScheme.Gamepad),
                    (HelpTooltipControlType.InputPressed, BroadcastControlsStatus.ControlScheme.XR)
                };
        
                foreach (var (controlType, scheme) in testCombinations)
                {
                    var sprite = helpTooltipControlIconSo.GetIcon(controlType, scheme);
                }
            }
    
            InitializeServices();
            CacheTooltips();
            InitializeCanvasGroups();
    
            inputToContinue?.Enable();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            Unsubscribe();
        }

        private void Update()
        {
            // Handle different behavior for permanent vs punctual tooltips
            if (isPermanentTooltip)
            {
                HandlePermanentTooltipUpdate();
            }
            else
            {
                HandlePunctualTooltipUpdate();
            }
            
            // Common behavior for all tooltips
            HandleCommonTooltipUpdate();
        }
        
        private void HandlePermanentTooltipUpdate()
        {
            // For permanent tooltips, always show if conditions are met (unless explicitly disabled)
            // The showTooltip flag is controlled by the tooltip's internal logic, not by iPad state
            if (!showTooltip)
            {
                if (_isCurrentlyVisible)
                    HideTooltipAsync().Forget();
                return;
            }

            if (!_isCurrentlyVisible)
                ShowTooltipAsync().Forget();
        }
        
        private void HandlePunctualTooltipUpdate()
        {
            if (!showTooltip)
            {
                if (_isCurrentlyVisible) // Only hide if currently visible
                {
                    if (!_wasInterruptedByIpad && _currentTooltip != null && !_currentTooltipCompleted)
                    {
                        _wasInterruptedByIpad = true;
                        _tooltipWasShowingBeforeIpad = true;
                    }
                    HideTooltipAsync().Forget();
                }
                return;
            }

            if (_wasInterruptedByIpad && _tooltipWasShowingBeforeIpad && !_currentTooltipCompleted)
            {
                // Resume the tooltip since the action wasn't completed
                _wasInterruptedByIpad = false;
                _tooltipWasShowingBeforeIpad = false;
            }
            else if (_wasInterruptedByIpad && _currentTooltipCompleted)
            {
                // Don't resume if the action was already completed
                _wasInterruptedByIpad = false;
                _tooltipWasShowingBeforeIpad = false;
                return;
            }

            if (!_isCurrentlyVisible) // Only show if not currently visible
                ShowTooltipAsync().Forget();
        }
        
        private void HandleCommonTooltipUpdate()
        {
            // Face camera
            if (_cameraTransform != null)
                transform.forward = _cameraTransform.forward;
    
            // Handle VR skipping
            if (_currentControlScheme == BroadcastControlsStatus.ControlScheme.XR 
                && _currentTooltip != null 
                && !_currentTooltip.canBeShownInVR)
            {
                SkipCurrentTooltipAsync().Forget();
            }
        }

        #region Initialization
        private void InitializeServices()
        {
            foreach (var tooltip in helpTooltipControls)
            {
                if (_services.ContainsKey(tooltip.helpTooltipControlType)) continue;
                
                IHelpTooltipService service = tooltip.helpTooltipControlType switch
                {
                    HelpTooltipControlType.HowToLook => new HelpTooltipLookService(tooltip.progressionAdded, _cameraTransform),
                    HelpTooltipControlType.HowToMove => new HelpTooltipMoveService(tooltip.progressionAdded, _cameraTransform),
                    HelpTooltipControlType.InputPressed => CreateInputService(tooltip),
                    _ => null
                };
                
                if (service != null)
                    _services[tooltip.helpTooltipControlType] = service;
            }
        }

        private IHelpTooltipService CreateInputService(HelpTooltipControlSo tooltip)
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
            
            return new HelpTooltipInputPressedService(tooltip.progressionAdded, inputs, startingControlScheme);
        }

        private void CacheTooltips()
        {
            foreach (var tooltip in helpTooltipControls)
            {
                if (!_tooltipLookup.ContainsKey(tooltip.helpTooltipControlType))
                    _tooltipLookup[tooltip.helpTooltipControlType] = tooltip;
            }
        }

        private void InitializeCanvasGroups()
        {
            if (helpCanvasGroup == null && helpGameObject != null)
                helpCanvasGroup = helpGameObject.GetComponent<CanvasGroup>();
            
            if (successCanvasGroup == null && successGameObject != null)
                successCanvasGroup = successGameObject.GetComponent<CanvasGroup>();
            
            if (helpCanvasGroup != null)
            {
                helpCanvasGroup.alpha = 0f;
                helpCanvasGroup.interactable = false;
                helpCanvasGroup.blocksRaycasts = false;
            }
            
            if (successCanvasGroup != null)
            {
                successCanvasGroup.alpha = 0f;
                successCanvasGroup.interactable = false;
                successCanvasGroup.blocksRaycasts = false;
            }
        }
        #endregion

        #region Public API
        public void ShowSingleTooltip(HelpTooltipControlType tooltipType)
        {
            if (!ValidateTooltipRequest(tooltipType)) 
            {
                Debug.LogError("ValidateTooltipRequest failed!");
                return;
            }
    
            StopAllTooltips();
    
            _isSequentialMode = false;
    
            if (_tooltipLookup.TryGetValue(tooltipType, out var tooltip))
            {
                _currentTooltip = tooltip;
                _currentTooltipCompleted = false;
                _wasInterruptedByIpad = false;
                _tooltipWasShowingBeforeIpad = false;
            }
    
            if (_services.TryGetValue(tooltipType, out var service))
            {
                _currentService = service;
            }

            StartTooltipAsync().Forget();
        }

        public void ShowAllTooltipsSequentially()
        {
            if (!Application.isPlaying) return;
            
            StopAllTooltips();
            
            _isSequentialMode = true;
            _tooltipQueue.Clear();
            
            foreach (var tooltip in helpTooltipControls)
                _tooltipQueue.Enqueue(tooltip);
            
            if (_tooltipQueue.Count > 0)
                ShowNextTooltipAsync().Forget();
        }
        #endregion

        #region Tooltip Display
        private async UniTask StartTooltipAsync()
        {
            if (_currentTooltip == null) return;
            
            SetupUI();
            showTooltip = true;
            
            _tooltipCancellation?.Cancel();
            _tooltipCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            
            try
            {
                await RunTooltipProgressAsync(_tooltipCancellation.Token);
                OnTooltipCompleted();
            }
            catch (OperationCanceledException)
            {
                // Tooltip was cancelled, this is expected
            }
        }

        private void SetupUI()
        {
            if (helpTooltipSlider != null) helpTooltipSlider.value = 0f;
            if (helpTooltipText != null) helpTooltipText.text = _currentTooltip.helpingMessage;
            
            if (helpTooltipImage != null && _currentTooltip != null)
            {
                // Test the GetIcon method directly
                if (helpTooltipControlIconSo != null)
                {
                    var iconSprite = helpTooltipControlIconSo.GetIcon(_currentTooltip.helpTooltipControlType, _currentControlScheme);
                    
                    if (iconSprite != null)
                    {
                        helpTooltipImage.sprite = iconSprite;
                    }
                    else
                    {
                        helpTooltipImage.sprite = _currentTooltip.HelpingImage;
                    }
                }
                else
                {
                    helpTooltipImage.sprite = _currentTooltip.HelpingImage;
                }
            }
            else
            {
                if (helpTooltipImage == null) Debug.LogError("helpTooltipImage is null!");
                if (_currentTooltip == null) Debug.LogError("_currentTooltip is null!");
            }
        }

        private async UniTask RunTooltipProgressAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_currentTooltip.actionCooldown), cancellationToken: cancellationToken);
            
            while (_currentTooltip != null && helpTooltipSlider != null && helpTooltipSlider.value < 1f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (_currentService != null)
                {
                    helpTooltipSlider.value += _currentService.Activate();
                }
                
                await UniTask.Delay(TimeSpan.FromSeconds(_currentTooltip.actionCooldown), cancellationToken: cancellationToken);
            }
        }

        private void OnTooltipCompleted()
        {
            _currentTooltipCompleted = true;
            
            if (_isSequentialMode)
                StartTransitionAsync().Forget();
            else
                StartCompletionSequenceAsync().Forget();
        }

        private async UniTaskVoid StartCompletionSequenceAsync()
        {
            _transitionCancellation?.Cancel();
            _transitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            
            try
            {
                await CrossFadeToSuccessAsync(_transitionCancellation.Token);
                
                await UniTask.Delay(TimeSpan.FromSeconds(helpSwitchCooldown), cancellationToken: _transitionCancellation.Token);
                
                await FadeOutAllAsync(_transitionCancellation.Token);
                
                StopCurrentTooltip();
            }
            catch (OperationCanceledException)
            {
                // Transition was cancelled, this is expected
            }
        }

        private async UniTaskVoid StartTransitionAsync()
        {
            _transitionCancellation?.Cancel();
            _transitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            
            try
            {
                await CrossFadeToSuccessAsync(_transitionCancellation.Token);
                
                await UniTask.Delay(TimeSpan.FromSeconds(helpSwitchCooldown), cancellationToken: _transitionCancellation.Token);
                
                await ShowNextTooltipAsync();
            }
            catch (OperationCanceledException)
            {
                // Transition was cancelled, this is expected
            }
        }

        private async UniTask ShowNextTooltipAsync()
        {
            if (!_isSequentialMode || _tooltipQueue.Count == 0)
            {
                StopAllTooltips();
                return;
            }
            
            _currentTooltip = _tooltipQueue.Dequeue();
            _currentTooltipCompleted = false;
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
            
            if (_services.TryGetValue(_currentTooltip.helpTooltipControlType, out var service))
            {
                _currentService = service;
                await StartTooltipAsync();
            }
            else
            {
                await ShowNextTooltipAsync(); // Skip if no service available
            }
        }

        private async UniTaskVoid SkipCurrentTooltipAsync()
        {
            if (_isSequentialMode)
                await ShowNextTooltipAsync();
            else
                StopCurrentTooltip();
        }

        private async UniTaskVoid ShowTooltipAsync()
        {
            if (_isTransitioningVisibility) return;
            
            if (!_isCurrentlyVisible && showTooltip)
            {
                await FadeInHelpAsync(_lifetimeCancellation.Token);
            }
        }

        private async UniTaskVoid HideTooltipAsync()
        {
            if (_isTransitioningVisibility) return;
            
            if (_isCurrentlyVisible && !showTooltip)
            {
                await FadeOutAllAsync(_lifetimeCancellation.Token);
            }
        }
        #endregion

        #region Fade Effects with LitMotion
        private async UniTask FadeInHelpAsync(CancellationToken cancellationToken)
        {
            if (helpCanvasGroup == null || _isTransitioningVisibility) return;
            
            _isTransitioningVisibility = true;
            
            try
            {
                helpGameObject?.SetActive(true);
                successGameObject?.SetActive(false);
                
                if (successCanvasGroup != null)
                {
                    successCanvasGroup.alpha = 0f;
                    successCanvasGroup.interactable = false;
                    successCanvasGroup.blocksRaycasts = false;
                }
                
                await LMotion.Create(helpCanvasGroup.alpha, 1f, fadeInDuration)
                    .WithEase(Ease.OutQuad)
                    .Bind(value => helpCanvasGroup.alpha = value)
                    .ToUniTask(cancellationToken);
                
                helpCanvasGroup.interactable = true;
                helpCanvasGroup.blocksRaycasts = true;
                
                _isCurrentlyVisible = true;
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            finally
            {
                _isTransitioningVisibility = false;
            }
        }

        private async UniTask CrossFadeToSuccessAsync(CancellationToken cancellationToken)
        {
            if (successCanvasGroup == null || _isTransitioningVisibility) return;
    
            _isTransitioningVisibility = true;
    
            try
            {
                helpGameObject?.SetActive(true);
                successGameObject?.SetActive(true);
        
                successCanvasGroup.alpha = 0f;
                successCanvasGroup.interactable = false;
                successCanvasGroup.blocksRaycasts = false;
        
                if (helpCanvasGroup != null)
                {
                    helpCanvasGroup.interactable = false;
                    helpCanvasGroup.blocksRaycasts = false;
                }
        
                var crossFadeTasks = new List<UniTask>();
        
                if (helpCanvasGroup != null && helpCanvasGroup.alpha > 0)
                {
                    var helpFadeOut = LMotion.Create(helpCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(value => helpCanvasGroup.alpha = value);
            
                    crossFadeTasks.Add(helpFadeOut.ToUniTask(cancellationToken));
                }
        
                var successFadeIn = LMotion.Create(0f, 1f, fadeInDuration)
                    .WithEase(Ease.OutQuad)
                    .Bind(value => successCanvasGroup.alpha = value);
        
                crossFadeTasks.Add(successFadeIn.ToUniTask(cancellationToken));
        
                await UniTask.WhenAll(crossFadeTasks);
        
                helpGameObject?.SetActive(false);
        
                successCanvasGroup.interactable = true;
                successCanvasGroup.blocksRaycasts = true;
        
                _isCurrentlyVisible = true;
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            finally
            {
                _isTransitioningVisibility = false;
            }
        }
        

        private async UniTask FadeOutAllAsync(CancellationToken cancellationToken)
        {
            if (_isTransitioningVisibility) return;
    
            _isTransitioningVisibility = true;
    
            try
            {
                var fadeTasks = new List<UniTask>();
        
                if (helpCanvasGroup != null && helpCanvasGroup.alpha > 0)
                {
                    helpCanvasGroup.interactable = false;
                    helpCanvasGroup.blocksRaycasts = false;
            
                    var helpFadeOut = LMotion.Create(helpCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(value => helpCanvasGroup.alpha = value);
            
                    fadeTasks.Add(helpFadeOut.ToUniTask(cancellationToken));
                }
        
                if (successCanvasGroup != null && successCanvasGroup.alpha > 0)
                {
                    successCanvasGroup.interactable = false;
                    successCanvasGroup.blocksRaycasts = false;
            
                    var successFadeOut = LMotion.Create(successCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(value => successCanvasGroup.alpha = value);
            
                    fadeTasks.Add(successFadeOut.ToUniTask(cancellationToken));
                }
        
                if (fadeTasks.Count > 0)
                    await UniTask.WhenAll(fadeTasks);
        
                helpGameObject?.SetActive(false);
                successGameObject?.SetActive(false);
        
                _isCurrentlyVisible = false;
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            finally
            {
                _isTransitioningVisibility = false;
            }
        }
        #endregion

        #region Control
        private void StopCurrentTooltip()
        {
            _tooltipCancellation?.Cancel();
            _transitionCancellation?.Cancel();
            
            showTooltip = false;
            _currentTooltip = null;
            _currentService = null;
            
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }

        private void StopAllTooltips()
        {
            _isSequentialMode = false;
            _tooltipQueue.Clear();
            StopCurrentTooltip();
        }

        private bool ValidateTooltipRequest(HelpTooltipControlType tooltipType)
        {
            if (!Application.isPlaying) return false;
            if (!_tooltipLookup.ContainsKey(tooltipType) || !_services.ContainsKey(tooltipType)) return false;
            if (helpTooltipText == null || helpTooltipImage == null || helpTooltipSlider == null) return false;
            return true;
        }
        #endregion

        #region Input Handling
        private void OnContinuePressed(InputAction.CallbackContext context)
        {
            if (_isSequentialMode)
                ShowNextTooltipAsync().Forget();
            else
                StopCurrentTooltip();
        }
        #endregion

        #region Event Handling
        private void Subscribe()
        {
            TooltipControlSchemeManager.UpdateShowTooltip += UpdateIsShowingTooltip;
            TooltipControlSchemeManager.UpdateTooltipControlSchemeWithHmd += UpdateControlScheme;
            TooltipControlSchemeManager.UpdateTooltipControlScheme += UpdateControlScheme;
            TooltipControlSchemeManager.DisableTooltip += DisableTooltip;
            
            ShowLookTooltipEvent += () => ShowSingleTooltip(HelpTooltipControlType.HowToLook);
            ShowMoveTooltipEvent += () => ShowSingleTooltip(HelpTooltipControlType.HowToMove);
            ShowInputTooltipEvent += () => ShowSingleTooltip(HelpTooltipControlType.InputPressed);
            ShowSpecificTooltipEvent += ShowSingleTooltip;
            StopAllTooltipsEvent += StopAllTooltips;
            
            if (inputToContinue != null)
                inputToContinue.performed += OnContinuePressed;
        }

        private void Unsubscribe()
        {
            // Manager events
            TooltipControlSchemeManager.UpdateShowTooltip -= UpdateIsShowingTooltip;
            TooltipControlSchemeManager.UpdateTooltipControlSchemeWithHmd -= UpdateControlScheme;
            TooltipControlSchemeManager.UpdateTooltipControlScheme -= UpdateControlScheme;
            TooltipControlSchemeManager.DisableTooltip -= DisableTooltip;
            
            // Static events
            ShowLookTooltipEvent -= () => ShowSingleTooltip(HelpTooltipControlType.HowToLook);
            ShowMoveTooltipEvent -= () => ShowSingleTooltip(HelpTooltipControlType.HowToMove);
            ShowInputTooltipEvent -= () => ShowSingleTooltip(HelpTooltipControlType.InputPressed);
            ShowSpecificTooltipEvent -= ShowSingleTooltip;
            StopAllTooltipsEvent -= StopAllTooltips;
            
            // Input events
            if (inputToContinue != null)
                inputToContinue.performed -= OnContinuePressed;
                
            // Cancel all running tasks
            _tooltipCancellation?.Cancel();
            _transitionCancellation?.Cancel();
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            _currentControlScheme = controlScheme;
    
            // Safety check
            if (helpTooltipControlIconSo == null)
            {
                Debug.LogError("HelpTooltipControlIconSo is not assigned!");
                return;
            }
    
            // Update all tooltip sprites
            foreach (var tooltip in helpTooltipControls)
            {
                if (tooltip == null) continue;
        
                var newSprite = helpTooltipControlIconSo.GetIcon(tooltip.helpTooltipControlType, controlScheme);
                if (newSprite != null)
                {
                    tooltip.UpdateSprite(newSprite);
                }
                else
                {
                    Debug.LogWarning($"No sprite found for {tooltip.helpTooltipControlType} with control scheme {controlScheme}");
                }
            }
    
            // Update current tooltip display
            if (_currentTooltip != null && helpTooltipImage != null)
            {
                var newSprite = helpTooltipControlIconSo.GetIcon(_currentTooltip.helpTooltipControlType, controlScheme);
                if (newSprite != null)
                {
                    _currentTooltip.UpdateSprite(newSprite);
                    helpTooltipImage.sprite = _currentTooltip.HelpingImage;
            
                    // Force refresh the image component
                    helpTooltipImage.enabled = false;
                    helpTooltipImage.enabled = true;
                }
                else
                {
                    Debug.LogWarning($"No sprite found for current tooltip {_currentTooltip.helpTooltipControlType} with control scheme {controlScheme}");
                }
            }
    
            // Update services
            foreach (var service in _services.Values)
                service?.UpdateFromControlScheme(controlScheme);
        }

        private void UpdateControlScheme(bool hmdStatus)
        {
            var scheme = hmdStatus ? BroadcastControlsStatus.ControlScheme.XR : BroadcastControlsStatus.ControlScheme.KeyboardMouse;
            UpdateControlScheme(scheme);
        }
        #endregion

        #region Public API for Manager
        /// <summary>
        /// Check if tooltips are currently being shown
        /// </summary>
        public bool IsShowingTooltip => showTooltip;
        
        /// <summary>
        /// Check if this is a permanent tooltip (interaction marker) or punctual help
        /// </summary>
        public bool IsPermanentTooltip => isPermanentTooltip;
        
        /// <summary>
        /// Check if this tooltip controller has an incomplete tooltip that should resume after iPad interruption
        /// </summary>
        public bool HasIncompleteTooltip()
        {
            return _currentTooltip != null && !_currentTooltipCompleted && _wasInterruptedByIpad;
        }
        
        public void CheckAndUpdateTooltipVisibility()
        {
            if (!isPermanentTooltip) return;
            
        }
        
        public void ResumeTooltipAfterInterruption()
        {
            if (isPermanentTooltip) return;
            if (!HasIncompleteTooltip()) return;
            
            showTooltip = true;
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }
        #endregion

        #region Static API
        public static void TriggerLookTooltip() => ShowLookTooltipEvent?.Invoke();
        public static void TriggerMoveTooltip() => ShowMoveTooltipEvent?.Invoke();
        public static void TriggerInputTooltip() => ShowInputTooltipEvent?.Invoke();
        #endregion
    }

    #if UNITY_EDITOR
    [CustomEditor(typeof(HelpTooltipControls))]
    public class HelpTooltipControlsEditor : Editor 
    {
        public override void OnInspectorGUI() 
        {
            var tooltipController = (HelpTooltipControls)target;
            
            EditorGUILayout.Space();
            GUILayout.Label("Tooltip Controls", EditorStyles.boldLabel);
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Tooltip controls only work during play mode.", MessageType.Info);
            }
            
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Look", GUILayout.Height(25))) 
                    HelpTooltipControls.TriggerLookTooltip();
                if(GUILayout.Button("Move", GUILayout.Height(25))) 
                    HelpTooltipControls.TriggerMoveTooltip();
                if(GUILayout.Button("Input", GUILayout.Height(25))) 
                    HelpTooltipControls.TriggerInputTooltip();
                GUILayout.EndHorizontal();
            }
            
            GUILayout.Space(10);
            DrawDefaultInspector();
        }
    }
    #endif
}