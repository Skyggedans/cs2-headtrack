using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using HeadTrack.Core;
using HeadTrack.Options;
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

        /// <summary>Keeps a receiver on <see cref="PoseAddress"/>:<see cref="PosePort"/>; updated once a frame.</summary>
        public static readonly PoseListener Listener = new PoseListener(() => new UdpPoseReceiver(Ring));

        /// <summary>The options page, once registered.</summary>
        public static HeadTrackOptions? Options;

        public static Unity.Entities.World? World;
        public static long LastSequence;
        public static bool Enabled = true;
        public static ProjectionPath Path = ProjectionPath.Explicit;
        public static bool PinLodFov = true;

        public static string PoseAddress = "127.0.0.1";
        public static IPAddress PoseIPAddress = IPAddress.Loopback;
        public static int PosePort = 4242;

        /// <summary>The options page's choices that are not core settings.</summary>
        public static bool FollowCamera = true;
        public static bool PhotoMode;
        public static ToolTracking WhileBuilding = ToolTracking.GainUpToOne;
        public static bool SuppressHeadMotionBlur = true;

        private static string? s_ListenerStatus;
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

        /// <summary>The debug channel holds the eye as a tool drag would.</summary>
        public static bool ForceFreeze;

        /// <summary>How far (m) the head may move the camera from the game's position, and what the last cast found.</summary>
        public static double ObstacleAllowed = double.PositiveInfinity;

        public static double LastObstacleClear = double.NaN;
        public static FrustumTangents LastRendered;
        public static Vector3 LastVanillaPosition;
        public static float LastCoverFov;
        public static float LastCoverAspect;
        public static long FramesSeen;
        public static long FramesApplied;
        public static long FramesClamped;

        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        /// <summary>The receiver listening now, for its counters.</summary>
        public static UdpPoseReceiver? Receiver => Listener.Receiver as UdpPoseReceiver;

        /// <summary>
        /// Hands the options page's values to the core and the mod. Runs on the
        /// main thread after every change in Options and once at load; the
        /// debug config and the debug channel's "set" act on the same settings
        /// and last until the next change in Options.
        /// </summary>
        public static void ApplyOptions(HeadTrackOptions o)
        {
            Enabled = o.Enabled;
            MapperSettings m = Settings.Mapper;
            m.Mode = o.Mode == HeadTrackMode.Window ? EyeMode.Window : EyeMode.Centred;
            m.Source = o.Source == HeadPoseSource.Absolute ? PoseSource.Absolute : PoseSource.Relative;
            m.Gain = o.Gain;
            m.DepthGain = o.DepthGain;
            m.ScreenWidthMm = o.ScreenWidthMm;
            m.ScreenHeightMm = o.ScreenHeightMm;
            m.RelativeDistanceMm = o.ViewingDistanceMm;
            m.InvertX = o.InvertX;
            m.InvertY = o.InvertY;
            m.InvertZ = o.InvertZ;
            Settings.WindowDepthFraction = o.WindowDepth;
            int smoothing = Math.Max(Smoothing.Min, Math.Min(Smoothing.Max, o.Smoothing));
            Settings.Signal.Lateral = Smoothing.Lateral(smoothing);
            Settings.Signal.Depth = Smoothing.Depth(smoothing);
            FollowCamera = o.FollowCamera;
            PhotoMode = o.PhotoMode;
            WhileBuilding = o.WhileBuilding;
            SuppressHeadMotionBlur = o.SuppressHeadMotionBlur;
            PosePort = o.Port >= SettingsLimits.PortMin && o.Port <= SettingsLimits.PortMax ? o.Port : 4242;
            Validate();
        }

        /// <summary>Keeps every setting in range after a change, and logs what it had to correct.</summary>
        public static void Validate()
        {
            foreach (string correction in Settings.Validate())
            {
                Mod.Log.Warn($"setting corrected: {correction}");
            }
        }

        /// <summary>Keeps the pose receiver where the settings say; logs each change of its state.</summary>
        public static void UpdateListener(double now)
        {
            Listener.Update(now, PoseIPAddress, PosePort);
            if (Listener.Status != s_ListenerStatus)
            {
                s_ListenerStatus = Listener.Status;
                Mod.Log.Info($"head poses: {s_ListenerStatus}");
            }
        }

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
                "enabled", "follow_camera", "photo_mode", "path", "mode", "source", "gain", "depth_gain", "window_depth", "distance_mm",
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
                case "follow_camera":
                case "photo_mode":
                    bool flag = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (!flag && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "expected 0 or 1";
                        return false;
                    }
                    switch (name)
                    {
                        case "enabled":
                            Enabled = flag;
                            break;
                        case "follow_camera":
                            FollowCamera = flag;
                            break;
                        case "photo_mode":
                            PhotoMode = flag;
                            break;
                        default:
                            PinLodFov = flag;
                            break;
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
                    if (!Number(out number))
                    {
                        error = "expected a number";
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
                        default: Settings.Mapper.MaxShiftY = number; break;
                    }
                    Validate();
                    return true;
                case "status_seconds":
                    if (!Number(out number) || number <= 0)
                    {
                        error = "expected a positive number";
                        return false;
                    }
                    StatusSeconds = number;
                    return true;
                case "pose_address":
                    if (!IPAddress.TryParse(value, out IPAddress? address))
                    {
                        error = "expected an IPv4 address";
                        return false;
                    }
                    PoseAddress = value;
                    PoseIPAddress = address;
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
    }
}
