using UnityEngine;

namespace jeanf.tooltip
{
    public class NavigationMapCornerSender : MonoBehaviour
    {
        [SerializeField] private NavigationMapCornerType cornerType;
        
        public delegate void SendNewMapCornerDelegate(Transform corner, NavigationMapCornerType cornerType);
        public static SendNewMapCornerDelegate OnSendNewMapCorner;
        private void OnEnable()
        {
            OnSendNewMapCorner?.Invoke(gameObject.transform, cornerType);
        }
    }
}
