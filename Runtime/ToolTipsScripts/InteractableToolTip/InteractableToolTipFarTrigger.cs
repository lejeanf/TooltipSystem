using UnityEngine;

namespace jeanf.tooltip
{
    public class InteractableToolTipFarTrigger : MonoBehaviour
    {
        [SerializeField] private InteractableToolTipFar interactableToolTipFar;
        
        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableToolTipFar.ShowImage();
                interactableToolTipFar.UpdatePlayerInRange(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == LayerMask.NameToLayer("Player"))
            {
                interactableToolTipFar.HideImage();
                interactableToolTipFar.UpdatePlayerInRange(false);
            }
        }
    }
}