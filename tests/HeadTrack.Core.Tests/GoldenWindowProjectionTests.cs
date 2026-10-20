using System.Text.Json;
using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

/// <summary>
/// Checks WindowProjection against vectors from tools/golden/window_projection.py,
/// an independent numpy implementation of Kooima's generalized perspective
/// projection (written without reading this code).
/// </summary>
public class GoldenWindowProjectionTests
{
    private static readonly Lazy<JsonElement[]> s_Cases = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Golden", "window_projection.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToArray();
    });

    public static IEnumerable<object[]> Cases() => Enumerable.Range(0, s_Cases.Value.Length).Select(i => new object[] { i });

    private static Vec3 V(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());

    private static void Close(double expected, double actual, double tolerance, string what)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected:R}, got {actual:R} (tolerance {tolerance:E1})");
    }

    private static void Close(Vec3 expected, Vec3 actual, double tolerance, string what)
    {
        Close(expected.X, actual.X, tolerance, what + ".x");
        Close(expected.Y, actual.Y, tolerance, what + ".y");
        Close(expected.Z, actual.Z, tolerance, what + ".z");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_the_independent_reference(int index)
    {
        JsonElement c = s_Cases.Value[index];
        string name = c.GetProperty("name").GetString()!;
        JsonElement cam = c.GetProperty("camera");
        JsonElement tanJson = cam.GetProperty("tan");
        var tan = new FrustumTangents(tanJson.GetProperty("left").GetDouble(), tanJson.GetProperty("right").GetDouble(),
            tanJson.GetProperty("bottom").GetDouble(), tanJson.GetProperty("top").GetDouble());
        var camera = new CameraFrame(V(cam.GetProperty("position")), V(cam.GetProperty("right")), V(cam.GetProperty("up")),
            V(cam.GetProperty("forward")), cam.GetProperty("near").GetDouble(), cam.GetProperty("far").GetDouble(), tan);
        JsonElement screen = c.GetProperty("screen_mm");
        double depth = c.GetProperty("depth").GetDouble();
        JsonElement expected = c.GetProperty("expected");

        WindowGeometry window = WindowProjection.Window(camera, depth, screen[0].GetDouble(), screen[1].GetDouble());
        Vec3 position = c.GetProperty("eye_mm").ValueKind == JsonValueKind.Null
            ? V(c.GetProperty("camera_override"))
            : WindowProjection.EyeToWorld(camera, window, V(c.GetProperty("eye_mm")));
        OffAxisView view = WindowProjection.ViewFrom(camera, window, position);

        double worldTolerance = 1e-9 * Math.Max(1.0, Math.Max(camera.Position.Length, depth));
        Assert.True(view.Valid, $"{name}: no view");
        Close(V(expected.GetProperty("window_centre")), window.Centre, worldTolerance, $"{name} window centre");
        Close(expected.GetProperty("scale_m_per_mm").GetDouble(), window.Scale, 1e-12 * Math.Max(1, window.Scale), $"{name} scale");
        Close(expected.GetProperty("z0_mm").GetDouble(), window.Z0, 1e-9, $"{name} z0");
        Close(V(expected.GetProperty("neutral_eye_mm")), window.NeutralEye, 1e-8, $"{name} neutral eye");
        Close(V(expected.GetProperty("position")), view.Position, worldTolerance, $"{name} position");
        Close(expected.GetProperty("left").GetDouble(), view.Left, 1e-9, $"{name} left");
        Close(expected.GetProperty("right").GetDouble(), view.Right, 1e-9, $"{name} right");
        Close(expected.GetProperty("bottom").GetDouble(), view.Bottom, 1e-9, $"{name} bottom");
        Close(expected.GetProperty("top").GetDouble(), view.Top, 1e-9, $"{name} top");
        Close(expected.GetProperty("far").GetDouble(), view.Far, 1e-9 * view.Far, $"{name} far");
        double[] matrix = view.Matrix;
        JsonElement expectedMatrix = expected.GetProperty("matrix");
        for (int i = 0; i < 16; i++)
        {
            Close(expectedMatrix[i].GetDouble(), matrix[i], 1e-8, $"{name} matrix[{i}]");
        }

        double[] vanilla = WindowProjection.FrustumMatrix(camera.Near * tan.Left, camera.Near * tan.Right,
            camera.Near * tan.Bottom, camera.Near * tan.Top, camera.Near, camera.Far);
        foreach (JsonElement probe in expected.GetProperty("probes").EnumerateArray())
        {
            string label = $"{name} probe {probe.GetProperty("label").GetString()}";
            Vec3 world = V(probe.GetProperty("world"));
            (double vx, double vy) = WindowProjectionTests.Ndc(vanilla, camera.Position, camera, world);
            (double sx, double sy) = WindowProjectionTests.Ndc(matrix, view.Position, camera, world);
            JsonElement ndcVanilla = probe.GetProperty("ndc_vanilla");
            JsonElement ndcShifted = probe.GetProperty("ndc_shifted");
            Close(ndcVanilla[0].GetDouble(), vx, 1e-7, label + " vanilla x");
            Close(ndcVanilla[1].GetDouble(), vy, 1e-7, label + " vanilla y");
            Close(ndcShifted[0].GetDouble(), sx, 1e-7, label + " shifted x");
            Close(ndcShifted[1].GetDouble(), sy, 1e-7, label + " shifted y");
        }
    }
}
