using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableTooltipFarTrigger : MonoBehaviour
    {
        private InteractableTooltipFar interactableTooltipFar;

        public void Awake()
        {
            interactableTooltipFar = gameObject.GetComponentInParent<InteractableTooltipFar>();
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableTooltipFar.UpdatePlayerInRange(true);
                interactableTooltipFar.ShowImage();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableTooltipFar.UpdatePlayerInRange(false);
                interactableTooltipFar.HideImage();
            }
        }
    }
}