using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class WindowProjectionTests
{
    private const double Ws = 597.0;
    private const double Hs = 336.0;

    /// <summary>Unity's Quaternion.Euler(pitch, yaw, 0) basis: pitch positive looks down.</summary>
    internal static CameraFrame Camera(double yawDeg, double pitchDeg, FrustumTangents tan, double near = 1.0, double far = 90000.0, Vec3? position = null)
    {
        double yaw = yawDeg * Math.PI / 180.0;
        double pitch = pitchDeg * Math.PI / 180.0;
        var forward = new Vec3(Math.Cos(pitch) * Math.Sin(yaw), -Math.Sin(pitch), Math.Cos(pitch) * Math.Cos(yaw));
        var right = new Vec3(Math.Cos(yaw), 0, -Math.Sin(yaw));
        Vec3 up = Vec3.Cross(forward, right);
        return new CameraFrame(position ?? new Vec3(700, 1696, -735), right, up, forward, near, far, tan);
    }

    /// <summary>The game's physical camera: vertical FOV 37.43 deg at 16:9 (m11 2.95639, m00 1.66297).</summary>
    internal static FrustumTangents GameTangents => new(-1 / 1.66297, 1 / 1.66297, -1 / 2.95639, 1 / 2.95639);

    internal static FrustumTangents Symmetric(double vfovDeg, double aspect = 3840.0 / 2160.0)
    {
        double t = Math.Tan(vfovDeg * Math.PI / 360.0);
        return new FrustumTangents(-t * aspect, t * aspect, -t, t);
    }

    internal static (double X, double Y) Ndc(double[] m, Vec3 cameraPosition, CameraFrame axes, Vec3 point)
    {
        Vec3 d = point - cameraPosition;
        double x = Vec3.Dot(d, axes.Right), y = Vec3.Dot(d, axes.Up), z = -Vec3.Dot(d, axes.Forward);
        double cx = m[0] * x + m[1] * y + m[2] * z + m[3];
        double cy = m[4] * x + m[5] * y + m[6] * z + m[7];
        double cw = m[12] * x + m[13] * y + m[14] * z + m[15];
        return (cx / cw, cy / cw);
    }

    private static OffAxisView ViewForEye(CameraFrame camera, double depth, Vec3 eye)
    {
        WindowGeometry window = WindowProjection.Window(camera, depth, Ws, Hs);
        return WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, eye));
    }

    public static IEnumerable<object[]> Frusta() => new[]
    {
        new object[] { GameTangents },
        new object[] { Symmetric(30) },
        new object[] { Symmetric(60) },
        new object[] { new FrustumTangents(-0.55, 0.70, -0.25, 0.42) },
    };

    [Theory]
    [MemberData(nameof(Frusta))]
    public void The_neutral_eye_reproduces_the_vanilla_camera_and_projection(FrustumTangents tan)
    {
        CameraFrame camera = Camera(16, 42.6, tan);
        WindowGeometry window = WindowProjection.Window(camera, 1120, Ws, Hs);

        OffAxisView view = WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, window.NeutralEye));

        Assert.True(view.Valid);
        Assert.True((view.Position - camera.Position).Length < 1e-9 * camera.Position.Length);
        double[] vanilla = WindowProjection.FrustumMatrix(tan.Left, tan.Right, tan.Bottom, tan.Top, camera.Near, camera.Far);
        double[] shifted = view.Matrix;
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(vanilla[i], shifted[i], 9);
        }
    }

    [Fact]
    public void The_neutral_eye_of_a_symmetric_frustum_is_on_the_axis_at_z0()
    {
        CameraFrame camera = Camera(0, 30, GameTangents);

        WindowGeometry window = WindowProjection.Window(camera, 500, Ws, Hs);

        Assert.Equal(0.0, window.NeutralEye.X, 9);
        Assert.Equal(0.0, window.NeutralEye.Y, 9);
        Assert.Equal(Hs / (2 / 2.95639), window.Z0, 6);
        Assert.Equal(window.Z0, window.NeutralEye.Z, 9);
    }

    [Theory]
    [MemberData(nameof(Frusta))]
    public void The_window_corners_stay_the_screen_corners_for_any_eye(FrustumTangents tan)
    {
        var rng = new Random(2);
        CameraFrame camera = Camera(-40, 25, tan);
        WindowGeometry window = WindowProjection.Window(camera, 300, Ws, Hs);
        for (int i = 0; i < 200; i++)
        {
            var eye = new Vec3((rng.NextDouble() - 0.5) * 1200, (rng.NextDouble() - 0.5) * 700, 200 + rng.NextDouble() * 900);
            OffAxisView view = WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, eye));
            foreach ((double a, double b) in new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) })
            {
                Vec3 corner = window.Centre + camera.Right * (a * window.HalfWidth) + camera.Up * (b * window.HalfHeight);
                (double x, double y) = Ndc(view.Matrix, view.Position, camera, corner);
                Assert.Equal(a, x, 9);
                Assert.Equal(b, y, 9);
            }
        }
    }

    [Fact]
    public void Points_on_the_window_plane_keep_their_place_on_screen()
    {
        var rng = new Random(4);
        CameraFrame camera = Camera(70, 60, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 800, Ws, Hs);
        double[] vanilla = WindowProjection.FrustumMatrix(GameTangents.Left, GameTangents.Right, GameTangents.Bottom, GameTangents.Top, camera.Near, camera.Far);
        OffAxisView view = ViewForEye(camera, 800, new Vec3(120, -60, 420));
        for (int i = 0; i < 100; i++)
        {
            Vec3 point = window.Centre + camera.Right * ((rng.NextDouble() - 0.5) * 2 * window.HalfWidth) + camera.Up * ((rng.NextDouble() - 0.5) * 2 * window.HalfHeight);
            (double x0, double y0) = Ndc(vanilla, camera.Position, camera, point);
            (double x1, double y1) = Ndc(view.Matrix, view.Position, camera, point);
            Assert.Equal(x0, x1, 9);
            Assert.Equal(y0, y1, 9);
        }
    }

    [Fact]
    public void Nearer_points_move_against_the_head_and_farther_ones_with_it()
    {
        CameraFrame camera = Camera(10, 40, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 500, Ws, Hs);
        Vec3 near = camera.Position + camera.Forward * 250;
        Vec3 far = camera.Position + camera.Forward * 1000;
        OffAxisView centre = ViewForEye(camera, 500, window.NeutralEye);
        OffAxisView right = ViewForEye(camera, 500, window.NeutralEye + new Vec3(100, 0, 0));

        Assert.True(Ndc(right.Matrix, right.Position, camera, near).X < Ndc(centre.Matrix, centre.Position, camera, near).X);
        Assert.True(Ndc(right.Matrix, right.Position, camera, far).X > Ndc(centre.Matrix, centre.Position, camera, far).X);
    }

    [Fact]
    public void A_position_raised_above_terrain_still_sees_exactly_the_window()
    {
        CameraFrame camera = Camera(0, 35, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 300, Ws, Hs);
        Vec3 wanted = WindowProjection.EyeToWorld(camera, window, new Vec3(0, -150, 500));

        foreach (double lift in new[] { 30.0, 120.0 })
        {
            OffAxisView view = WindowProjection.ViewFrom(camera, window, wanted + new Vec3(0, lift, 0));
            Vec3 corner = window.Centre + camera.Right * window.HalfWidth + camera.Up * window.HalfHeight;
            (double x, double y) = Ndc(view.Matrix, view.Position, camera, corner);
            Assert.Equal(1.0, x, 9);
            Assert.Equal(1.0, y, 9);
        }
    }

    [Fact]
    public void The_far_plane_grows_by_the_displacement()
    {
        CameraFrame camera = Camera(0, 35, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 300, Ws, Hs);
        Vec3 position = WindowProjection.EyeToWorld(camera, window, new Vec3(80, 20, 650));

        OffAxisView view = WindowProjection.ViewFrom(camera, window, position);

        Assert.Equal(camera.Far + (position - camera.Position).Length, view.Far, 6);
        Assert.Equal(camera.Near, view.Near);
    }

    [Fact]
    public void An_eye_at_or_behind_the_screen_gives_no_view()
    {
        CameraFrame camera = Camera(0, 35, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 300, Ws, Hs);

        OffAxisView atScreen = WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, new Vec3(0, 0, 0)));
        OffAxisView behind = WindowProjection.ViewFrom(camera, window, WindowProjection.EyeToWorld(camera, window, new Vec3(0, 0, -100)));

        Assert.False(atScreen.Valid);
        Assert.False(behind.Valid);
    }

    [Fact]
    public void A_non_finite_position_gives_no_view()
    {
        CameraFrame camera = Camera(0, 35, GameTangents);
        WindowGeometry window = WindowProjection.Window(camera, 300, Ws, Hs);

        OffAxisView view = WindowProjection.ViewFrom(camera, window, new Vec3(double.NaN, 0, 0));

        Assert.False(view.Valid);
    }

    [Fact]
    public void The_projection_matrix_is_unitys_frustum_convention()
    {
        double[] m = WindowProjection.FrustumMatrix(-0.4, 0.6, -0.2, 0.3, 1.0, 100.0);

        Assert.Equal(2.0 / 1.0, m[0], 12);
        Assert.Equal(0.2, m[2], 12);
        Assert.Equal(2.0 / 0.5, m[5], 12);
        Assert.Equal(0.2, m[6], 12);
        Assert.Equal(-101.0 / 99.0, m[10], 12);
        Assert.Equal(-200.0 / 99.0, m[11], 12);
        Assert.Equal(-1.0, m[14], 12);
        Assert.Equal(0.0, m[15], 12);
        Assert.Equal(0.0, m[3], 12);
        Assert.Equal(0.0, m[7], 12);
    }

    [Theory]
    [MemberData(nameof(Frusta))]
    public void Decomposing_a_projection_gives_back_its_tangents(FrustumTangents tan)
    {
        double[] m = WindowProjection.FrustumMatrix(tan.Left * 0.3, tan.Right * 0.3, tan.Bottom * 0.3, tan.Top * 0.3, 0.3, 5000);

        FrustumTangents back = FrustumTangents.FromProjection(m[0], m[2], m[5], m[6]);

        Assert.Equal(tan.Left, back.Left, 12);
        Assert.Equal(tan.Right, back.Right, 12);
        Assert.Equal(tan.Bottom, back.Bottom, 12);
        Assert.Equal(tan.Top, back.Top, 12);
    }
}
