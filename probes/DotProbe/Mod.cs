using Colossal.Logging;
using Game;
using Game.Modding;

namespace HeadTrackDotProbe
{
    /// <summary>Must never run: its folder name starts with '.'.</summary>
    public sealed class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(HeadTrackDotProbe)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Error("DOT PROBE LOADED: a mod folder starting with '.' was NOT skipped");
        }

        public void OnDispose()
        {
        }
    }
}
