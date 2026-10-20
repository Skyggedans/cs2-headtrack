using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Cinemachine;
using Game;
using Game.Rendering;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine;

namespace HeadTrackProbe
{
    /// <summary>
    /// Decides which frames are logged and formats what every probe point
    /// sees. A frame is either logged at every point or at none, so one
    /// logged frame shows the whole order. Logged: one frame in the main menu,
    /// the first frames after a city becomes active, a few frames after the
    /// active camera controller changes, and two frames every
    /// <see cref="Period"/> frames.
    /// </summary>
    internal static class Recorder
    {
        private const int CityBurst = 5;
        private const int ControllerBurst = 3;
        private const int Period = 1200;
        private const int PeriodicBurst = 2;

        private static readonly PropertyInfo? s_ProjectionMatrixMode = typeof(Camera).GetProperty(
            "projectionMatrixMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly HashSet<string> s_Failed = new HashSet<string>();

        private static CameraUpdateSystem? s_CameraUpdateSystem;
        private static int s_DecidedFrame = -1;
        private static bool s_LogThisFrame;
        private static int s_BurstUntil = -1;
        private static int s_NextPeriodic = -1;
        private static bool s_CityActive;
        private static bool s_MenuLogged;
        private static string? s_LastController;

        internal static void Attach(World world)
        {
            if (s_CameraUpdateSystem == null)
            {
                s_CameraUpdateSystem = world.GetExistingSystemManaged<CameraUpdateSystem>();
            }
        }

        /// <summary>
        /// Runs a probe point; one that throws is logged once with its stack
        /// and stays silent afterwards, so a broken point cannot flood the log.
        /// </summary>
        internal static void Guard(string name, Action point)
        {
            if (s_Failed.Contains(name))
            {
                return;
            }
            try
            {
                point();
            }
            catch (Exception e)
            {
                s_Failed.Add(name);
                Mod.Log.Error($"probe point {name} failed and is silenced: {e}");
            }
        }

        internal static void Point(string label, bool full = false, Camera? camera = null, string? extra = null)
        {
            if (!ShouldLog())
            {
                return;
            }
            var line = new StringBuilder(512);
            line.Append("f=").Append(Time.frameCount)
                .Append(" t=").Append(Num(Time.realtimeSinceStartupAsDouble * 1000.0, "F2")).Append("ms ")
                .Append(label);
            if (camera != null)
            {
                line.Append(" cam=").Append(Describe(camera));
            }
            Camera main = Camera.main;
            line.Append(" | main ").Append(CameraLine(main));
            if (full)
            {
                AppendFull(line, main);
            }
            if (extra != null)
            {
                line.Append(" | ").Append(extra);
            }
            Mod.Log.Info(line.ToString());
        }

        internal static string Describe(Camera? camera)
        {
            if (camera == null)
            {
                return "null";
            }
            string main = camera == Camera.main ? "(main)" : string.Empty;
            return $"{camera.name}#{camera.GetInstanceID()}{main}";
        }

        private static bool ShouldLog()
        {
            int frame = Time.frameCount;
            if (frame != s_DecidedFrame)
            {
                s_DecidedFrame = frame;
                s_LogThisFrame = Decide(frame);
            }
            return s_LogThisFrame;
        }

        private static bool Decide(int frame)
        {
            GameManager gameManager = GameManager.instance;
            if (gameManager == null)
            {
                return false;
            }
            bool loading = gameManager.isGameLoading;
            bool city = gameManager.gameMode == GameMode.Game && !loading
                && s_CameraUpdateSystem != null && s_CameraUpdateSystem.activeViewer != null;
            if (!city)
            {
                s_CityActive = false;
                if (gameManager.gameMode == GameMode.MainMenu && !loading && !s_MenuLogged)
                {
                    s_MenuLogged = true;
                    return true;
                }
                return false;
            }
            if (!s_CityActive)
            {
                s_CityActive = true;
                s_BurstUntil = frame + CityBurst;
                s_NextPeriodic = frame + Period;
            }
            string controller = s_CameraUpdateSystem!.activeCameraController?.GetType().Name ?? "null";
            if (s_LastController != null && controller != s_LastController)
            {
                s_BurstUntil = Math.Max(s_BurstUntil, frame + ControllerBurst);
            }
            s_LastController = controller;
            if (frame >= s_NextPeriodic)
            {
                s_BurstUntil = Math.Max(s_BurstUntil, frame + PeriodicBurst);
                s_NextPeriodic = frame + Period;
            }
            return frame < s_BurstUntil;
        }

        private static string CameraLine(Camera? camera)
        {
            if (camera == null)
            {
                return "null";
            }
            Transform transform = camera.transform;
            Matrix4x4 p = camera.projectionMatrix;
            double cot = 1.0 / Math.Tan(camera.fieldOfView * Math.PI / 360.0);
            string mode = s_ProjectionMatrixMode?.GetValue(camera)?.ToString() ?? "n/a";
            return $"pos={Vec(transform.position)} euler={Vec(transform.eulerAngles, "F3")} "
                + $"fov={Num(camera.fieldOfView, "F4")} aspect={Num(camera.aspect, "F5")} "
                + $"near={Num(camera.nearClipPlane, "F4")} far={Num(camera.farClipPlane, "F1")} "
                + $"phys={camera.usePhysicalProperties} lensShift={Vec2(camera.lensShift)} "
                + $"P(m00={Num(p.m00, "F5")} m11={Num(p.m11, "F5")} m02={Num(p.m02, "F5")} m12={Num(p.m12, "F5")} "
                + $"m11-cot={Num(p.m11 - cot, "E2")}) mode={mode}";
        }

        private static void AppendFull(StringBuilder line, Camera? main)
        {
            if (main != null)
            {
                line.Append(" | gateFit=").Append(main.gateFit)
                    .Append(" sensor=").Append(Vec2(main.sensorSize))
                    .Append(" focal=").Append(Num(main.focalLength, "F3"))
                    .Append(" px=").Append(main.pixelWidth).Append('x').Append(main.pixelHeight)
                    .Append(" target=").Append(main.targetTexture == null ? "screen" : main.targetTexture.name);
            }

            GameManager gameManager = GameManager.instance;
            if (gameManager != null)
            {
                line.Append(" | game mode=").Append(gameManager.gameMode)
                    .Append(" loading=").Append(gameManager.isGameLoading)
                    .Append(" state=").Append(gameManager.state);
            }

            CameraUpdateSystem? cameraUpdate = s_CameraUpdateSystem;
            if (cameraUpdate != null)
            {
                IGameCameraController? controller = cameraUpdate.activeCameraController;
                line.Append(" | cus activeCamera=").Append(Describe(cameraUpdate.activeCamera))
                    .Append(" viewer=").Append(cameraUpdate.activeViewer != null)
                    .Append(" cachedPos=").Append(Vec((Vector3)cameraUpdate.position))
                    .Append(" controller=").Append(controller?.GetType().Name ?? "null");
                if (controller != null)
                {
                    line.Append(" pivot=").Append(Vec(controller.pivot))
                        .Append(" ctrlPos=").Append(Vec(controller.position))
                        .Append(" ctrlRot=").Append(Vec(controller.rotation, "F3"))
                        .Append(" zoom=").Append(Num(controller.zoom, "F3"));
                }
            }

            CinemachineCore core = CinemachineCore.Instance;
            int brains = core.BrainCount;
            line.Append(" | brains=").Append(brains);
            for (int i = 0; i < brains; i++)
            {
                CinemachineBrain brain = core.GetActiveBrain(i);
                LensSettings lens = brain.CurrentCameraState.Lens;
                line.Append(" [").Append(i).Append(' ').Append(brain.name)
                    .Append(" update=").Append(brain.m_UpdateMethod)
                    .Append(" out=").Append(Describe(brain.OutputCamera))
                    .Append(" vcam=").Append(brain.ActiveVirtualCamera?.Name ?? "null")
                    .Append(" blending=").Append(brain.IsBlending)
                    .Append(" lens(mode=").Append(lens.ModeOverride)
                    .Append(" fov=").Append(Num(lens.FieldOfView, "F4"))
                    .Append(" near=").Append(Num(lens.NearClipPlane, "F4"))
                    .Append(" far=").Append(Num(lens.FarClipPlane, "F1"))
                    .Append(" shift=").Append(Vec2(lens.LensShift))
                    .Append(")]");
            }
        }

        private static string Vec(Vector3 v, string format = "F3")
        {
            return $"({Num(v.x, format)},{Num(v.y, format)},{Num(v.z, format)})";
        }

        private static string Vec2(Vector2 v)
        {
            return $"({Num(v.x, "F4")},{Num(v.y, "F4")})";
        }

        private static string Num(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
