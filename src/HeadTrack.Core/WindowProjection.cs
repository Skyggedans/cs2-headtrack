using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// A frustum's near-plane extents divided by the near distance: the
    /// tangents of its four half-angles, negative to the left and below.
    /// </summary>
    public readonly struct FrustumTangents
    {
        public FrustumTangents(double left, double right, double bottom, double top)
        {
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        public double Left { get; }

        public double Right { get; }

        public double Bottom { get; }

        public double Top { get; }

        /// <summary>
        /// The tangents of an OpenGL-style (Unity) perspective matrix, from
        /// its m00, m02, m11 and m12: whatever made it (field of view,
        /// physical gate fit, lens shift), this is the frustum it renders.
        /// </summary>
        public static FrustumTangents FromProjection(double m00, double m02, double m11, double m12) =>
            new FrustumTangents((m02 - 1) / m00, (1 + m02) / m00, (m12 - 1) / m11, (1 + m12) / m11);
    }

    /// <summary>The game's camera as it stands after its own update: Unity axes, metres.</summary>
    public readonly struct CameraFrame
    {
        public CameraFrame(Vec3 position, Vec3 right, Vec3 up, Vec3 forward, double near, double far, FrustumTangents tangents)
        {
            Position = position;
            Right = right;
            Up = up;
            Forward = forward;
            Near = near;
            Far = far;
            Tangents = tangents;
        }

        public Vec3 Position { get; }

        public Vec3 Right { get; }

        public Vec3 Up { get; }

        public Vec3 Forward { get; }

        public double Near { get; }

        public double Far { get; }

        public FrustumTangents Tangents { get; }
    }

    /// <summary>
    /// The window: the cross-section of the game's own frustum at the window
    /// depth, onto which the physical screen is mapped.
    /// </summary>
    public readonly struct WindowGeometry
    {
        public WindowGeometry(Vec3 centre, double halfWidth, double halfHeight, double scale, double z0, Vec3 neutralEye)
        {
            Centre = centre;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            Scale = scale;
            Z0 = z0;
            NeutralEye = neutralEye;
        }

        /// <summary>The window's centre in the world (W0).</summary>
        public Vec3 Centre { get; }

        public double HalfWidth { get; }

        public double HalfHeight { get; }

        /// <summary>World metres per screen millimetre (s).</summary>
        public double Scale { get; }

        /// <summary>The viewing distance at which the game's field of view fits the screen (mm).</summary>
        public double Z0 { get; }

        /// <summary>The eye (mm, screen frame) from which the window is seen exactly as the game shows it.</summary>
        public Vec3 NeutralEye { get; }
    }

    /// <summary>The head-coupled camera: where it stands and its off-axis frustum through the window.</summary>
    public readonly struct OffAxisView
    {
        public OffAxisView(bool valid, Vec3 position, double left, double right, double bottom, double top, double near, double far)
        {
            Valid = valid;
            Position = position;
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
            Near = near;
            Far = far;
        }

        /// <summary>False when the frustum cannot be built (eye at or behind the screen, non-finite input).</summary>
        public bool Valid { get; }

        public Vec3 Position { get; }

        /// <summary>Near-plane extents along the camera's right and up axes (metres at <see cref="Near"/>).</summary>
        public double Left { get; }

        public double Right { get; }

        public double Bottom { get; }

        public double Top { get; }

        public double Near { get; }

        public double Far { get; }

        public FrustumTangents Tangents => new FrustumTangents(Left / Near, Right / Near, Bottom / Near, Top / Near);

        /// <summary>Unity's Matrix4x4.Frustum of this view, row-major.</summary>
        public double[] Matrix => WindowProjection.FrustumMatrix(Left, Right, Bottom, Top, Near, Far);
    }

    /// <summary>
    /// Generalized perspective projection (Kooima 2009) for a screen that acts
    /// as a window into the game. The window is perpendicular to the game
    /// camera's forward axis, so the head-coupled camera keeps the game's
    /// rotation and only moves, and its frustum is the game's frustum made
    /// off-centre through the same rectangle.
    /// </summary>
    public static class WindowProjection
    {
        /// <summary>An eye closer to the screen plane than this fraction of the window depth gives no view.</summary>
        private const double MinDepthFraction = 1e-6;

        /// <summary>
        /// Frustum extents beyond this tangent (about 84 degrees) are refused as
        /// degenerate. Large but sane offsets are the mod's to limit (it shrinks
        /// the eye's offset); this only guards the matrix.
        /// </summary>
        private const double MaxTangent = 10.0;

        /// <summary>
        /// The window at <paramref name="depth"/> metres in front of the
        /// camera, for a <paramref name="screenWidthMm"/> × <paramref name="screenHeightMm"/> screen.
        /// The vertical extent sets the scale; the window's aspect is the
        /// game frustum's, which is the screen's when the game fills it.
        /// </summary>
        public static WindowGeometry Window(CameraFrame camera, double depth, double screenWidthMm, double screenHeightMm)
        {
            FrustumTangents tan = camera.Tangents;
            double centreX = 0.5 * (tan.Right + tan.Left);
            double centreY = 0.5 * (tan.Top + tan.Bottom);
            Vec3 centre = camera.Position + camera.Forward * depth + (camera.Right * centreX + camera.Up * centreY) * depth;
            double halfWidth = 0.5 * depth * (tan.Right - tan.Left);
            double halfHeight = 0.5 * depth * (tan.Top - tan.Bottom);
            double scale = 2.0 * halfHeight / screenHeightMm;
            double z0 = screenHeightMm / (tan.Top - tan.Bottom);
            var neutral = new Vec3(-centreX * z0, -centreY * z0, z0);
            return new WindowGeometry(centre, halfWidth, halfHeight, scale, z0, neutral);
        }

        /// <summary>
        /// Where an eye at <paramref name="eyeMm"/> (screen frame: +x right,
        /// +y up, +z towards the viewer) stands in the world.
        /// </summary>
        public static Vec3 EyeToWorld(CameraFrame camera, WindowGeometry window, Vec3 eyeMm) =>
            window.Centre + (camera.Right * eyeMm.X + camera.Up * eyeMm.Y - camera.Forward * eyeMm.Z) * window.Scale;

        /// <summary>
        /// The frustum from <paramref name="position"/> through the window,
        /// with the camera's rotation. Always computed from the position the
        /// camera will really take, so a position moved by a clamp (terrain,
        /// buildings) still frames exactly the window.
        /// </summary>
        public static OffAxisView ViewFrom(CameraFrame camera, WindowGeometry window, Vec3 position)
        {
            double near = camera.Near;
            double far = camera.Far + (position - camera.Position).Length;
            if (!position.IsFinite || !(near > 0) || !(far > near))
            {
                return Invalid(position, near, far);
            }
            Vec3 toCentre = window.Centre - position;
            double depth = Vec3.Dot(toCentre, camera.Forward);
            double minDepth = MinDepthFraction * Math.Max(1.0, (window.Centre - camera.Position).Length);
            if (!(depth > minDepth))
            {
                return Invalid(position, near, far);
            }
            double centreX = Vec3.Dot(toCentre, camera.Right);
            double centreY = Vec3.Dot(toCentre, camera.Up);
            double left = near * (centreX - window.HalfWidth) / depth;
            double right = near * (centreX + window.HalfWidth) / depth;
            double bottom = near * (centreY - window.HalfHeight) / depth;
            double top = near * (centreY + window.HalfHeight) / depth;
            bool valid = right > left && top > bottom
                && Math.Max(Math.Abs(left), Math.Abs(right)) <= MaxTangent * near
                && Math.Max(Math.Abs(bottom), Math.Abs(top)) <= MaxTangent * near;
            return new OffAxisView(valid, position, left, right, bottom, top, near, far);
        }

        /// <summary>Unity's Matrix4x4.Frustum (OpenGL convention, view space looking down -z), row-major.</summary>
        public static double[] FrustumMatrix(double left, double right, double bottom, double top, double near, double far) => new[]
        {
            2 * near / (right - left), 0, (right + left) / (right - left), 0,
            0, 2 * near / (top - bottom), (top + bottom) / (top - bottom), 0,
            0, 0, -(far + near) / (far - near), -2 * far * near / (far - near),
            0, 0, -1, 0,
        };

        private static OffAxisView Invalid(Vec3 position, double near, double far) =>
            new OffAxisView(false, position, 0, 0, 0, 0, near, far);
    }
}
