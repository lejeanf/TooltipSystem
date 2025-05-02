using jeanf.universalplayer;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    public class InteractableToolTip : ToolTip
    {
        
        [FormerlySerializedAs("_tooltipGameObject")]
        [Header("ToolTip Settings")]
        [SerializeField] private string tooltipGameObjectName;
        [SerializeField] private GameObject tooltipGameObjectPrefab;
        [SerializeField] private string tooltipInteractionDescription;
        [SerializeField] private InteractableToolTipInputSo interactableToolTipInputSo;
        [SerializeField] private InteractableToolTipSettingsSo interactableToolTipSettingsSo;
        [SerializeField] private InputIconSo inputIconSo;
        [Tooltip("1 : Need player to look directly at the target | 0 : Accept that player doesn't look the target")]
        [SerializeField] private float fieldOfViewThreshold = 0.8f;
        
        [Header("Temporary Settings")]
        [SerializeField] private InteractableToolTipEnum interactableToolTipType;
        
        private InteractableToolTipService _interactableToolTipService;
        private InteractableTextToolTipService _interactableTextToolTipService;
        
        private string _currentText;
        
        private bool _isPlayerNear;
        private Transform _cameraTransform;
        
        private Image _image;

        private GameObject _parent;
        private GameObject _tooltip;

        private bool _isToolTipDisplayed;
        public bool IsToolTipDisplayed => _isToolTipDisplayed;

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        #region Start
        private void Awake()
        {
            _cameraTransform = Camera.main?.transform;
            _currentText = $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse)} {tooltipInteractionDescription}";

            _parent = transform.parent.gameObject;
            
            _tooltip = Instantiate(tooltipGameObjectPrefab, _parent.transform.parent, false);
            _tooltip.name = tooltipGameObjectName;

            _image = _tooltip.GetComponentInChildren<Image>();
            TMP_Text text = _tooltip.GetComponentInChildren<TMP_Text>();
            
            if(text != null)
                text.text = tooltipInteractionDescription;
            
            _interactableToolTipService = new InteractableToolTipService(_tooltip, interactableToolTipSettingsSo);
            Debug.Log($"InteractableToolTipService : {_interactableToolTipService}");
            _interactableTextToolTipService = new InteractableTextToolTipService();
            SetUpComponents();

            _tooltip.transform.position = transform.position;
        }
        
        private float GetParentObjectHeight()
        {
            var rend = _parent.GetComponent<Renderer>();
            if (rend != null)
            {
                return rend.bounds.size.y;
            }

            var objectCollider = _parent.GetComponent<Collider>();
            if (objectCollider != null)
            {
                return objectCollider.bounds.size.y;
            }

            return 0;
        }

        #region Setup
        private void SetUpComponents()
        {
            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    SetUpIcon();
                    break;
                case InteractableToolTipEnum.Text:
                    SetUpText();
                    break;
            }
        }

        private void SetUpText()
        {
            GameObject textGameObject = null;
            textGameObject = _interactableTextToolTipService.ForgeTextGameObject(_currentText, interactableToolTipSettingsSo);
            textGameObject.transform.SetParent(_tooltip.transform);
            textGameObject.transform.localPosition = Vector3.zero;
        }

        private void SetUpIcon()
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
            if (!showToolTip) { HideToolTip(); return; }
            
            if (!_isPlayerNear)
            {
                HideToolTip();
                return;
            }
                
            if (CheckIfPlayerIsLooking())
            {
                ShowToolTip();
            }
            else
            {
                HideToolTip();
            }
            
            ToolTipLookTowardsPlayer();
            
        }
        
        private void Subscribe()
        {
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme += UpdateControlScheme;
            OnUpdateIsShowingToolTip += UpdateIsShowingToolTip;
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            OnUpdateIsShowingToolTip -= UpdateIsShowingToolTip;
            _interactableToolTipService.Destroy();
            _interactableTextToolTipService.Destroy();
        }

        private void OnTriggerEnter(Collider other)
        {
            // Maybe get a constant with the name "Ignore Raycast"
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                _isPlayerNear = true;
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            // Maybe get a constant with the name "Ignore Raycast"
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                _isPlayerNear = false;
            }
        }
        
        private void ShowToolTip()
        {
            _isToolTipDisplayed = true;
            
            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    _interactableToolTipService.ShowIcons();
                    break;
                case InteractableToolTipEnum.Text:
                    _interactableTextToolTipService.ShowText();
                    break;
            }
        }

        private void HideToolTip()
        {
            _isToolTipDisplayed = false;
            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    _interactableToolTipService.HideIcons();
                    break;
                case InteractableToolTipEnum.Text:
                    _interactableTextToolTipService.HideText();
                    break;
            }
        }
        
        private bool CheckIfPlayerIsLooking()
        {
            if (_cameraTransform is null) return false;
            
            bool isLooking = false;
            
            Vector3 directionToObject = (_parent.transform.position - _cameraTransform.position).normalized;

            float dot = Vector3.Dot(_cameraTransform.forward, directionToObject);
            
            isLooking = dot > fieldOfViewThreshold;
            return isLooking;
        }
        
        private void ToolTipLookTowardsPlayer()
        {
            _tooltip.transform.forward = _cameraTransform.forward;
        }
        
        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            string inputName = interactableToolTipInputSo.GetBindingName(controlScheme);

            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    Sprite icon = inputIconSo.GetInputIcon(inputName);
                    _image.sprite = icon;
                    break;
                case InteractableToolTipEnum.Text:
                    _currentText = $"{inputName} {tooltipInteractionDescription}";
                    _interactableTextToolTipService.ChangeText(_currentText);
                    break;
            }
        }

        private void UpdateControlScheme(bool hmdStatus)
        {
            string inputName;
            
            if (hmdStatus)
            {
                inputName = interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.XR);
            }
            else
            {
                inputName = interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            }
            
            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    if (_interactableToolTipService is null) return;
                    Sprite icon = inputIconSo.GetInputIcon(inputName);
                    _image.sprite = icon;
                    break;
                case InteractableToolTipEnum.Text:
                    if(_interactableTextToolTipService is null) return;
                    _currentText = $"{inputName} {tooltipInteractionDescription}";
                    _interactableTextToolTipService.ChangeText(_currentText);
                    break;
            }
        }
    }
}