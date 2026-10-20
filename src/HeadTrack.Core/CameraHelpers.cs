using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// A physical-camera lens that renders a given frustum: gate fit None,
    /// a sensor with the frustum's aspect, the focal length for its height
    /// and the lens shift for its offset (Unity measures the shift in sensor
    /// sizes). The alternative to an explicit projection matrix.
    /// </summary>
    public readonly struct PhysicalLens
    {
        public PhysicalLens(double sensorWidth, double sensorHeight, double focalLength, double shiftX, double shiftY)
        {
            SensorWidth = sensorWidth;
            SensorHeight = sensorHeight;
            FocalLength = focalLength;
            ShiftX = shiftX;
            ShiftY = shiftY;
        }

        public double SensorWidth { get; }

        public double SensorHeight { get; }

        public double FocalLength { get; }

        public double ShiftX { get; }

        public double ShiftY { get; }

        /// <summary>The field of view Unity reports for this lens: the unshifted sensor's.</summary>
        public double VerticalFieldOfViewDegrees => 2 * Math.Atan(0.5 * SensorHeight / FocalLength) * 180 / Math.PI;

        /// <summary>The frustum this lens renders with gate fit None.</summary>
        public FrustumTangents Tangents
        {
            get
            {
                double halfWidth = 0.5 * SensorWidth / FocalLength;
                double halfHeight = 0.5 * SensorHeight / FocalLength;
                double centreX = ShiftX * SensorWidth / FocalLength;
                double centreY = ShiftY * SensorHeight / FocalLength;
                return new FrustumTangents(centreX - halfWidth, centreX + halfWidth, centreY - halfHeight, centreY + halfHeight);
            }
        }

        public static PhysicalLens FromTangents(FrustumTangents view, double sensorHeight)
        {
            double width = view.Right - view.Left;
            double height = view.Top - view.Bottom;
            double sensorWidth = sensorHeight * width / height;
            double focal = sensorHeight / height;
            return new PhysicalLens(sensorWidth, sensorHeight, focal, (view.Right + view.Left) / (2 * width), (view.Top + view.Bottom) / (2 * height));
        }
    }

    /// <summary>The window depth for the gameplay camera.</summary>
    public static class WindowDepth
    {
        /// <summary>Below this horizontal share of the view direction, looking up, the depth fades into the zoom.</summary>
        private const double MinHorizontal = 0.1;

        /// <summary>
        /// The depth along the view of the point the camera aims at. Looking
        /// down, the pivot's own depth along the view: the window plane then
        /// passes through the pivot, so the aimed ground stays put on screen
        /// whatever the head does, above the game's lift over the orbit and
        /// after a collision push along the view alike. Looking up, where the
        /// game's floor can lift the camera until the pivot lies beside the
        /// view axis, the pivot's horizontal offset along the horizontal view
        /// direction over that direction's share of the view: the zoom on the
        /// orbit, positive at any lift. Both agree on a level view. Nearing
        /// straight up, where there is no horizontal direction to go by, it
        /// fades smoothly into the zoom. Clamped to [max(2·near, zoomMin),
        /// far/2]; anything not finite gives the lower bound.
        /// </summary>
        public static double AimPoint(Vec3 pivot, Vec3 position, Vec3 forward, double zoom, double near, double far, double zoomMin)
        {
            double horizontal2 = forward.X * forward.X + forward.Z * forward.Z;
            double depth = zoom;
            if (forward.Y < 0)
            {
                depth = Vec3.Dot(pivot - position, forward);
            }
            else if (horizontal2 > 0)
            {
                double dx = pivot.X - position.X;
                double dz = pivot.Z - position.Z;
                double along = (dx * forward.X + dz * forward.Z) / horizontal2;
                double weight = Ease.Smoothstep(Math.Sqrt(horizontal2) / MinHorizontal);
                depth = weight * along + (1 - weight) * zoom;
            }
            double min = Math.Max(2 * near, zoomMin);
            double max = 0.5 * far;
            if (double.IsNaN(depth))
            {
                return min;
            }
            return depth < min ? min : depth > max ? max : depth;
        }
    }

    /// <summary>How high above ground or water the shifted camera stays.</summary>
    public static class GroundFloor
    {
        /// <summary>CinemachineRestrictToTerrain's padding (m): the orbit and cinematic cameras stay this high above the surface.</summary>
        public const double OrbitPadding = 1.0;

        /// <summary>The gameplay camera's floor above the surface (m), as CameraController keeps it.</summary>
        public static double Gameplay(double zoom, double zoomMin) => 0.5 * zoomMin + 0.1 * Math.Max(0, zoom - zoomMin);

        /// <summary>
        /// The floor's height above the surface: what the game keeps
        /// (<paramref name="wanted"/>), never more than the vanilla camera
        /// itself keeps, so the floor never lifts the camera above where the
        /// game put it. Below zero where the vanilla camera sits under the
        /// sampled surface: the floor is then the vanilla camera's own height.
        /// </summary>
        public static double Offset(double wanted, double vanillaClearance) => Math.Min(wanted, vanillaClearance);
    }

    /// <summary>
    /// The effect's overall weight: rises and falls smoothly when a gate
    /// opens or closes (photo mode, map view, a tool drag), drops at once on
    /// a cut (loading, a camera change).
    /// </summary>
    public sealed class EffectRamp
    {
        /// <summary>A longer gap between updates counts as this long (s).</summary>
        private const double MaxFrameSeconds = 0.1;

        private double m_Progress;
        private double m_Last;
        private bool m_HasLast;

        public EffectRamp(double seconds)
        {
            Seconds = seconds;
        }

        /// <summary>How long a full ramp takes (s); 0 switches at once. A new value applies from the next update.</summary>
        public double Seconds { get; set; }

        public double Weight => Smoothstep(m_Progress);

        public double Update(double now, bool on)
        {
            double dt = m_HasLast ? Math.Min(MaxFrameSeconds, Math.Max(0, now - m_Last)) : 0;
            m_HasLast = true;
            m_Last = now;
            double step = Seconds > 0 ? dt / Seconds : 1;
            m_Progress = on ? Math.Min(1, m_Progress + step) : Math.Max(0, m_Progress - step);
            return Weight;
        }

        /// <summary>Drops the weight to zero at <paramref name="now"/>; it ramps in from there.</summary>
        public double Cut(double now)
        {
            m_Progress = 0;
            m_Last = now;
            m_HasLast = true;
            return 0;
        }

        private static double Smoothstep(double p) => p * p * (3 - 2 * p);
    }
}
