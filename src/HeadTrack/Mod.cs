using System;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Rendering;
using Game.SceneFlow;
using HarmonyLib;
using HeadTrack.Core;
using HeadTrack.Debugging;
using HeadTrack.Options;
using HeadTrack.Patches;
using HeadTrack.Rendering;
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
        private static HeadTrackLocale? s_English;
        private static HeadTrackLocale? s_Russian;

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
            RegisterOptions();
            // Debug overrides on top of the options, until the next change in Options.
            Runtime.LoadConfig(modDirectory);
            Runtime.UpdateListener(Runtime.Now);

            updateSystem.UpdateBefore<HeadTrackRestoreSystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);
            updateSystem.UpdateAfter<HeadTrackApplySystem, CameraUpdateSystem>(SystemUpdatePhase.PreCulling);

            s_Harmony = new Harmony(HarmonyId);
            Patch("culling", () => { CullingPatches.Apply(s_Harmony); return true; });
            Patch("GTAO for the off-axis view", () => AoOffAxisPatch.Apply(s_Harmony));

            s_Host = new GameObject(nameof(HeadTrack));
            UnityEngine.Object.DontDestroyOnLoad(s_Host);
            HeadTrackHost host = s_Host.AddComponent<HeadTrackHost>();
            if (Runtime.DebugPort > 0)
            {
                host.StartChannel(Runtime.DebugPort);
            }

            Log.Info($"ready: path {Runtime.Path}, mode {Runtime.Settings.Mapper.Mode}, source {Runtime.Settings.Mapper.Source}, gain {Runtime.Settings.Mapper.Gain}");
        }

        public void OnDispose()
        {
            if (s_Host != null)
            {
                UnityEngine.Object.Destroy(s_Host);
                s_Host = null;
            }
            UnregisterOptions();
            Runtime.Listener.Dispose();
            CameraRestorer.Restore();
            Runtime.CoverageActive = false;
            Runtime.LodPinActive = false;
            s_Harmony?.UnpatchAll(HarmonyId);
            s_Harmony = null;
            Log.Info("OnDispose");
        }

        /// <summary>One group of Harmony patches; a target that moved in a game update costs only that group.</summary>
        private static void Patch(string what, Func<bool> apply)
        {
            try
            {
                if (apply())
                {
                    Log.Info($"patched: {what}");
                }
                else
                {
                    Log.Warn($"not patched (target not found): {what}");
                }
            }
            catch (Exception e)
            {
                Log.Error($"patch failed: {what}: {e}");
            }
        }

        /// <summary>
        /// The options page, its texts, its keys and its saved values, in the
        /// order the game needs: the key bindings exist before LoadSettings
        /// applies saved rebinds, and Apply runs once because LoadSettings
        /// does not.
        /// </summary>
        private void RegisterOptions()
        {
            try
            {
                // Each constructor records itself in ModSetting.instances: the live one goes last.
                var defaults = new HeadTrackOptions(this);
                var options = new HeadTrackOptions(this);
                options.RegisterInOptionsUI();
                s_English = HeadTrackLocale.English(options);
                s_Russian = HeadTrackLocale.Russian(options);
                GameManager.instance.localizationManager.AddSource("en-US", s_English);
                GameManager.instance.localizationManager.AddSource("ru-RU", s_Russian);
                options.RegisterKeyBindings();
                AssetDatabase.global.LoadSettings(nameof(HeadTrack), options, defaults);
                Runtime.Options = options;
                options.Apply();
                Log.Info("options registered");
            }
            catch (Exception e)
            {
                Log.Error($"options failed, running on defaults: {e}");
            }
        }

        private static void UnregisterOptions()
        {
            HeadTrackOptions? options = Runtime.Options;
            Runtime.Options = null;
            if (options != null)
            {
                // The input manager cannot drop actions; switched off, they never fire.
                foreach (Game.Input.ProxyAction action in options.GetActions())
                {
                    action.shouldBeEnabled = false;
                }
                options.UnregisterInOptionsUI();
            }
            Colossal.Localization.LocalizationManager? localization = GameManager.instance?.localizationManager;
            if (localization != null)
            {
                if (s_English != null)
                {
                    localization.RemoveSource("en-US", s_English);
                }
                if (s_Russian != null)
                {
                    localization.RemoveSource("ru-RU", s_Russian);
                }
            }
            s_English = null;
            s_Russian = null;
        }
    }
}
