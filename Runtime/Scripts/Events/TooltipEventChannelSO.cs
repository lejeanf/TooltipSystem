using UnityEngine;
using UnityEngine.Events;
using jeanf.EventSystem;

namespace jeanf.tooltip 
{ 
    [CreateAssetMenu(menuName = "Events/Tooltip Event Channel")]
    public class TooltipEventChannelSO : DescriptionBaseSO
    {
        public UnityAction<TooltipSO> OnEventRaised;

        public void RaiseEvent(TooltipSO tooltipSo)
        {
            if (OnEventRaised != null)
            {
                OnEventRaised.Invoke(tooltipSo);
            }
        }
    }
}

