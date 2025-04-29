using System.Collections.Generic;
using System.Linq;
using jeanf.universalplayer;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        [SerializeField] private HelpToolTipControlIconSo helpToolTipControlIconSo;
        [SerializeField] private BroadcastControlsStatus.ControlScheme startingControlScheme;
        [Header("For testing")]
        [SerializeField] private InputAction inputToContinue;
        
        
        private string _currentText;
        private Transform _cameraTransform;
        
        private BroadcastControlsStatus.ControlScheme _currentControlScheme;
        
        private TMP_Text _helpToolTipText;
        private Image _helpToolTipImage;
        private Slider _helpToolTipSlider;
        
        private ToolTipTimer _toolTipServiceTimerCooldown;
        private ToolTipTimer _toolTipSucessTimerCooldown;
        private HelpToolTipControlSo _activeHelpToolTipControl;
        
        private IHelpToolTipService _currentHelpToolTipService;
        private HelpToolTipLookService _helpToolTipLookService;
        private HelpToolTipMoveService _helpToolTipMoveService;
        private HelpToolTipInputPressedService _helpToolTipInputPressedService;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();
        
        private void Awake()
        {
            _cameraTransform = Camera.main?.transform;
            
            _helpToolTipText = GetComponentInChildren<TMP_Text>();
            _helpToolTipImage = GetComponentInChildren<Image>();
            _helpToolTipSlider = GetComponentInChildren<Slider>();
            
            _toolTipServiceTimerCooldown = new ToolTipTimer();
            _toolTipSucessTimerCooldown = new ToolTipTimer();
            
            _currentControlScheme = startingControlScheme;
            
            SetUpAllServices();
            
            _activeHelpToolTipControl = helpToolTipControls.First();
            helpToolTipControls.RemoveAt(0);
            
            UpdateHelpToolTipCurrentService();
            
            UpdateAllHelpToolTipSo(startingControlScheme);
            
            _helpToolTipSlider.value = 0f;
            
            _currentText = _activeHelpToolTipControl.helpingMessage;
            _helpToolTipText.text = _currentText;
            _helpToolTipImage.sprite = _activeHelpToolTipControl.HelpingImage;
            
            _toolTipServiceTimerCooldown.StartTimer(_activeHelpToolTipControl.actionCooldown, ActivateHelpToolTipService);
            
            inputToContinue.Enable();
        }

        private void SetUpAllServices()
        {
            for (int i = 0; i < helpToolTipControls.Count; i++)
            {
                switch (helpToolTipControls[i].helpToolTipControlType)
                {
                    case HelpToolTipControlType.HowToLook:
                        _helpToolTipLookService ??= new HelpToolTipLookService(helpToolTipControls[i].progressionAdded, _cameraTransform);
                        break;
                    case HelpToolTipControlType.HowToMove:
                        _helpToolTipMoveService ??= new HelpToolTipMoveService(helpToolTipControls[i].progressionAdded, _cameraTransform);
                        break;
                    case HelpToolTipControlType.InputPressed:
                        
                        helpToolTipControls[i].actionRequiredWhenKeyBoard.action?.Enable();
                        helpToolTipControls[i].actionRequiredWhenGamepad.action?.Enable();
                        helpToolTipControls[i].actionRequiredWhenXr.action?.Enable();
                        
                        Dictionary<BroadcastControlsStatus.ControlScheme, InputActionReference> inputs = new Dictionary<BroadcastControlsStatus.ControlScheme, InputActionReference>
                        {
                            {
                                BroadcastControlsStatus.ControlScheme.KeyboardMouse,
                                helpToolTipControls[i].actionRequiredWhenKeyBoard
                            },
                            {
                                BroadcastControlsStatus.ControlScheme.Gamepad,
                                helpToolTipControls[i].actionRequiredWhenGamepad
                            },
                            {
                                BroadcastControlsStatus.ControlScheme.XR, 
                                helpToolTipControls[i].actionRequiredWhenXr
                            }
                        };
                        _helpToolTipInputPressedService ??= new HelpToolTipInputPressedService(helpToolTipControls[i].progressionAdded, inputs, startingControlScheme);
                        break;
                }
            }
        }

        private void Update()
        {
            if(!showToolTip) { HideHelpToolTip(); return; }
            
            //For testing
            if (inputToContinue.WasPressedThisFrame())
            {
                SetUpNextHelpToolTip();
                return;
            }
            //
            
            ShowHelpToolTip();

            if (Mathf.Approximately(_helpToolTipSlider.value, 1))
                SetUpNextHelpToolTip();
            
            transform.forward = _cameraTransform.forward;
        }

        private void ShowHelpToolTip()
        {
            if (_toolTipSucessTimerCooldown.IsTimerRunning)
            {
                helpGameObject.SetActive(false);
                successGameObject.SetActive(true);
            }
            else
            {
                helpGameObject.SetActive(true);
                successGameObject.SetActive(false);
            }
        }

        private void HideHelpToolTip()
        {
            helpGameObject.SetActive(false);
            successGameObject.SetActive(false);
        }

        private void UpdateHelpToolTipCurrentService()
        {
            switch (_activeHelpToolTipControl.helpToolTipControlType)
            {
                case HelpToolTipControlType.HowToLook:
                    _currentHelpToolTipService = _helpToolTipLookService;
                    break;
                case HelpToolTipControlType.HowToMove:
                    _currentHelpToolTipService = _helpToolTipMoveService;
                    break;
                case HelpToolTipControlType.InputPressed:
                    _currentHelpToolTipService = _helpToolTipInputPressedService;
                    break;
            }
        }

        private void SetUpNextHelpToolTip()
        {
            if (helpToolTipControls.Count == 0)
            {
                _activeHelpToolTipControl = null;
                _toolTipServiceTimerCooldown.StopTimer();
                _toolTipSucessTimerCooldown.StopTimer();
                gameObject.SetActive(false);
                return;
            }
            
            _activeHelpToolTipControl = helpToolTipControls.First();
            helpToolTipControls.RemoveAt(0);
            
            _helpToolTipSlider.value = 0f;
            
            _currentText = _activeHelpToolTipControl.helpingMessage;
            _helpToolTipText.text = _currentText;
            _helpToolTipImage.sprite = _activeHelpToolTipControl.HelpingImage;
            
            UpdateHelpToolTipCurrentService();
            
            StartTransition();
        }

        private void SetUpService()
        {
            switch (_activeHelpToolTipControl.helpToolTipControlType)
            {
                case HelpToolTipControlType.HowToLook:
                    break;
                case HelpToolTipControlType.HowToMove:
                    break;
                case HelpToolTipControlType.InputPressed:
                    break;
            }
        }
        
        private void StartTransition()
        {
            _toolTipSucessTimerCooldown.StartTimer(helpSwitchCooldown, ActivateHelpToolTipService);
        }

        private void ActivateHelpToolTipService()
        {
            if (_toolTipSucessTimerCooldown.IsTimerRunning) return;

            _helpToolTipSlider.value += _currentHelpToolTipService.Activate();
            _toolTipServiceTimerCooldown.StartTimer(_activeHelpToolTipControl.actionCooldown, ActivateHelpToolTipService);
        }
        
        private void Subscribe()
        {
            ToolTipManager.UpdateToolTipControlSchemeWithHmd += UpdateAllHelpToolTipSo;
            ToolTipManager.UpdateToolTipControlScheme += UpdateAllHelpToolTipSo;
            OnUpdateIsShowingToolTip += UpdateIsShowingToolTip;
        }

        private void UnSubscribe()
        {
            ToolTipManager.UpdateToolTipControlSchemeWithHmd -= UpdateAllHelpToolTipSo;
            ToolTipManager.UpdateToolTipControlScheme -= UpdateAllHelpToolTipSo;
            OnUpdateIsShowingToolTip -= UpdateIsShowingToolTip;
            _toolTipServiceTimerCooldown.StopTimer();
            _toolTipSucessTimerCooldown.StopTimer();
        }

        private void UpdateAllHelpToolTipSo(BroadcastControlsStatus.ControlScheme controlScheme)
        {
            for (int i = 0; i < helpToolTipControls.Count; i++)
            {
                Sprite newSprite = helpToolTipControlIconSo
                    .GetIcon(helpToolTipControls[i].helpToolTipControlType, controlScheme);
                helpToolTipControls[i].UpdateSprite(newSprite);
            }
            
            Sprite newActualSprite = helpToolTipControlIconSo
                .GetIcon(_activeHelpToolTipControl.helpToolTipControlType, controlScheme);
            _activeHelpToolTipControl.UpdateSprite(newActualSprite);
            
            _helpToolTipImage.sprite = _activeHelpToolTipControl.HelpingImage;
            
            _helpToolTipInputPressedService.UpdateFromControlScheme(controlScheme);
            _currentHelpToolTipService?.UpdateFromControlScheme(controlScheme);
            
            _currentControlScheme = controlScheme;
        }
        
        private void UpdateAllHelpToolTipSo(bool hmdStatus)
        {
            if (hmdStatus)
            {
                UpdateAllHelpToolTipSo(BroadcastControlsStatus.ControlScheme.XR);
            }
            else
            {
                UpdateAllHelpToolTipSo(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
            }
        }
    }
}