using HeadTrack.Rendering;
using UnityEngine;

namespace HeadTrack.Systems
{
    /// <summary>Puts back what Apply wrote, from the snapshot it took.</summary>
    internal static class CameraRestorer
    {
        public static void Restore()
        {
            if (!Runtime.Dirty)
            {
                return;
            }
            Runtime.Dirty = false;
            CameraSnapshot snapshot = Runtime.Snapshot;
            Camera camera = snapshot.Camera;
            if (camera == null)
            {
                return;
            }
            camera.transform.position = snapshot.Position;
            camera.farClipPlane = snapshot.Far;
            if (snapshot.WroteMatrix)
            {
                camera.ResetProjectionMatrix();
            }
            camera.usePhysicalProperties = snapshot.Physical;
            if (snapshot.Physical)
            {
                camera.sensorSize = snapshot.SensorSize;
                camera.gateFit = snapshot.GateFit;
                camera.focalLength = snapshot.FocalLength;
            }
            camera.lensShift = snapshot.LensShift;
            HeadMotionBlurPatches.CurrentOffset = Vector3.zero;
        }
    }
}
