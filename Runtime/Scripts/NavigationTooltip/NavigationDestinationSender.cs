using UnityEngine;

namespace jeanf.tooltip
{
    public class NavigationDestinationSender : MonoBehaviour
    {
        public delegate void SendDestinationDelegate(Transform destination);
        public static SendDestinationDelegate OnSendDestination;
        
        private void OnEnable()
        {
            OnSendDestination?.Invoke(gameObject.transform);
        }
    }
}
