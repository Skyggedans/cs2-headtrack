using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using HeadTrack.Core;
using UnityEngine;

namespace HeadTrack
{
    /// <summary>How the off-axis frustum reaches the camera.</summary>
    internal enum ProjectionPath
    {
        /// <summary>camera.projectionMatrix = the off-axis frustum.</summary>
        Explicit,

        /// <summary>The physical camera's own lens: gate fit None, sensor, focal length and lens shift.</summary>
        Physical,
    }

    /// <summary>What Apply overwrote on the camera, so Restore can put it back.</summary>
    internal struct CameraSnapshot
    {
        public Camera Camera;
        public Vector3 Position;
        public float Far;
        public bool Physical;
        public Vector2 SensorSize;
        public Camera.GateFitMode GateFit;
        public float FocalLength;
        public Vector2 LensShift;
        public bool WroteMatrix;
    }

    /// <summary>
    /// The mod's state, shared by its systems, patches and the debug host.
    /// Everything here runs on the main thread except the UDP receiver,
    /// which only touches the ring.
    /// </summary>
    internal static class Runtime
    {
        public static readonly HeadTrackSettings Settings = new HeadTrackSettings();
        public static readonly PoseRing Ring = new PoseRing(64);
        public static readonly HeadCoupledCamera Head = new HeadCoupledCamera(Settings);

        public static UdpPoseReceiver? Receiver;
        public static Unity.Entities.World? World;
        public static long LastSequence;
        public static bool Enabled = true;
        public static ProjectionPath Path = ProjectionPath.Explicit;
        public static bool PinLodFov = true;

        public static string PoseAddress = "127.0.0.1";
        public static int PosePort = 4242;
        public static int DebugPort;
        public static string ShotsDirectory = string.Empty;
        public static double StatusSeconds = 10.0;

        /// <summary>Set by Apply when it wrote the camera; Restore undoes it before the game's camera update.</summary>
        public static bool Dirty;
        public static CameraSnapshot Snapshot;

        /// <summary>This frame's off-axis frustum for the pre-culling cover, when Apply wrote one.</summary>
        public static bool CoverageActive;
        public static FrustumTangents CoverageRendered;
        public static FrustumTangents CoverageView;

        /// <summary>The engine's LOD field of view before Apply's writes, pinned while the camera is shifted.</summary>
        public static bool LodPinActive;
        public static float VanillaLodFov;
        public static float ShiftedLodFov;

        public static FrameResult LastFrame;
        public static FrustumTangents LastRendered;
        public static Vector3 LastVanillaPosition;
        public static float LastCoverFov;
        public static float LastCoverAspect;
        public static long FramesSeen;
        public static long FramesApplied;
        public static long FramesClamped;

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        /// <summary>Reads HeadTrack.debug.cfg from the mod folder, if there is one.</summary>
        public static void LoadConfig(string? modDirectory)
        {
            if (modDirectory == null)
            {
                return;
            }
            string path = System.IO.Path.Combine(modDirectory, "HeadTrack.debug.cfg");
            if (!File.Exists(path))
            {
                Mod.Log.Info($"no {path}: defaults (pose UDP {PoseAddress}:{PosePort}, debug channel off)");
                return;
            }
            KeyValueConfig config = KeyValueConfig.Parse(File.ReadAllText(path));
            foreach (string error in config.Errors)
            {
                Mod.Log.Warn($"{path}: {error}");
            }
            foreach (string key in new[]
            {
                "enabled", "path", "mode", "source", "gain", "depth_gain", "window_depth", "distance_mm",
                "screen_width_mm", "screen_height_mm", "max_shift_x", "max_shift_y", "pin_lod_fov",
                "pose_address", "pose_port", "debug_port", "shots_dir", "status_seconds",
            })
            {
                string? value = config.Get(key);
                if (value != null && !TrySet(key, value, out string? error))
                {
                    Mod.Log.Warn($"{path}: {key}: {error}");
                }
            }
            Mod.Log.Info($"read {path}");
        }

