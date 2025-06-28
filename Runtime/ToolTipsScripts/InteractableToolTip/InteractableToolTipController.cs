using jeanf.scenemanagement;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipController : ToolTip
    {
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
        [SerializeField] private Zone currentZone;
        
        
        public delegate bool RequestShowToolTipDelegate(float playerDirectionDot, InteractableToolTipController interactableToolTipController);
        public static RequestShowToolTipDelegate RequestShowToolTip;
        
        public delegate void WarnHideToolTipDelegate(InteractableToolTipController interactableToolTipController);
        public static WarnHideToolTipDelegate WarnHideToolTip;
        
        private bool isPlayerInZone = false;
        
        private InteractableToolTipService _interactableToolTipService;
        
        private bool _isPlayerNear;
        private Transform _cameraTransform;
        
        private Image _image;

        private float _playerLookingDirectionDot;

        private GameObject _parent;
        private GameObject _tooltip;
        
        private InteractableToolTip _interactableToolTip;

        private bool _isToolTipDisplayed;
        public bool IsToolTipDisplayed => _isToolTipDisplayed;
        
        private bool _wasInterruptedByIpad;
        private bool _tooltipWasShowingBeforeIpad;
        private bool _ipadIsShowing = false;

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        #region Public API for Manager
        public bool IsShowingTooltip => showToolTip;
        public bool IsPermanentTooltip => isPermanentTooltip;
        
        public bool HasIncompleteTooltip()
        {
            return _wasInterruptedByIpad && _tooltipWasShowingBeforeIpad && 
                   _isPlayerNear && isPlayerInZone && CheckIfPlayerIsLooking();
        }
        
        public void CheckAndUpdateTooltipVisibility()
        {
            if (!isPermanentTooltip) return;
        }
        
        public void ResumeTooltipAfterInterruption()
        {
            if (isPermanentTooltip) return;
            if (!HasIncompleteTooltip()) return;
            
            showToolTip = true;
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }
        #endregion

        #region Start
        private void Awake()
        {
            if (interactableToolTipSettingsSo == null ||
                interactableToolTipInputSo == null ||
                interactableToolTipSettingsSo.animationSo == null ||
                inputIconSo == null)
            {
                gameObject.SetActive(false);
                return;
            }
            
            if (tooltipGameObjectPrefab == null)
            {
                Debug.LogError("tooltipGameObjectPrefab is null in InteractableToolTipController");
                gameObject.SetActive(false);
                return;
            }
               
            _cameraTransform = Camera.main?.transform;
            if (_cameraTransform == null)
            {
                Debug.LogError("Camera.main is null in InteractableToolTipController");
                gameObject.SetActive(false);
                return;
            }
            
            _parent = transform.parent?.gameObject;
            
            _tooltip = Instantiate(tooltipGameObjectPrefab, transform, false);
            if (_tooltip == null)
            {
                Debug.LogError("Failed to instantiate tooltip prefab");
                gameObject.SetActive(false);
                return;
            }
            
            _tooltip.name = interactableToolTipSettingsSo.tooltipName;
            _interactableToolTip = _tooltip.GetComponent<InteractableToolTip>();
            
            if (_interactableToolTip == null)
            {
                Debug.LogError("InteractableToolTip component not found on instantiated tooltip");
                gameObject.SetActive(false);
                return;
            }
            
            _interactableToolTip.ArrangeRotation();
            _interactableToolTip.UpdateDescription(interactableToolTipSettingsSo.description);

            var images = _tooltip.GetComponentsInChildren<Image>();
            if (images.Length > 2)
                _image = images[2];
            else if (images.Length > 0)
                _image = images[0];
            else
                _image = null;
            
            _interactableToolTipService = new InteractableToolTipService(_interactableToolTip, interactableToolTipSettingsSo);
            SetIcon();

            _tooltip.transform.localPosition = Vector3.zero;
            _tooltip.transform.localRotation = Quaternion.Euler(0,0,0);
        }

        #region Setup

        private void SetIcon()
        {
            string inputName = interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            
            Sprite sprite = inputIconSo.GetInputIcon(inputName);
            
            if(_image != null)
                _image.sprite = sprite;
            
            _tooltip.transform.localPosition = Vector3.zero;
        }
        #endregion
        
        #endregion
        
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
        }
        
        private void HandlePermanentTooltipUpdate()
        {
            bool effectiveShowToolTip = isPermanentTooltip ? _originalShowToolTipState : showToolTip;
            
            if (!effectiveShowToolTip || !isPlayerInZone) 
            { 
                HideToolTipWithoutAnimation(); 
                return; 
            }
            
            if (!_isPlayerNear)
            {
                if (_isToolTipDisplayed)
                {
                    WarnHideToolTip?.Invoke(this);
                }
                
                HideToolTip();
                return;
            }
            
            if (CheckIfPlayerIsLooking() && RequestPermissionToShowToolTip())
            {
                ShowToolTip();
            }
            else
            {
                if (_isToolTipDisplayed)
                {
                    WarnHideToolTip?.Invoke(this);
                }
                
                HideToolTip();
            }
        }
        
        private void HandlePunctualTooltipUpdate()
        {
            if (!showToolTip)
            {
                if (!_wasInterruptedByIpad && _isToolTipDisplayed)
                {
                    _wasInterruptedByIpad = true;
                    _tooltipWasShowingBeforeIpad = true;
                    _wasDisplayedBeforeInterruption = true;
                }
                
                HideToolTipWithoutAnimation();
                return;
            }
            
            if (_wasInterruptedByIpad && _tooltipWasShowingBeforeIpad)
            {
                _wasInterruptedByIpad = false;
                _tooltipWasShowingBeforeIpad = false;
                _wasDisplayedBeforeInterruption = false;
            }
            
            if (!isPlayerInZone) 
            { 
                HideToolTipWithoutAnimation(); 
                return; 
            }
            
            if (!_isPlayerNear)
            {
                if (_isToolTipDisplayed)
                {
                    WarnHideToolTip?.Invoke(this);
                }
                
                HideToolTip();
                return;
            }
            
            if (CheckIfPlayerIsLooking() && RequestPermissionToShowToolTip())
            {
                ShowToolTip();
            }
            else
            {
                if (_isToolTipDisplayed)
                {
                    WarnHideToolTip?.Invoke(this);
                }
                
                HideToolTip();
            }
        }

        private void Subscribe()
        {
            ToolTipManager.UpdateShowToolTip += UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme += UpdateControlScheme;
            ToolTipManager.DisableToolTip += DisableToolTip;
            WorldManager.PublishCurrentZoneId += CheckIfPlayerInZone;
        }
        
        private new void UpdateIsShowingToolTip(bool isShowing)
        {
            _ipadIsShowing = !isShowing;
            
            if (!isPermanentTooltip)
            {
                showToolTip = isShowing;
            }
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            _interactableToolTipService?.Destroy();
            
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                Debug.Log("Player entered tooltip trigger");
                _isPlayerNear = true;
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                Debug.Log("Player exited tooltip trigger");
                _isPlayerNear = false;
            }
        }

        public void InstanciateTooltip()
        {
            _tooltip = Instantiate(tooltipGameObjectPrefab, transform, false);
            _tooltip.name = interactableToolTipSettingsSo.tooltipName;
            _interactableToolTip = _tooltip.GetComponent<InteractableToolTip>();
            _interactableToolTip.ArrangeRotation();
            _interactableToolTip.UpdateDescription(interactableToolTipSettingsSo.description);

            _image = _tooltip.GetComponentsInChildren<Image>().Length > 1 ? _tooltip.GetComponentsInChildren<Image>()[2] : _tooltip.GetComponentsInChildren<Image>()[0];
            
            _interactableToolTipService = new InteractableToolTipService(_interactableToolTip, interactableToolTipSettingsSo);
            SetIcon();

            _tooltip.transform.localPosition = Vector3.zero;
            _tooltip.transform.localRotation = Quaternion.Euler(0,0,0);
        }

        public void DestroyInstanciateToolTip()
        {
            DestroyImmediate(_tooltip);
        }
        
        public void ShowToolTip()
        {
            if (_interactableToolTip == null) return;
            
            _isToolTipDisplayed = true;
            _interactableToolTip.ShowCloseTooltip();
            _interactableToolTipService?.ShowIcons();
            _interactableToolTip.HideFarTooltip();
        }

        public void HideToolTip()
        {
            if (_interactableToolTip == null) return;
            
            _isToolTipDisplayed = false;
            
            _interactableToolTipService?.HideIcons();
            
            if(showToolTip)
                _interactableToolTip.ShowFarTooltip();
            else
                _interactableToolTip.HideFarTooltip();
        }

        private void HideToolTipWithoutAnimation()
        {
            if (_interactableToolTip == null) return;
            
            _isToolTipDisplayed = false;
            _interactableToolTip.HideCloseTooltip();
            _interactableToolTip.HideFarTooltip();
        }
        
        private bool CheckIfPlayerIsLooking()
        {
            if (_cameraTransform is null) return false;
            
            var isLooking = false;
            
            var directionToObject = (objectToBeViewed.transform.position - _cameraTransform.position).normalized;

            _playerLookingDirectionDot = Vector3.Dot(_cameraTransform.forward, directionToObject);
            
            isLooking = _playerLookingDirectionDot > interactableToolTipSettingsSo.fieldOfViewThreshold;
            return isLooking;
        }
        
        private bool RequestPermissionToShowToolTip()
        {
            if (bypassPermissionSystem)
            {
                Debug.Log("Permission system bypassed - returning true");
                return true;
            }
            
            bool? permission = RequestShowToolTip?.Invoke(_playerLookingDirectionDot, this);
            if (permission.HasValue)
            {
                Debug.Log($"Permission system returned: {permission.Value}");
                return permission.Value;
            }
            else
            {
                Debug.Log("No permission delegate assigned - returning false");
                return false;
            }
        }
        
        private void ToolTipLookTowardsPlayer()
        {
            _interactableToolTip.LookTowardsTarget(_cameraTransform);
        }
        
        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            string inputName = interactableToolTipInputSo.GetBindingName(controlScheme);
            
            Sprite icon = inputIconSo.GetInputIcon(inputName);
            _image.sprite = icon;
        }

        private void UpdateControlScheme(bool hmdStatus)
        {
            string inputName;

            inputName = interactableToolTipInputSo.GetBindingName(hmdStatus ? BroadcastControlsStatus.ControlScheme.XR : BroadcastControlsStatus.ControlScheme.KeyboardMouse);

            if (_interactableToolTipService is null) return;
            
            var icon = inputIconSo.GetInputIcon(inputName);
            _image.sprite = icon;
        }

        private void CheckIfPlayerInZone(string zoneId)
        {
            if(currentZone == null) 
            {
                Debug.Log("currentZone is null");
                return;
            }
            
            bool wasInZone = isPlayerInZone;
            isPlayerInZone = (zoneId == currentZone.id.id);
            
            if (wasInZone != isPlayerInZone)
            {
                Debug.Log($"Zone changed. Player in zone: {isPlayerInZone}, Current zone: {zoneId}, Required zone: {currentZone.id.id}");
            }
        }
    }
}