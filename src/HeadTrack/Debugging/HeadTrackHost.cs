using System;
using System.Collections;
using System.IO;
using System.Net;
using Game.SceneFlow;
using Game.Simulation;
using HeadTrack.Core;
using UnityEngine;

namespace HeadTrack.Debugging
{
    /// <summary>
    /// A persistent GameObject that executes debug-channel commands on the main
    /// thread: screenshots at the end of the frame, camera dumps, switches,
    /// settings, pause and quit.
    /// </summary>
    internal sealed class HeadTrackHost : MonoBehaviour
    {
        private DebugChannel? m_Channel;
        private float m_SpeedBeforePause = 1f;

        public void StartChannel(int port)
        {
            m_Channel = new DebugChannel();
            if (m_Channel.Start(port, out string? error))
            {
                Mod.Log.Info($"debug channel on UDP 127.0.0.1:{m_Channel.Port}");
            }
            else
            {
                Mod.Log.Error($"debug channel: {error}");
                m_Channel = null;
            }
        }

        private void Update()
        {
            DebugChannel? channel = m_Channel;
            if (channel == null)
            {
                return;
            }
            while (channel.TryDequeue(out string text, out EndPoint from))
            {
                try
                {
                    Execute(channel, text.Trim(), from);
                }
                catch (Exception e)
                {
                    Mod.Log.Error($"debug command '{text}' failed: {e}");
                    channel.Reply(from, $"err {text}: {e.Message}");
                }
            }
        }

        private void OnDestroy()
        {
            m_Channel?.Dispose();
            m_Channel = null;
        }

        private void Execute(DebugChannel channel, string text, EndPoint from)
        {
            if (!DebugCommand.TryParse(text, out DebugCommand command, out string? error))
            {
                channel.Reply(from, $"err {error}");
                return;
            }
            switch (command.Verb)
            {
                case DebugVerb.Shot:
                    if (string.IsNullOrEmpty(Runtime.ShotsDirectory))
                    {
                        channel.Reply(from, "err no shots_dir in HeadTrack.debug.cfg");
                        return;
                    }
                    StartCoroutine(Shot(channel, command.Argument, from));
                    return;
                case DebugVerb.Dump:
                    string dump = Diagnostics.Build(Camera.main);
                    Mod.Log.Info($"dump: {dump}");
                    channel.Reply(from, $"ok dump | {dump}");
                    return;
                case DebugVerb.Enable:
                    Runtime.Enabled = command.Argument == "1";
                    channel.Reply(from, $"ok enable {command.Argument}");
                    return;
                case DebugVerb.Set:
                    channel.Reply(from, Runtime.TrySet(command.Name, command.Value, out string? setError)
                        ? $"ok set {command.Name} {command.Value}"
                        : $"err set {command.Name}: {setError}");
                    return;
                case DebugVerb.Pause:
                    channel.Reply(from, Pause(command.Argument == "1") ? $"ok pause {command.Argument}" : "err no simulation");
                    return;
                case DebugVerb.Centre:
                    Runtime.Head.RequestCentre(Runtime.Now);
                    channel.Reply(from, "ok centre");
                    return;
                case DebugVerb.Quit:
                    channel.Reply(from, "ok quit");
                    Mod.Log.Info("quit requested on the debug channel");
                    GameManager.QuitGame();
                    return;
            }
        }

        private bool Pause(bool pause)
        {
            SimulationSystem? simulation = Runtime.World?.GetExistingSystemManaged<SimulationSystem>();
            if (simulation == null)
            {
                return false;
            }
            if (pause)
            {
                if (simulation.selectedSpeed > 0)
                {
                    m_SpeedBeforePause = simulation.selectedSpeed;
                }
                simulation.selectedSpeed = 0f;
            }
            else
            {
                simulation.selectedSpeed = m_SpeedBeforePause > 0 ? m_SpeedBeforePause : 1f;
            }
            return true;
        }

        /// <summary>Two frames for the latest state to render, then what the player sees at the end of a frame.</summary>
        private IEnumerator Shot(DebugChannel channel, string name, EndPoint from)
        {
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            string dump = Diagnostics.Build(Camera.main);
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png;
            try
            {
                png = texture.EncodeToPNG();
            }
            finally
            {
                Destroy(texture);
            }
            Directory.CreateDirectory(Runtime.ShotsDirectory);
            string path = Path.Combine(Runtime.ShotsDirectory, name + ".png");
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, png);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
            Mod.Log.Info($"shot {path}: {dump}");
            channel.Reply(from, $"ok shot {path} | {dump}");
        }
    }
}
