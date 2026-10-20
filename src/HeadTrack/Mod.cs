using System;
using System.IO;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Rendering;
using Game.SceneFlow;
using HarmonyLib;
using HeadTrack.Debugging;
using HeadTrack.Patches;
using HeadTrack.Systems;
using UnityEngine;

namespace HeadTrack
{
    /// <summary>
    /// The mod's entry point. The game creates it without running a
    /// constructor or field initializers (FormatterServices.GetUninitializedObject),
    /// so everything an instance needs is set up in <see cref="OnLoad"/>;
    /// the shared state lives in static <see cref="Runtime"/>.
    /// </summary>
    public sealed class Mod : IMod
    {
        public const string HarmonyId = "cs2-headtrack";

        /// <summary>Writes to Logs/HeadTrack.log in the game's user data folder.</summary>
        public static readonly ILog Log = LogManager.GetLogger(nameof(HeadTrack)).SetShowsErrorsInUI(false);

        private static Harmony? s_Harmony;
        private static GameObject? s_Host;

        public void OnLoad(UpdateSystem updateSystem)
        {
            string version = typeof(Mod).Assembly.GetName().Version.ToString();
            Log.Info($"OnLoad: HeadTrack {version}, game {Application.version}, Unity {Application.unityVersion}");
            string? modDirectory = null;
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                modDirectory = Path.GetDirectoryName(asset.path);
                Log.Info($"mod assembly: {asset.path}");
            }
            Runtime.LoadConfig(modDirectory);
            Runtime.StartReceiver();

            updateSystem.UpdateBefore<HeadTrackRestoreSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAfter<HeadTrackApplySystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);

            try
            {
                s_Harmony = new Harmony(HarmonyId);
                CullingPatches.Apply(s_Harmony);
            }
            catch (Exception e)
            {
                Log.Error($"Harmony patches failed: {e}");
            }

            s_Host = new GameObject(nameof(HeadTrack));
            UnityEngine.Object.DontDestroyOnLoad(s_Host);
            HeadTrackHost host = s_Host.AddComponent<HeadTrackHost>();
            if (Runtime.DebugPort > 0)
            {
                host.StartChannel(Runtime.DebugPort);
            }

            Runtime.Head.RequestCentre(Runtime.Now);
            Log.Info($"ready: path {Runtime.Path}, mode {Runtime.Settings.Mapper.Mode}, source {Runtime.Settings.Mapper.Source}, gain {Runtime.Settings.Mapper.Gain}");
        }

        public void OnDispose()
        {
            if (s_Host != null)
            {
                UnityEngine.Object.Destroy(s_Host);
                s_Host = null;
            }
            Runtime.StopReceiver();
            CameraRestorer.Restore();
            Runtime.CoverageActive = false;
            Runtime.LodPinActive = false;
            s_Harmony?.UnpatchAll(HarmonyId);
            s_Harmony = null;
            Log.Info("OnDispose");
        }
    }
}
