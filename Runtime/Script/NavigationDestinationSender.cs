using UnityEngine;

namespace jeanf.tooltip
{
    public class NavigationDestinationSender : MonoBehaviour
    {
        
        public delegate void SendDestination(Transform destination);
        public static SendDestination OnSendDestination;
        
        private void OnEnable()
        {
            OnSendDestination?.Invoke(gameObject.transform);
        }
    }
}
