using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using UnityEngine;

namespace HeadTrack
{
    /// <summary>
    /// The mod's entry point. The game creates it without running a
    /// constructor or field initializers (FormatterServices.GetUninitializedObject),
    /// so everything an instance needs is set up in <see cref="OnLoad"/>.
    /// </summary>
    public sealed class Mod : IMod
    {
        /// <summary>Writes to Logs/HeadTrack.log in the game's user data folder.</summary>
        public static readonly ILog Log = LogManager.GetLogger(nameof(HeadTrack)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            string version = typeof(Mod).Assembly.GetName().Version.ToString();
            Log.Info($"OnLoad: HeadTrack {version}, game {Application.version}, Unity {Application.unityVersion}");
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Log.Info($"mod assembly: {asset.path}");
            }
            else
            {
                Log.Warn("mod assembly: not found in the mod manager");
            }
        }

        public void OnDispose()
        {
            Log.Info("OnDispose");
        }
    }
}
