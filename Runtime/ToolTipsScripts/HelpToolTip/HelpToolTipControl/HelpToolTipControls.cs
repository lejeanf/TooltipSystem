using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using jeanf.universalplayer;
using LitMotion;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class HelpToolTipControls : ToolTip
    {
        [SerializeField] private List<HelpToolTipControlSo> helpToolTipControls;
        [SerializeField] private float helpSwitchCooldown = 1.5f;
        [SerializeField] private GameObject helpGameObject;
        [SerializeField] private GameObject successGameObject;
        [SerializeField] private TMP_Text helpToolTipText;
        [SerializeField] private Image helpToolTipImage;
        [SerializeField] private Slider helpToolTipSlider;
        [SerializeField] private HelpToolTipControlIconSo helpToolTipControlIconSo;
        [SerializeField] private BroadcastControlsStatus.ControlScheme startingControlScheme;
        [SerializeField] private InputAction inputToContinue;
        
        [Header("Fade Settings")]
        [SerializeField] private CanvasGroup helpCanvasGroup;
        [SerializeField] private CanvasGroup successCanvasGroup;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.2f;
        
        // Static Events
        public static event Action ShowLookTooltipEvent;
        public static event Action ShowMoveTooltipEvent;
        public static event Action ShowInputTooltipEvent;
        public static event Action<HelpToolTipControlType> ShowSpecificTooltipEvent;
        public static event Action StopAllTooltipsEvent;
        
        // Private fields
        private Transform _cameraTransform;
        private BroadcastControlsStatus.ControlScheme _currentControlScheme;
        private HelpToolTipControlSo _currentTooltip;
        private IHelpToolTipService _currentService;
        private Queue<HelpToolTipControlSo> _tooltipQueue = new Queue<HelpToolTipControlSo>();
        private bool _isSequentialMode;
        
        // Services cache
        private Dictionary<HelpToolTipControlType, IHelpToolTipService> _services = new Dictionary<HelpToolTipControlType, IHelpToolTipService>();
        private Dictionary<HelpToolTipControlType, HelpToolTipControlSo> _tooltipLookup = new Dictionary<HelpToolTipControlType, HelpToolTipControlSo>();
        
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
            if (!showToolTip)
            {
                HideTooltipAsync().Forget();
                return;
            }

            ShowTooltipAsync().Forget();
            
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
            foreach (var tooltip in helpToolTipControls)
            {
                if (_services.ContainsKey(tooltip.helpToolTipControlType)) continue;
                
                IHelpToolTipService service = tooltip.helpToolTipControlType switch
                {
                    HelpToolTipControlType.HowToLook => new HelpToolTipLookService(tooltip.progressionAdded, _cameraTransform),
                    HelpToolTipControlType.HowToMove => new HelpToolTipMoveService(tooltip.progressionAdded, _cameraTransform),
                    HelpToolTipControlType.InputPressed => CreateInputService(tooltip),
                    _ => null
                };
                
                if (service != null)
                    _services[tooltip.helpToolTipControlType] = service;
            }
        }

        private IHelpToolTipService CreateInputService(HelpToolTipControlSo tooltip)
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
            
            return new HelpToolTipInputPressedService(tooltip.progressionAdded, inputs, startingControlScheme);
        }

        private void CacheTooltips()
        {
            foreach (var tooltip in helpToolTipControls)
            {
                if (!_tooltipLookup.ContainsKey(tooltip.helpToolTipControlType))
                    _tooltipLookup[tooltip.helpToolTipControlType] = tooltip;
            }
        }

        private void InitializeCanvasGroups()
        {
            // Auto-find CanvasGroups if not assigned
            if (helpCanvasGroup == null && helpGameObject != null)
                helpCanvasGroup = helpGameObject.GetComponent<CanvasGroup>();
            
            if (successCanvasGroup == null && successGameObject != null)
                successCanvasGroup = successGameObject.GetComponent<CanvasGroup>();
            
            // Initialize alpha values
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
        public void ShowSingleTooltip(HelpToolTipControlType tooltipType)
        {
            if (!ValidateTooltipRequest(tooltipType)) return;
            
            StopAllTooltips();
            
            _isSequentialMode = false;
            _currentTooltip = _tooltipLookup[tooltipType];
            _currentService = _services[tooltipType];
            
            StartTooltipAsync().Forget();
        }

        public void ShowAllTooltipsSequentially()
        {
            if (!Application.isPlaying) return;
            
            StopAllTooltips();
            
            _isSequentialMode = true;
            _tooltipQueue.Clear();
            
            foreach (var tooltip in helpToolTipControls)
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
            showToolTip = true;
            
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
            if (helpToolTipSlider != null) helpToolTipSlider.value = 0f;
            if (helpToolTipText != null) helpToolTipText.text = _currentTooltip.helpingMessage;
            if (helpToolTipImage != null) helpToolTipImage.sprite = _currentTooltip.HelpingImage;
        }

        private async UniTask RunTooltipProgressAsync(CancellationToken cancellationToken)
        {
            // Wait for initial cooldown
            await UniTask.Delay(TimeSpan.FromSeconds(_currentTooltip.actionCooldown), cancellationToken: cancellationToken);
            
            // Progress the tooltip
            while (_currentTooltip != null && helpToolTipSlider != null && helpToolTipSlider.value < 1f)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (_currentService != null)
                {
                    helpToolTipSlider.value += _currentService.Activate();
                }
                
                await UniTask.Delay(TimeSpan.FromSeconds(_currentTooltip.actionCooldown), cancellationToken: cancellationToken);
            }
        }

        private void OnTooltipCompleted()
        {
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
                // Cross-fade to success state
                await CrossFadeToSuccessAsync(_transitionCancellation.Token);
                
                // Wait for success display duration
                await UniTask.Delay(TimeSpan.FromSeconds(helpSwitchCooldown), cancellationToken: _transitionCancellation.Token);
                
                // Fade out completely
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
                // Cross-fade to success state
                await CrossFadeToSuccessAsync(_transitionCancellation.Token);
                
                // Wait for transition cooldown
                await UniTask.Delay(TimeSpan.FromSeconds(helpSwitchCooldown), cancellationToken: _transitionCancellation.Token);
                
                // Continue to next tooltip
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
            
            if (_services.TryGetValue(_currentTooltip.helpToolTipControlType, out var service))
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
            
            // Only fade in if we're not currently visible and should be showing
            if (!_isCurrentlyVisible && showToolTip)
            {
                await FadeInHelpAsync(_lifetimeCancellation.Token);
            }
        }

        private async UniTaskVoid HideTooltipAsync()
        {
            if (_isTransitioningVisibility) return;
            
            // Only fade out if we're currently visible and shouldn't be showing
            if (_isCurrentlyVisible && !showToolTip)
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
                // Ensure GameObjects are active before fading
                helpGameObject?.SetActive(true);
                successGameObject?.SetActive(false);
                
                // Make sure success is hidden
                if (successCanvasGroup != null)
                {
                    successCanvasGroup.alpha = 0f;
                    successCanvasGroup.interactable = false;
                    successCanvasGroup.blocksRaycasts = false;
                }
                
                // Fade in help using LitMotion
                await LMotion.Create(helpCanvasGroup.alpha, 1f, fadeInDuration)
                    .WithEase(Ease.OutQuad)
                    .Bind(alpha => helpCanvasGroup.alpha = alpha)
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
                // Ensure both GameObjects are active for cross-fade
                helpGameObject?.SetActive(true);
                successGameObject?.SetActive(true);
                
                // Prepare success canvas group
                successCanvasGroup.alpha = 0f;
                successCanvasGroup.interactable = false;
                successCanvasGroup.blocksRaycasts = false;
                
                // Disable help interactions immediately
                if (helpCanvasGroup != null)
                {
                    helpCanvasGroup.interactable = false;
                    helpCanvasGroup.blocksRaycasts = false;
                }
                
                // Create cross-fade animation using LitMotion
                var crossFadeTasks = new List<UniTask>();
                
                // Fade out help (if visible)
                if (helpCanvasGroup != null && helpCanvasGroup.alpha > 0)
                {
                    var helpFadeOut = LMotion.Create(helpCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(alpha => helpCanvasGroup.alpha = alpha);
                    
                    crossFadeTasks.Add(helpFadeOut.ToUniTask(cancellationToken));
                }
                
                // Fade in success
                var successFadeIn = LMotion.Create(0f, 1f, fadeInDuration)
                    .WithEase(Ease.OutQuad)
                    .Bind(alpha => helpCanvasGroup.alpha = alpha);
                
                crossFadeTasks.Add(successFadeIn.ToUniTask(cancellationToken));
                
                // Wait for all animations to complete
                await UniTask.WhenAll(crossFadeTasks);
                
                // Clean up after animation
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

        private async UniTask FadeToSuccessAsync(CancellationToken cancellationToken)
        {
            if (successCanvasGroup == null || _isTransitioningVisibility) return;
            
            _isTransitioningVisibility = true;
            
            try
            {
                // Ensure GameObjects are active
                helpGameObject?.SetActive(false);
                successGameObject?.SetActive(true);
                
                // Fade out help if it's visible
                if (helpCanvasGroup != null && helpCanvasGroup.alpha > 0)
                {
                    helpCanvasGroup.interactable = false;
                    helpCanvasGroup.blocksRaycasts = false;
                    
                    await LMotion.Create(helpCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(alpha => helpCanvasGroup.alpha = alpha)
                        .ToUniTask(cancellationToken);
                }
                
                // Fade in success
                await LMotion.Create(successCanvasGroup.alpha, 1f, fadeInDuration)
                    .WithEase(Ease.OutQuad)
                    .Bind(alpha => helpCanvasGroup.alpha = alpha)
                    .ToUniTask(cancellationToken);
                
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
                        .Bind(alpha => helpCanvasGroup.alpha = alpha);
                    
                    fadeTasks.Add(helpFadeOut.ToUniTask(cancellationToken));
                }
                
                if (successCanvasGroup != null && successCanvasGroup.alpha > 0)
                {
                    successCanvasGroup.interactable = false;
                    successCanvasGroup.blocksRaycasts = false;
                    
                    var successFadeOut = LMotion.Create(successCanvasGroup.alpha, 0f, fadeOutDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(alpha => helpCanvasGroup.alpha = alpha);
                    
                    fadeTasks.Add(successFadeOut.ToUniTask(cancellationToken));
                }
                
                if (fadeTasks.Count > 0)
                    await UniTask.WhenAll(fadeTasks);
                
                // Deactivate GameObjects after fade
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
            
            showToolTip = false;
            _currentTooltip = null;
            _currentService = null;
        }

        private void StopAllTooltips()
        {
            _isSequentialMode = false;
            _tooltipQueue.Clear();
            StopCurrentTooltip();
        }

        private bool ValidateTooltipRequest(HelpToolTipControlType tooltipType)
        {
            if (!Application.isPlaying) return false;
            if (!_tooltipLookup.ContainsKey(tooltipType) || !_services.ContainsKey(tooltipType)) return false;
            if (helpToolTipText == null || helpToolTipImage == null || helpToolTipSlider == null) return false;
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
            ToolTipManager.UpdateShowToolTip += UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme += UpdateControlScheme;
            ToolTipManager.DisableToolTip += DisableToolTip;
            
            ShowLookTooltipEvent += () => ShowSingleTooltip(HelpToolTipControlType.HowToLook);
            ShowMoveTooltipEvent += () => ShowSingleTooltip(HelpToolTipControlType.HowToMove);
            ShowInputTooltipEvent += () => ShowSingleTooltip(HelpToolTipControlType.InputPressed);
            ShowSpecificTooltipEvent += ShowSingleTooltip;
            StopAllTooltipsEvent += StopAllTooltips;
            
            if (inputToContinue != null)
                inputToContinue.performed += OnContinuePressed;
        }

        private void Unsubscribe()
        {
            // Manager events
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            
            // Static events
            ShowLookTooltipEvent -= () => ShowSingleTooltip(HelpToolTipControlType.HowToLook);
            ShowMoveTooltipEvent -= () => ShowSingleTooltip(HelpToolTipControlType.HowToMove);
            ShowInputTooltipEvent -= () => ShowSingleTooltip(HelpToolTipControlType.InputPressed);
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
            
            // Update all tooltip sprites
            foreach (var tooltip in helpToolTipControls)
            {
                var newSprite = helpToolTipControlIconSo.GetIcon(tooltip.helpToolTipControlType, controlScheme);
                tooltip.UpdateSprite(newSprite);
            }
            
            // Update current tooltip display
            if (_currentTooltip != null && helpToolTipImage != null)
            {
                var newSprite = helpToolTipControlIconSo.GetIcon(_currentTooltip.helpToolTipControlType, controlScheme);
                _currentTooltip.UpdateSprite(newSprite);
                helpToolTipImage.sprite = _currentTooltip.HelpingImage;
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

        #region Static API
        public static void TriggerLookTooltip() => ShowLookTooltipEvent?.Invoke();
        public static void TriggerMoveTooltip() => ShowMoveTooltipEvent?.Invoke();
        public static void TriggerInputTooltip() => ShowInputTooltipEvent?.Invoke();
        #endregion
    }

    #if UNITY_EDITOR
    [CustomEditor(typeof(HelpToolTipControls))]
    public class HelpToolTipControlsEditor : Editor 
    {
        public override void OnInspectorGUI() 
        {
            var tooltipController = (HelpToolTipControls)target;
            
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
                    HelpToolTipControls.TriggerLookTooltip();
                if(GUILayout.Button("Move", GUILayout.Height(25))) 
                    HelpToolTipControls.TriggerMoveTooltip();
                if(GUILayout.Button("Input", GUILayout.Height(25))) 
                    HelpToolTipControls.TriggerInputTooltip();
                GUILayout.EndHorizontal();
            }
            
            GUILayout.Space(10);
            DrawDefaultInspector();
        }
    }
    #endif
}