using System.Collections.Generic;
using System.Linq;
using jeanf.universalplayer;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTip : ToolTip
    {
        
        [Header("ToolTip Settings")]
        [SerializeField] private InteractableToolTipInputSo interactableToolTipInputSo;
        [SerializeField] private InteractableToolTipSettingsSo interactableToolTipSettingsSo;
        [SerializeField] private InputIconSo inputIconSo;
        [Tooltip("Added Offset with the object height")]
        [SerializeField] private float addedOffsetY = 0.5f;
        [Tooltip("1 : Need player to look directly at the target | 0 : Accept that player doesn't look the target")]
        [SerializeField] private float fieldOfViewThreshold = 0.8f;
        
        [Header("Temporary Settings")]
        [SerializeField] private InteractableToolTipEnum interactableToolTipType;
        
        private InteractableIconToolTipService _interactableIconToolTipService;
        private InteractableTextToolTipService _interactableTextToolTipService;
        
        private string _currentText;
        
        private bool _isPlayerNear;
        private Transform _cameraTransform;
        private float _targetFontSize;
        private float _fontSizeWhenHidden;
        
        private GameObject _tooltipGameObject;
        private Vector3 _tooltipPosition;

        private bool _isToolTipDisplayed;
        public bool IsToolTipDisplayed => _isToolTipDisplayed;

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        #region Start
        private void Start()
        {
            _interactableIconToolTipService = new InteractableIconToolTipService();
            _interactableTextToolTipService = new InteractableTextToolTipService();
            
            _cameraTransform = Camera.main?.transform;
            _currentText = $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse)} {interactableToolTipInputSo.followingMessage}";
            
            _tooltipGameObject = new GameObject("ToolTip");
            _tooltipGameObject.transform.SetParent(transform.parent);
            _tooltipGameObject.transform.localPosition = transform.localPosition;
            
            SetUpComponents();

            _tooltipPosition = new Vector3(transform.localPosition.x, GetParentObjectHeight() + addedOffsetY, transform.localPosition.z);
            _tooltipGameObject.transform.localPosition = _tooltipPosition;
        }
        
        private float GetParentObjectHeight()
        {
            var rend = transform.GetComponent<Renderer>();
            if (rend != null)
            {
                return rend.bounds.size.y;
            }

            var objectCollider = transform.GetComponent<Collider>();
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
            textGameObject.transform.SetParent(_tooltipGameObject.transform);
            textGameObject.transform.localPosition = Vector3.zero;
        }

        private void SetUpIcon()
        {
            string inputName = interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            
            List<Sprite> sprites = inputIconSo.GetInputIcons(inputName);

            GameObject iconGameObject = null;
            
            if (sprites.Count > 1)
                iconGameObject = _interactableIconToolTipService.ForgeIconGameObject(sprites[0], sprites[1], interactableToolTipSettingsSo);
            else if (sprites.Count == 1)
                iconGameObject = _interactableIconToolTipService.ForgeIconGameObject(sprites.First(), interactableToolTipSettingsSo);

            //Maybe add a Debug.LogWarning
            if (iconGameObject is null) return;
            
            iconGameObject.transform.SetParent(_tooltipGameObject.transform);
            iconGameObject.transform.localPosition = Vector3.zero;
        }
        #endregion
        
        #endregion
        private void Update()
        {
            if (!showToolTip) return;
            
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
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateControlScheme;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateControlScheme;
            _interactableIconToolTipService.Destroy();
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
                    _interactableIconToolTipService.ShowIcons();
                    break;
                case InteractableToolTipEnum.Text:
                    _interactableTextToolTipService.ShowText();
                    break;
            }
            
            _tooltipGameObject.transform.localPosition = _tooltipPosition;
        }

        private void HideToolTip()
        {
            _isToolTipDisplayed = false;
            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    _interactableIconToolTipService.HideIcons();
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
            
            Vector3 directionToObject = (transform.position - _cameraTransform.position).normalized;

            float dot = Vector3.Dot(_cameraTransform.forward, directionToObject);
            
            isLooking = dot > fieldOfViewThreshold;
            return isLooking;
        }
        
        private void ToolTipLookTowardsPlayer()
        {
            _tooltipGameObject.transform.forward = _cameraTransform.forward;
        }
        
        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            string inputName = interactableToolTipInputSo.GetBindingName(controlScheme);

            switch (interactableToolTipType)
            {
                case InteractableToolTipEnum.Icon:
                    List<Sprite> icons = inputIconSo.GetInputIcons(inputName);
                    if(icons.Count > 1)
                        _interactableIconToolTipService.ChangeSprite(icons[0], icons[1]);
                    else if (icons.Count == 1)
                        _interactableIconToolTipService.ChangeSprite(icons.First());
                    break;
                case InteractableToolTipEnum.Text:
                    _currentText = $"{inputName} {interactableToolTipInputSo.followingMessage}";
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
                    if (_interactableIconToolTipService is null) return;
                    List<Sprite> icons = inputIconSo.GetInputIcons(inputName);
                    if(icons.Count > 1)
                        _interactableIconToolTipService.ChangeSprite(icons[0], icons[1]);
                    else if (icons.Count == 1)
                        _interactableIconToolTipService.ChangeSprite(icons.First());
                    break;
                case InteractableToolTipEnum.Text:
                    if(_interactableTextToolTipService is null) return;
                    _currentText = $"{inputName} {interactableToolTipInputSo.followingMessage}";
                    _interactableTextToolTipService.ChangeText(_currentText);
                    break;
            }
        }
    }
}