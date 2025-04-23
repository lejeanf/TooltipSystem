using jeanf.EventSystem;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.Events;

namespace jeanf.tooltip
{
    [CreateAssetMenu(menuName = "Events/Control Scheme Channel")]
    public class ControlSchemeChannelSo : DescriptionBaseSO, RaiseEvent
    {
        public UnityAction<BroadcastControlsStatus.ControlScheme> OnEventRaised;

        public void RaiseEvent(BroadcastControlsStatus.ControlScheme value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}