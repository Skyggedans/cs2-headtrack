using System;
using Game;

namespace HeadTrack.Systems
{
    /// <summary>
    /// Runs immediately before the game's CameraUpdateSystem (PreCulling) and
    /// gives the camera back its vanilla state, so the controllers, their
    /// collision checks and the Cinemachine brain never see the shift: no
    /// drift, no feedback. The shifted state stays in force from Apply until
    /// here, through rendering and the next frame's mouse ray.
    /// </summary>
    public sealed class HeadTrackRestoreSystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            try
            {
                CameraRestorer.Restore();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"HeadTrackRestoreSystem failed and is disabled: {e}");
                Enabled = false;
            }
        }
    }
}
