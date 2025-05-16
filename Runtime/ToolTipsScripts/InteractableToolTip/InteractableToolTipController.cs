using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipController : ToolTip
    {
        [Header("ToolTip Settings")]
        [SerializeField] private GameObject tooltipGameObjectPrefab;
        [SerializeField] private InteractableToolTipSettingsSo interactableToolTipSettingsSo;
        [SerializeField] private InputIconSo inputIconSo;
        //
        //[SerializeField] private InteractableToolTipInputSo interactableToolTipInputSo;
        
        
        public delegate bool RequestShowToolTipDelegate(float playerDirectionDot, InteractableToolTipController interactableToolTipController);
        public static RequestShowToolTipDelegate RequestShowToolTip;
        
        public delegate void WarnHideToolTipDelegate(InteractableToolTipController interactableToolTipController);
        public static WarnHideToolTipDelegate WarnHideToolTip;
        
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

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        #region Start
        private void Awake()
        {
            _cameraTransform = Camera.main?.transform;
            _parent = transform.parent.gameObject;
            
            _tooltip = Instantiate(tooltipGameObjectPrefab, _parent.transform.parent, false);
            _tooltip.name = interactableToolTipSettingsSo.tooltipName;
            _interactableToolTip = _tooltip.GetComponent<InteractableToolTip>();
            _interactableToolTip.ArrangeRotation();
            _interactableToolTip.UpdateDescription(interactableToolTipSettingsSo.description);

            _image = _tooltip.GetComponentInChildren<Image>();
            
            _interactableToolTipService = new InteractableToolTipService(_interactableToolTip, interactableToolTipSettingsSo);
            SetUpComponents();

            _tooltip.transform.position = transform.position;
        }

        #region Setup
        private void SetUpComponents()
        {
            SetUpIcon();
        }

        private void SetUpIcon()
        {
            string inputName = interactableToolTipSettingsSo.interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            
            Sprite sprite = inputIconSo.GetInputIcon(inputName);
            
            if(_image != null)
                _image.sprite = sprite;
            
            _tooltip.transform.localPosition = Vector3.zero;
        }
        #endregion
        
        #endregion
        private void Update()
        {
            if (!showToolTip) { HideToolTipWithoutAnimation(); return; }
            
            ToolTipLookTowardsPlayer();
            
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
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            _interactableToolTipService.Destroy();
        }

        private void OnTriggerEnter(Collider other)
        {
            // Maybe get a constant with the name "Player"
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                _isPlayerNear = true;
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            // Maybe get a constant with the name "Player"
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                _isPlayerNear = false;
            }
        }
        
        public void ShowToolTip()
        {
            _isToolTipDisplayed = true;
            _interactableToolTip.ShowCloseTooltip();
            _interactableToolTipService.ShowIcons();
            _interactableToolTip.HideFarTooltip();
        }

        public void HideToolTip()
        {
            _isToolTipDisplayed = false;
            
            _interactableToolTipService.HideIcons();
            
            if(showToolTip)
                _interactableToolTip.ShowFarTooltip();
            else
                _interactableToolTip.HideFarTooltip();
        }

        private void HideToolTipWithoutAnimation()
        {
            _interactableToolTip.HideCloseTooltip();
            _interactableToolTip.HideFarTooltip();
        }
        
        private bool CheckIfPlayerIsLooking()
        {
            if (_cameraTransform is null) return false;
            
            bool isLooking = false;
            
            Vector3 directionToObject = (_parent.transform.position - _cameraTransform.position).normalized;

            _playerLookingDirectionDot = Vector3.Dot(_cameraTransform.forward, directionToObject);
            
            isLooking = _playerLookingDirectionDot > interactableToolTipSettingsSo.fieldOfViewThreshold;
            return isLooking;
        }
        
        private bool RequestPermissionToShowToolTip()
        {
            bool? permission = RequestShowToolTip?.Invoke(_playerLookingDirectionDot, this);
            if (permission.HasValue)
                return permission.Value;
            else
                return false;
        }
        
        private void ToolTipLookTowardsPlayer()
        {
            _interactableToolTip.LookTowardsTarget(_cameraTransform);
        }
        
        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            string inputName = interactableToolTipSettingsSo.interactableToolTipInputSo.GetBindingName(controlScheme);
            
            Sprite icon = inputIconSo.GetInputIcon(inputName);
            _image.sprite = icon;
        }

        private void UpdateControlScheme(bool hmdStatus)
        {
            string inputName;
            
            if (hmdStatus)
            {
                inputName = interactableToolTipSettingsSo.interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.XR);
            }
            else
            {
                inputName = interactableToolTipSettingsSo.interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            }
            
            if (_interactableToolTipService is null) return;
            
            Sprite icon = inputIconSo.GetInputIcon(inputName);
            _image.sprite = icon;
            
        }
    }
}