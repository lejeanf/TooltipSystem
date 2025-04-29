using UnityEngine;

namespace jeanf.tooltip
{
    public abstract class ToolTip : MonoBehaviour
    {
        [SerializeField] protected bool showToolTip = true;
        
        public delegate void UpdateIsShowingToolTipDelegate(bool isShowing);
        public static UpdateIsShowingToolTipDelegate OnUpdateIsShowingToolTip;

        protected void UpdateIsShowingToolTip(bool isShowing)
        {
            showToolTip = isShowing;
        }
    }
}