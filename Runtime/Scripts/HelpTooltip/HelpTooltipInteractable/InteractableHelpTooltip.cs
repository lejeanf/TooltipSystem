using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace jeanf.tooltip
{
    public class InteractableHelpTooltip : Tooltip
    {
        [SerializeField] private string helpingMessage;
        [SerializeField] private float timeBeforeShowingInSeconds = 120;
        [SerializeField] public Color helpingColor = Color.yellow;
        [SerializeField] public Material helpingMaterial;
        [FormerlySerializedAs("helpToolTipInteractableType")]
        [SerializeField] private HelpTooltipInteractableType helpTooltipInteractableType;
        
        private TMP_Text _helpTooltipText;
        private TooltipTimer _toolTipTimerManager;
        private InteractableTooltipController _interactableTooltipController;
        private ParticleSystem _particleSystem;

        private Renderer _rend;
        private Color _originalColor;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            TooltipControlSchemeManager.UpdateShowTooltip += UpdateIsShowingTooltip;
            TooltipControlSchemeManager.DisableTooltip += DisableTooltip;
        }

        private void UnSubscribe()
        {
            TooltipControlSchemeManager.UpdateShowTooltip -= UpdateIsShowingTooltip;
            TooltipControlSchemeManager.DisableTooltip -= DisableTooltip;
        }
        
        private void Awake()
        {
            _toolTipTimerManager = new TooltipTimer();
            
            _interactableTooltipController = GetComponent<InteractableTooltipController>();
            
            _rend = GetComponent<Renderer>();
            _originalColor = _rend.material.color;
            
            _particleSystem = GetComponent<ParticleSystem>();
            var particleRenderer = _particleSystem.GetComponent<ParticleSystemRenderer>();
            particleRenderer.material = helpingMaterial;
            _particleSystem.Stop();
            
            _toolTipTimerManager.StartTimer(timeBeforeShowingInSeconds, ShowHelpTooltip).Forget();
        }

        private void Update()
        {
            if (!showTooltip) {HideHelpTooltip(); return;}
            
            if (_interactableTooltipController.IsTooltipDisplayed)
            {
                HideHelpTooltip();
            }
        }

        private void ShowHelpTooltip()
        {
            switch (helpTooltipInteractableType)
            {
                case HelpTooltipInteractableType.ColorTooltip:
                    _rend.material.color = helpingColor;
                    break;
                case HelpTooltipInteractableType.ParticuleTooltip:
                    if (!_particleSystem.isPlaying)
                        _particleSystem.Play();
                    break;
            }
        }

        private void HideHelpTooltip()
        {
            _toolTipTimerManager.StopTimer();
            switch (helpTooltipInteractableType)
            {
                case HelpTooltipInteractableType.ColorTooltip:
                    _rend.material.color = _originalColor;
                    break;
                case HelpTooltipInteractableType.ParticuleTooltip:
                    _particleSystem.Stop();
                    break;
            }
        }
    }
}