        /// <summary>Changes one setting by name; shared by the debug config and the debug channel's "set".</summary>
        public static bool TrySet(string name, string value, out string? error)
        {
            error = null;
            bool Number(out double result)
            {
                bool ok = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                    && !double.IsNaN(result) && !double.IsInfinity(result);
                return ok;
            }
            double number;
            switch (name)
            {
                case "enabled":
                case "pin_lod_fov":
                    bool flag = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (!flag && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "expected 0 or 1";
                        return false;
                    }
                    if (name == "enabled")
                    {
                        Enabled = flag;
                    }
                    else
                    {
                        PinLodFov = flag;
                    }
                    return true;
                case "path":
                    if (value.Equals("explicit", StringComparison.OrdinalIgnoreCase))
                    {
                        Path = ProjectionPath.Explicit;
                        return true;
                    }
                    if (value.Equals("physical", StringComparison.OrdinalIgnoreCase))
                    {
                        Path = ProjectionPath.Physical;
                        return true;
                    }
                    error = "expected explicit or physical";
                    return false;
                case "mode":
                    if (value.Equals("centred", StringComparison.OrdinalIgnoreCase) || value.Equals("centered", StringComparison.OrdinalIgnoreCase))
                    {
                        Settings.Mapper.Mode = EyeMode.Centred;
                        return true;
                    }
                    if (value.Equals("window", StringComparison.OrdinalIgnoreCase))
                    {
                        Settings.Mapper.Mode = EyeMode.Window;
                        return true;
                    }
                    error = "expected centred or window";
                    return false;
                case "source":
                    if (value.Equals("absolute", StringComparison.OrdinalIgnoreCase))
                    {
                        Settings.Mapper.Source = PoseSource.Absolute;
                        return true;
                    }
                    if (value.Equals("relative", StringComparison.OrdinalIgnoreCase))
                    {
                        Settings.Mapper.Source = PoseSource.Relative;
                        return true;
                    }
                    error = "expected absolute or relative";
                    return false;
                case "gain":
                case "depth_gain":
                case "window_depth":
                case "distance_mm":
                case "screen_width_mm":
                case "screen_height_mm":
                case "max_shift_x":
                case "max_shift_y":
                case "status_seconds":
                    if (!Number(out number) || number <= 0)
                    {
                        error = "expected a positive number";
                        return false;
                    }
                    switch (name)
                    {
                        case "gain": Settings.Mapper.Gain = number; break;
                        case "depth_gain": Settings.Mapper.DepthGain = number; break;
                        case "window_depth": Settings.WindowDepthFraction = number; break;
                        case "distance_mm": Settings.Mapper.RelativeDistanceMm = number; break;
                        case "screen_width_mm": Settings.Mapper.ScreenWidthMm = number; break;
                        case "screen_height_mm": Settings.Mapper.ScreenHeightMm = number; break;
                        case "max_shift_x": Settings.Mapper.MaxShiftX = number; break;
                        case "max_shift_y": Settings.Mapper.MaxShiftY = number; break;
                        default: StatusSeconds = number; break;
                    }
                    return true;
                case "pose_address":
                    if (!IPAddress.TryParse(value, out _))
                    {
                        error = "expected an IPv4 address";
                        return false;
                    }
                    PoseAddress = value;
                    return true;
                case "pose_port":
                case "debug_port":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 0 || port > 65535)
                    {
                        error = "expected a port number";
                        return false;
                    }
                    if (name == "pose_port")
                    {
                        PosePort = port;
                    }
                    else
                    {
                        DebugPort = port;
                    }
                    return true;
                case "shots_dir":
                    if (!System.IO.Path.IsPathRooted(value))
                    {
                        error = "expected an absolute path";
                        return false;
                    }
                    ShotsDirectory = value;
                    return true;
                default:
                    error = $"unknown setting '{name}'";
                    return false;
            }
        }

        public static void StartReceiver()
        {
            StopReceiver();
            Receiver = new UdpPoseReceiver(Ring);
            if (Receiver.Start(IPAddress.Parse(PoseAddress), PosePort))
            {
                Mod.Log.Info($"listening for head poses on UDP {PoseAddress}:{Receiver.Port}");
            }
            else
            {
                Mod.Log.Error($"head poses: {Receiver.Error}");
            }
        }

        public static void StopReceiver()
        {
            Receiver?.Dispose();
            Receiver = null;
        }
    }
}
