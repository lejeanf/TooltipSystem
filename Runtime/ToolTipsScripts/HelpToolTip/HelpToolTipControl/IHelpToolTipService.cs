using jeanf.vrplayer;

namespace jeanf.tooltip
{
    public interface IHelpToolTipService
    {
        public float Activate();
        public void UpdateFromControlScheme(BroadcastControlsStatus.ControlScheme controlScheme);

    }
}