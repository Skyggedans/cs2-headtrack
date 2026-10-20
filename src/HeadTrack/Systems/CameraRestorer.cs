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
            PutBack(Runtime.Snapshot);
        }

        /// <summary>The vanilla camera from <paramref name="snapshot"/>, leaving <see cref="Runtime.Dirty"/> as it is.</summary>
        public static void PutBack(in CameraSnapshot snapshot)
        {
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
        }
    }
}
