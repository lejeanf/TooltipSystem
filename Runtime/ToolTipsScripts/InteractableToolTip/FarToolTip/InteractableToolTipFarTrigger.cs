using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTipFarTrigger : MonoBehaviour
    {
        private InteractableToolTipFar interactableToolTipFar;

        public void Awake()
        {
            interactableToolTipFar = gameObject.GetComponentInParent<InteractableToolTipFar>();
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableToolTipFar.UpdatePlayerInRange(true);
                interactableToolTipFar.ShowImage();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableToolTipFar.UpdatePlayerInRange(false);
                interactableToolTipFar.HideImage();
            }
        }
    }
}