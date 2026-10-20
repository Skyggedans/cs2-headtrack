using System;
using Game;
using Game.Rendering;
using HeadTrack.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace HeadTrack.Systems
{
    /// <summary>Per-mode window depth and ground clearance.</summary>
    internal static class ModeDepth
    {
        /// <summary>
        /// The depth (m, along the view) of the point the live camera aims at,
        /// or NaN to keep the last one.
        /// </summary>
        public static double Aim(ViewMode mode, CameraUpdateSystem cameras, Camera camera, in CameraFrame frame)
        {
            CameraController? gameplay = cameras.gamePlayController;
            switch (mode)
            {
                case ViewMode.Gameplay when gameplay != null:
                    return WindowDepth.AimPoint(gameplay.pivot.ToVec3(), frame.Position, frame.Forward, gameplay.zoom,
                        frame.Near, frame.Far, gameplay.zoomRange.min);
                case ViewMode.Follow:
                case ViewMode.PhotoOrbit:
                {
                    // The orbit vcam's LookAt and Follow are both its anchor, which is the pivot: the aimed point.
                    IGameCameraController? orbit = cameras.activeCameraController;
                    return orbit != null
                        ? Bound(Vec3.Dot(orbit.pivot.ToVec3() - frame.Position, frame.Forward), frame, gameplay)
                        : double.NaN;
                }
                case ViewMode.PhotoFree:
                    return Bound(FocusDepth(cameras, camera), frame, gameplay);
                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// The free photo camera has no aim point (its pivot is its position):
        /// the in-focus distance of a physical depth of field, else the game's
        /// own smoothed focus ray.
        /// </summary>
        private static double FocusDepth(CameraUpdateSystem cameras, Camera camera)
        {
            DepthOfField? dof = HDCamera.GetOrCreate(camera).volumeStack?.GetComponent<DepthOfField>();
            if (dof != null && dof.IsActive() && dof.focusMode.value == DepthOfFieldMode.UsePhysicalCamera
                && dof.focusDistanceMode.value == FocusDistanceMode.Volume)
            {
                return dof.focusDistance.value;
            }
            Viewer? viewer = cameras.activeViewer;
            return viewer != null ? viewer.viewerDistances.focus : double.NaN;
        }

        private static double Bound(double depth, in CameraFrame frame, CameraController? gameplay)
        {
            if (double.IsNaN(depth) || double.IsInfinity(depth) || depth <= 0)
            {
                return double.NaN;
            }
            double min = Math.Max(2 * frame.Near, 2.0);
            double max = Math.Min(0.5 * frame.Far, gameplay != null ? gameplay.zoomRange.max : 10000.0);
            return depth < min ? min : depth > max ? max : depth;
        }

        /// <summary>
        /// How high (m) above ground or water the shifted camera stays: the
        /// game's own lift for the gameplay camera, CinemachineRestrictToTerrain's
        /// 1 m padding for the orbit and cinematic cameras; never more than the
        /// vanilla camera itself keeps, so a ramp across modes never lifts it.
        /// </summary>
        public static double FloorOffset(ViewMode mode, CameraController? gameplay, double vanillaClearance)
        {
            double wanted = 1.0;
            if (mode == ViewMode.Gameplay && gameplay != null)
            {
                double zoomMin = gameplay.zoomRange.min;
                wanted = 0.5 * zoomMin + 0.1 * Math.Max(0, gameplay.zoom - zoomMin);
            }
            return Math.Max(0.5, Math.Min(wanted, vanillaClearance));
        }
    }
}
