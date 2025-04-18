using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableHelpToolTip : MonoBehaviour
    {
        [SerializeField] private string helpingMessage;
        [SerializeField] private float timeBeforeShowingInSeconds = 120;
        [SerializeField] public Color helpingColor = Color.yellow;
        [SerializeField] public Material helpingMaterial;
        [SerializeField] private HelpToolTipInteractableType helpToolTipInteractableType;
        
        private TMP_Text _helpToolTipText;
        private ToolTipTimer _toolTipTimerManager;
        private InteractableToolTip _interactableToolTip;
        private ParticleSystem _particleSystem;

        private Renderer _rend;
        private Color _originalColor;
        private void Awake()
        {
            _toolTipTimerManager = new ToolTipTimer();
            
            _interactableToolTip = GetComponent<InteractableToolTip>();
            
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
            if (_interactableToolTip.IsToolTipDisplayed)
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