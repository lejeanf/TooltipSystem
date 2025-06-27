using UnityEngine;

namespace jeanf.tooltip
{
    public abstract class ToolTip : MonoBehaviour
    {
        [SerializeField] protected bool showToolTip = false;

        protected void UpdateIsShowingToolTip(bool isShowing)
        {
            showToolTip = isShowing;
        }

        protected void DisableToolTip()
        {
            gameObject.SetActive(false);
        }
    }
}