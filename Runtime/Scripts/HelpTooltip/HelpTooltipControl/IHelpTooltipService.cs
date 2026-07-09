using jeanf.universalplayer;

namespace jeanf.tooltip
{
    public interface IHelpTooltipService
    {
        public float Activate();
        public void UpdateFromControlScheme(BroadcastControlsStatus.ControlScheme controlScheme);

    }
}