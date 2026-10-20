using System;
using System.Reflection;
using Game.UI;
using HarmonyLib;
using HeadTrack.Systems;
using UnityEngine;

namespace HeadTrack.Rendering
{
    /// <summary>
    /// Save previews (quick save, autosave, the backup save on exit, the save
    /// screen) are rendered with camera.Render() wherever in the frame the save
    /// runs, so with the head-coupled camera in force. This renders them with
    /// the vanilla camera and puts the head-coupled one back afterwards, even
    /// if the capture throws.
    /// </summary>
    internal static class CapturePatch
    {
        public struct Applied
        {
            public bool Valid;
            public Vector3 Position;
            public float Far;
            public bool Explicit;
            public Matrix4x4 Projection;
            public bool Physical;
            public Vector2 SensorSize;
            public Camera.GateFitMode GateFit;
            public float FocalLength;
            public Vector2 LensShift;
            public Vector3 Offset;
        }

        public static bool Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(ScreenCaptureHelper), nameof(ScreenCaptureHelper.CaptureScreenshot));
            if (target == null)
            {
                return false;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(CapturePatch), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(CapturePatch), nameof(Finalizer)));
            return true;
        }

        /// <summary>Prefix of public static void ScreenCaptureHelper.CaptureScreenshot(Camera camera, RenderTexture destination, MenuHelpers.SaveGamePreviewSettings settings).</summary>
        public static void Prefix(Camera camera, out Applied __state)
        {
            __state = default;
            if (!Runtime.Dirty || camera == null || camera != Runtime.Snapshot.Camera)
            {
                return;
            }
            __state = new Applied
            {
                Valid = true,
                Position = camera.transform.position,
                Far = camera.farClipPlane,
                Explicit = Runtime.Snapshot.WroteMatrix,
                Projection = camera.projectionMatrix,
                Physical = camera.usePhysicalProperties,
                SensorSize = camera.sensorSize,
                GateFit = camera.gateFit,
                FocalLength = camera.focalLength,
                LensShift = camera.lensShift,
                Offset = HeadMotionBlurPatches.CurrentOffset,
            };
            // Vanilla for the capture; Dirty stays set, so next frame's Restore still runs.
            CameraRestorer.PutBack(Runtime.Snapshot);
            HeadMotionBlurPatches.CurrentOffset = Vector3.zero;
        }

        public static Exception? Finalizer(Exception? __exception, Camera camera, Applied __state)
        {
            if (__state.Valid && camera != null)
            {
                camera.transform.position = __state.Position;
                camera.farClipPlane = __state.Far;
                if (__state.Explicit)
                {
                    camera.usePhysicalProperties = false;
                    camera.projectionMatrix = __state.Projection;
                }
                else
                {
                    camera.usePhysicalProperties = __state.Physical;
                    camera.gateFit = __state.GateFit;
                    camera.sensorSize = __state.SensorSize;
                    camera.focalLength = __state.FocalLength;
                }
                camera.lensShift = __state.LensShift;
                HeadMotionBlurPatches.CurrentOffset = __state.Offset;
            }
            return __exception;
        }
    }
}
