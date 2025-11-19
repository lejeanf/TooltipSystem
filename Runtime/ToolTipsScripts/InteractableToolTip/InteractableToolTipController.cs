using jeanf.scenemanagement;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipController : ToolTip
    {
        [Header("Tooltip debug")]
        public bool isDebug = false;
        
        [Header("Tooltip Behavior")]
        [SerializeField] private bool isPermanentTooltip = true;
        
        [Header("Debug")]
        [SerializeField] private bool bypassPermissionSystem = false;
        
        [Header("ToolTip Settings")]
        [SerializeField] private GameObject tooltipGameObjectPrefab;
        [SerializeField] private GameObject objectToBeViewed;
        [SerializeField] private InteractableToolTipSettingsSo interactableToolTipSettingsSo;
        [SerializeField] private InputIconSo inputIconSo;
        [SerializeField] private InteractableToolTipInputSo interactableToolTipInputSo;
        public Zone currentZone;
        
        //Delegates
        public delegate bool RequestShowToolTipDelegate(float playerDirectionDot, InteractableToolTipController interactableToolTipController);
        public static RequestShowToolTipDelegate RequestShowToolTip;
        
        public delegate void WarnHideToolTipDelegate(InteractableToolTipController interactableToolTipController);
        public static WarnHideToolTipDelegate WarnHideToolTip;
        
        //States
        private bool isPlayerInZone = false;
        private bool _isPlayerNear;
        private bool _isToolTipDisplayed;
        private bool _wasInterruptedByIpad;
        private bool _tooltipWasShowingBeforeIpad;
        private bool _ipadIsShowing = false;
        
        //Components and services
        private InteractableToolTipService _interactableToolTipService;
        private Transform _cameraTransform;
        private Image _image;
        private GameObject _tooltip;
        private InteractableToolTip _interactableToolTip;
        
        //Calculated values
        private float _playerLookingDirectionDot;
        
        //Layer mask in cache to avoid repeated calls
        private int _playerLayerMask;

        //Public properties
        public bool IsToolTipDisplayed => _isToolTipDisplayed;
        public bool IsShowingTooltip => showToolTip;
        public bool IsPermanentTooltip => isPermanentTooltip;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _playerLayerMask = LayerMask.NameToLayer("Player");
            _cameraTransform = Camera.main.transform;
            
            if (!ValidateComponents())
            {
                gameObject.SetActive(false);
                return;
            }
            
            InitializeTooltip();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();
        
        private void Update()
        {
            if (isPermanentTooltip)
                HandlePermanentTooltipUpdate();
            else
                HandlePunctualTooltipUpdate();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == _playerLayerMask)
                _isPlayerNear = true;
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == _playerLayerMask)
                _isPlayerNear = false;
        }
        
        #endregion

        #region Initialization
        
        private bool ValidateComponents()
        {
            return interactableToolTipSettingsSo != null &&
                   interactableToolTipInputSo != null &&
                   interactableToolTipSettingsSo.animationSo != null &&
                   inputIconSo != null &&
                   tooltipGameObjectPrefab != null;
        }
        
        private void InitializeTooltip()
        {
            _tooltip = Instantiate(tooltipGameObjectPrefab, transform, false);
            if (_tooltip == null)
            {
                gameObject.SetActive(false);
                return;
            }
            
            _tooltip.name = interactableToolTipSettingsSo.tooltipName;
            _interactableToolTip = _tooltip.GetComponent<InteractableToolTip>();
            
            if (_interactableToolTip == null)
            {
                gameObject.SetActive(false);
                return;
            }
            
            _interactableToolTip.ArrangeRotation();
            _interactableToolTip.UpdateDescription(interactableToolTipSettingsSo.description);
            
            CacheImageComponent();
            
            _interactableToolTipService = new InteractableToolTipService(_interactableToolTip, interactableToolTipSettingsSo);
            SetIcon();
            
            ResetTooltipTransform();
        }

        private void CacheImageComponent()
        {
            var images = _tooltip.GetComponentsInChildren<Image>();
            _image = images.Length > 2 ? images[2] : (images.Length > 0 ? images[0] : null);
        }

        private void ResetTooltipTransform()
        {
            _tooltip.transform.localPosition = Vector3.zero;
            _tooltip.transform.localRotation = Quaternion.identity;
        }

        private void SetIcon()
        {
            string inputName = interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            Sprite sprite = inputIconSo.GetInputIcon(inputName);
            
            if (_image != null)
                _image.sprite = sprite;
        }
        
        #endregion
        
        #region Public API for Manager
        
        public bool HasIncompleteTooltip()
        {
            return _wasInterruptedByIpad && 
                   _tooltipWasShowingBeforeIpad && 
                   _isPlayerNear && 
                   isPlayerInZone && 
                   CheckIfPlayerIsLooking();
        }
        
        public void CheckAndUpdateTooltipVisibility()
        {
            //Method for future extension if necessary
        }
        
        public void ResumeTooltipAfterInterruption()
        {
            if (isPermanentTooltip || !HasIncompleteTooltip()) 
                return;
            
            showToolTip = true;
            ResetInterruptionState();
        }
        
        public void NotifyIpadHidden()
        {
            if (isDebug) 
                Debug.Log($"[InteractableToolTipController] NotifyIpadHidden: isPermanentTooltip = {isPermanentTooltip}");
            
            if (isPermanentTooltip)
                _ipadIsShowing = false;
        }
        
        #endregion

        #region Tooltip Update Logic
        
        private void HandlePermanentTooltipUpdate()
        {
            if (_ipadIsShowing || !isPlayerInZone) 
            { 
                HideToolTipWithoutAnimation(); 
                return; 
            }
            
            if (!_isPlayerNear)
            {
                NotifyAndHideToolTip();
                return;
            }
            
            UpdateTooltipVisibility();
        }
        
        private void HandlePunctualTooltipUpdate()
        {
            if (!showToolTip)
            {
                HandleInterruptionByIpad();
                HideToolTipWithoutAnimation();
                return;
            }
            
            ResetInterruptionStateIfNeeded();
            
            if (!isPlayerInZone) 
            { 
                HideToolTipWithoutAnimation(); 
                return; 
            }
            
            if (!_isPlayerNear)
            {
                NotifyAndHideToolTip();
                return;
            }
            
            UpdateTooltipVisibility();
        }

        private void UpdateTooltipVisibility()
        {
            bool isLooking = CheckIfPlayerIsLooking();
            bool hasPermission = RequestPermissionToShowToolTip();
            
            if (isLooking && hasPermission)
                ShowToolTip();
            else
                NotifyAndHideToolTip();
        }

        private void NotifyAndHideToolTip()
        {
            if (_isToolTipDisplayed)
                WarnHideToolTip?.Invoke(this);
            
            HideToolTip();
        }

        private void HandleInterruptionByIpad()
        {
            if (!_wasInterruptedByIpad && _isToolTipDisplayed)
            {
                _wasInterruptedByIpad = true;
                _tooltipWasShowingBeforeIpad = true;
            }
        }

        private void ResetInterruptionStateIfNeeded()
        {
            if (_wasInterruptedByIpad && _tooltipWasShowingBeforeIpad)
                ResetInterruptionState();
        }

        private void ResetInterruptionState()
        {
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }
        
        #endregion

        #region Tooltip Display Methods
        
        public void ShowToolTip()
        {
            if (_interactableToolTip == null) return;
            
            if (isDebug) 
                Debug.Log("[InteractableToolTipController] ShowToolTip", this);
            
            _isToolTipDisplayed = true;
            _interactableToolTip.ShowCloseTooltip();
            _interactableToolTipService?.ShowIcons();
            _interactableToolTip.HideFarTooltip();
        }

        public void HideToolTip()
        {
            if (_interactableToolTip == null) return;
            
            if (isDebug) 
                Debug.Log("[InteractableToolTipController] HideToolTip", this);
            
            _isToolTipDisplayed = false;
            _interactableToolTipService?.HideIcons();
            
            if (showToolTip)
                _interactableToolTip.ShowFarTooltip();
            else
                _interactableToolTip.HideFarTooltip();
        }

        private void HideToolTipWithoutAnimation()
        {
            if (_interactableToolTip == null) return;
            
            if (isDebug) 
                Debug.Log("[InteractableToolTipController] HideToolTipWithoutAnimation", this);
            
            _isToolTipDisplayed = false;
            _interactableToolTip.HideCloseTooltip();
            _interactableToolTip.HideFarTooltip();
        }
        
        #endregion

        #region Player Detection
        
        private bool CheckIfPlayerIsLooking()
        {
            if (_cameraTransform == null) return false;
            
            Vector3 directionToObject = (objectToBeViewed.transform.position - _cameraTransform.position).normalized;
            _playerLookingDirectionDot = Vector3.Dot(_cameraTransform.forward, directionToObject);
            
            return _playerLookingDirectionDot > interactableToolTipSettingsSo.fieldOfViewThreshold;
        }
        
        private bool RequestPermissionToShowToolTip()
        {
            if (bypassPermissionSystem)
                return true;
            
            bool? permission = RequestShowToolTip?.Invoke(_playerLookingDirectionDot, this);
            return permission ?? false;
        }
        
        #endregion

        #region Event Subscription
        
        private void Subscribe()
        {
            ToolTipManager.UpdateShowToolTip += UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateControlSchemeWithHmd;
            ToolTipManager.UpdateToolTipControlScheme += UpdateControlScheme;
            ToolTipManager.DisableToolTip += DisableToolTip;
            WorldManager.PublishCurrentZoneId += CheckIfPlayerInZone;
        }
        
        private void UnSubscribe()
        {
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlSchemeWithHmd;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            WorldManager.PublishCurrentZoneId -= CheckIfPlayerInZone;
            
            _interactableToolTipService?.Destroy();
            ResetInterruptionState();
        }

        private new void UpdateIsShowingToolTip(bool isShowing)
        {
            bool wasIpadShowing = _ipadIsShowing;
            _ipadIsShowing = !isShowing;
            
            if (isPermanentTooltip)
            {
                if (_ipadIsShowing && !wasIpadShowing)
                    HideToolTipWithoutAnimation();
            }
            else
            {
                showToolTip = isShowing;
            }
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            if (_image == null) return;
            
            string inputName = interactableToolTipInputSo.GetBindingName(controlScheme);
            _image.sprite = inputIconSo.GetInputIcon(inputName);
        }

        private void UpdateControlSchemeWithHmd(bool hmdStatus)
        {
            if (_interactableToolTipService == null || _image == null) return;
            
            var controlScheme = hmdStatus ? 
                BroadcastControlsStatus.ControlScheme.XR : 
                BroadcastControlsStatus.ControlScheme.KeyboardMouse;
            
            string inputName = interactableToolTipInputSo.GetBindingName(controlScheme);
            _image.sprite = inputIconSo.GetInputIcon(inputName);
        }

        private void CheckIfPlayerInZone(string zoneId)
        {
            if (currentZone == null) return;
            
            isPlayerInZone = (zoneId == currentZone.id.id);
        }
        
        #endregion

        #region Editor/Debug Methods
        
        public void InstanciateTooltip()
        {
            if (isDebug) 
                Debug.Log("[InteractableToolTipController] InstanciateTooltip", this);
            
            InitializeTooltip();
        }

        public void DestroyInstanciateToolTip()
        {
            if (_tooltip != null)
                DestroyImmediate(_tooltip);
        }
        
        #endregion
    }
}