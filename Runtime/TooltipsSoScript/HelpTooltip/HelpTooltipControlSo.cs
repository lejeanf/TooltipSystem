using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace jeanf.tooltip
{
    
    [CreateAssetMenu(fileName = "HelpTooltipControlSO", menuName = "Tooltips/HelpTooltipControlSO", order = 1)]
    public class HelpTooltipControlSo : ScriptableObject
    {
        public string helpingMessage;
        public float progressionAdded = 0.1f;
        [Tooltip("In seconds / Cooldown between verifications")]
        public float actionCooldown = 0.1f;
        public bool canBeShownMultipleTimes = false;
        public float timeBeforeShowingAgain = 120f;
        public bool canBeShownInVR = true;
        [Header("Not Required / Only required in Input Pressed mode")]
        public InputActionReference actionRequiredWhenKeyBoard;
        public InputActionReference actionRequiredWhenGamepad;
        public InputActionReference actionRequiredWhenXr;
        [Tooltip("1 helpTooltip max per types")]
        [FormerlySerializedAs("helpToolTipControlType")]
        public HelpTooltipControlType helpTooltipControlType;
        
        private Sprite _helpingImage;
        public Sprite HelpingImage => _helpingImage;

        public void UpdateSprite(Sprite sprite)
        {
            _helpingImage = sprite;
        }
    }
}