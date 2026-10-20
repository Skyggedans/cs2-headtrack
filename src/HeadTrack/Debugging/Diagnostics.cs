using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using Game.Rendering;
using HeadTrack.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace HeadTrack.Debugging
{
    /// <summary>One line describing the camera this frame, for the log and the debug channel.</summary>
    internal static class Diagnostics
    {
        private static readonly PropertyInfo? s_ProjectionMode = typeof(Camera).GetProperty(
            "projectionMatrixMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static string Build(Camera? camera)
        {
            FrameResult frame = Runtime.LastFrame;
            HeadSignalOutput signal = frame.Signal;
            WindowGeometry window = frame.Window;
            OffAxisView view = frame.View;
            FrustumTangents vanilla = Runtime.LastRendered;
            var text = new StringBuilder(1024);
            void Add(string format, params object[] args) => text.Append(string.Format(CultureInfo.InvariantCulture, format, args)).Append(' ');

            Add("frame={0} apply={1} enabled={2} path={3} weight={4:F3} presence={5:F3} age={6:F3} period={7:F4} delay={8:F4} held={9}",
                Time.frameCount, frame.Apply, Runtime.Enabled, Runtime.Path, frame.Weight, signal.Presence, signal.Age, signal.Period, signal.Delay, signal.Held);
            Add("eye={0} neutral={1} z0={2:F2} scale={3:F5} depth={4:F2}", frame.Eye, window.NeutralEye, window.Z0, window.Scale, frame.Depth);
            Add("vanillaPos={0} vanillaTan=({1:F5},{2:F5},{3:F5},{4:F5})",
                Runtime.LastVanillaPosition.ToVec3(), vanilla.Left, vanilla.Right, vanilla.Bottom, vanilla.Top);
            Game.CameraController? gameplay = Runtime.World?.GetExistingSystemManaged<CameraUpdateSystem>()?.gamePlayController;
            if (gameplay != null)
            {
                Vector3 angles = gameplay.rotation;
                Add("pivot={0} zoom={1:F2} yaw={2:F2} pitch={3:F2}", gameplay.pivot.ToVec3(), gameplay.zoom, angles.y, angles.x);
            }
            if (frame.Apply)
            {
                FrustumTangents tan = view.Tangents;
                Add("pos={0} shift={1:F3} tan=({2:F5},{3:F5},{4:F5},{5:F5}) near={6:F3} far={7:F1} clamped={8}",
                    view.Position, (view.Position - Runtime.LastVanillaPosition.ToVec3()).Length, tan.Left, tan.Right, tan.Bottom, tan.Top, view.Near, view.Far, frame.Clamped);
            }
            Add("obstacle allowed={0:F2} lastClear={1:F2} frozen={2} restarts={3} settling={4}",
                Runtime.ObstacleAllowed, Runtime.LastObstacleClear, frame.Frozen, Runtime.Head.Signal.Restarts, signal.Settling);
            Add("lodFov vanilla={0:F4} engine={1:F4} pinned={2}", Runtime.VanillaLodFov, Runtime.ShiftedLodFov, Runtime.LodPinActive);
            if (camera != null)
            {
                Matrix4x4 p = camera.projectionMatrix;
                Add("camera pos={0} fov={1:F4} phys={2} mode={3} P=({4:F5},{5:F5},{6:F5},{7:F5}) px={8}x{9}",
                    camera.transform.position.ToVec3(), camera.fieldOfView, camera.usePhysicalProperties,
                    s_ProjectionMode?.GetValue(camera)?.ToString() ?? "n/a", p.m00, p.m11, p.m02, p.m12, camera.pixelWidth, camera.pixelHeight);
                if (frame.Apply)
                {
                    Add("rayError={0:E2} rad screenError={1:F3} px", RayError(camera, view), ScreenError(camera, frame));
                }
                UnityEngine.Rendering.VolumeStack? stack = HDCamera.GetOrCreate(camera).volumeStack;
                MotionBlur? blur = stack?.GetComponent<MotionBlur>();
                ScreenSpaceAmbientOcclusion? ao = stack?.GetComponent<ScreenSpaceAmbientOcclusion>();
                if (blur != null && ao != null)
                {
                    Add("motionBlur active={0} intensity={1:F2} cameraBlur={2} clamp={3} suppressHead={4} ao intensity={5:F2}",
                        blur.IsActive(), blur.intensity.value, blur.cameraMotionBlur.value, blur.specialCameraClampMode.value,
                        Runtime.SuppressHeadMotionBlur, ao.intensity.value);
                }
            }
            UdpPoseReceiver? receiver = Runtime.Receiver;
            Add("rx={0} rejected={1} repeats={2} lost={3} seen={4} applied={5} clamped={6}",
                receiver?.Received ?? 0, receiver?.Rejected ?? 0, Runtime.Ring.Duplicates, Runtime.Ring.Overwritten,
                Runtime.FramesSeen, Runtime.FramesApplied, Runtime.FramesClamped);
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// The largest angle between Unity's ScreenPointToRay and a ray built
        /// from the mod's own frustum, at the four corners and the centre:
        /// proves the camera holds the off-axis state and that Unity's picking
        /// honours it.
        /// </summary>
        private static double RayError(Camera camera, OffAxisView view)
        {
            double width = camera.pixelWidth, height = camera.pixelHeight;
            Transform t = camera.transform;
            double worst = 0;
            foreach ((double x, double y) in new[] { (0.0, 0.0), (width, 0.0), (0.0, height), (width, height), (width / 2, height / 2) })
            {
                double nx = view.Left + (view.Right - view.Left) * x / width;
                double ny = view.Bottom + (view.Top - view.Bottom) * y / height;
                Vec3 expected = (t.right.ToVec3() * nx + t.up.ToVec3() * ny + t.forward.ToVec3() * view.Near).Normalized();
                Vec3 actual = camera.ScreenPointToRay(new Vector3((float)x, (float)y, 0)).direction.ToVec3().Normalized();
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vec3.Dot(expected, actual))));
                worst = Math.Max(worst, angle);
            }
            return worst;
        }

        /// <summary>The largest distance in pixels between a window corner as Unity projects it and the screen corner.</summary>
        private static double ScreenError(Camera camera, FrameResult frame)
        {
            Transform t = camera.transform;
            WindowGeometry window = frame.Window;
            double worst = 0;
            foreach ((double a, double b) in new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) })
            {
                Vec3 corner = window.Centre + t.right.ToVec3() * (a * window.HalfWidth) + t.up.ToVec3() * (b * window.HalfHeight);
                Vector3 screen = camera.WorldToScreenPoint(corner.ToVector3());
                double ex = (a + 1) / 2 * camera.pixelWidth, ey = (b + 1) / 2 * camera.pixelHeight;
                worst = Math.Max(worst, Math.Sqrt((screen.x - ex) * (screen.x - ex) + (screen.y - ey) * (screen.y - ey)));
            }
            return worst;
        }
    }
}
