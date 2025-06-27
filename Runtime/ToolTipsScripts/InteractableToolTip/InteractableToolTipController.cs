using jeanf.scenemanagement;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTipController : ToolTip
    {
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

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

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
               
            
            _cameraTransform = Camera.main?.transform;
            _parent = transform.parent.gameObject;
            
            //_tooltip = Instantiate(tooltipGameObjectPrefab, _parent.transform.parent, false);
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
            //_tooltip.transform.localPosition = transform.localPosition;
            //_tooltip.transform.localRotation = transform.localRotation;
            //_tooltip.transform.localScale = transform.localScale;
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
            if (!showToolTip || !isPlayerInZone) { HideToolTipWithoutAnimation(); return; }
            
            //ToolTipLookTowardsPlayer();
            
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

        private void UnSubscribe()
        {
            ToolTipManager.UpdateShowToolTip -= UpdateIsShowingToolTip;
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            ToolTipManager.DisableToolTip -= DisableToolTip;
            _interactableToolTipService?.Destroy();
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
            
            var isLooking = false;
            
            //var directionToObject = (_parent.transform.position - _cameraTransform.position).normalized;
            //var directionToObject = (transform.position - _cameraTransform.position).normalized;
            var directionToObject = (objectToBeViewed.transform.position - _cameraTransform.position).normalized;

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
            if(currentZone == null) return;
            
            isPlayerInZone = (zoneId == currentZone.id.id);
        }
    }
}