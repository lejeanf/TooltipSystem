using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableHelpToolTip : ToolTip
    {
        [SerializeField] private string helpingMessage;
        [SerializeField] private float timeBeforeShowingInSeconds = 120;
        [SerializeField] public Color helpingColor = Color.yellow;
        [SerializeField] public Material helpingMaterial;
        [SerializeField] private HelpToolTipInteractableType helpToolTipInteractableType;
        
        private TMP_Text _helpToolTipText;
        private ToolTipTimer _toolTipTimerManager;
        private InteractableToolTipController _interactableToolTipController;
        private ParticleSystem _particleSystem;

        private Renderer _rend;
        private Color _originalColor;
        
        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy() => UnSubscribe();

        private void Subscribe()
        {
            OnUpdateIsShowingToolTip += UpdateIsShowingToolTip;
        }

        private void UnSubscribe()
        {
            OnUpdateIsShowingToolTip -= UpdateIsShowingToolTip;
        }
        
        private void Awake()
        {
            _toolTipTimerManager = new ToolTipTimer();
            
            _interactableToolTipController = GetComponent<InteractableToolTipController>();
            
            _rend = GetComponent<Renderer>();
            _originalColor = _rend.material.color;
            
            _particleSystem = GetComponent<ParticleSystem>();
            var particleRenderer = _particleSystem.GetComponent<ParticleSystemRenderer>();
            particleRenderer.material = helpingMaterial;
            _particleSystem.Stop();
            
            _toolTipTimerManager.StartTimer(timeBeforeShowingInSeconds, ShowHelpToolTip);
        }

        private void Update()
        {
            if (!showToolTip) {HideHelpToolTip(); return;}
            
            if (_interactableToolTipController.IsToolTipDisplayed)
            {
                HideHelpToolTip();
            }
        }

        private void ShowHelpToolTip()
        {
            switch (helpToolTipInteractableType)
            {
                case HelpToolTipInteractableType.ColorToolTip:
                    _rend.material.color = helpingColor;
                    break;
                case HelpToolTipInteractableType.ParticuleToolTip:
                    if (!_particleSystem.isPlaying)
                        _particleSystem.Play();
                    break;
            }
        }

        private void HideHelpToolTip()
        {
            _toolTipTimerManager.StopTimer();
            switch (helpToolTipInteractableType)
            {
                case HelpToolTipInteractableType.ColorToolTip:
                    _rend.material.color = _originalColor;
                    break;
                case HelpToolTipInteractableType.ParticuleToolTip:
                    _particleSystem.Stop();
                    break;
            }
        }
    }
}