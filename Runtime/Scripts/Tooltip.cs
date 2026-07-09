using UnityEngine;
using UnityEngine.Serialization;

namespace jeanf.tooltip
{
    public abstract class Tooltip : MonoBehaviour
    {
        [FormerlySerializedAs("showToolTip")]
        [SerializeField] protected bool showTooltip = false;

        protected void UpdateIsShowingTooltip(bool isShowing)
        {
            showTooltip = isShowing;
        }

        protected void DisableTooltip()
        {
            gameObject.SetActive(false);
        }
    }
}