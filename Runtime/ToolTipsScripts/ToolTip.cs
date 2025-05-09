using UnityEngine;

namespace jeanf.tooltip
{
    public abstract class ToolTip : MonoBehaviour
    {
        [SerializeField] protected bool showToolTip = true;

        protected void UpdateIsShowingToolTip(bool isShowing)
        {
            showToolTip = isShowing;
        }
    }
}