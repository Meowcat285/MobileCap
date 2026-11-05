using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;
using MobileCap.Core.Scene;

namespace MobileCap.Core.Export;

/// <summary>Everything about a capture that is written beside its images.</summary>
public sealed record CaptureRecord(
    string Name,
    DateTimeOffset StartedAt,
    DeviceInfo Device,
    CaptureSettings Settings,
    IReadOnlyList<CaptureShot> Shots,
    IReadOnlyList<GapWarning> Gaps);

public static class SpirulaManifest
{
    public const string FileName = "spirula_manifest.yaml";

    public const string Command =
        "spirula sfm auto images -o ws --manifest spirula_manifest.yaml --metric-positions metric_positions.txt";

    /// <summary>
    /// One pinhole camera for every image (the mean of the shots' intrinsics, since focus is
    /// locked), and the images as one sequence in file-name order, which is capture order.
    /// Read by Spirula's src/sfm/core/Manifest.cpp.
    /// </summary>
    public static string Write(IReadOnlyList<CaptureShot> shots)
    {
        var sb = new StringBuilder();
        sb.Append("# Written by MobileCap. Run from this folder:\n# ").Append(Command).Append('\n');
        sb.Append("image_dir: images\n");
        sb.Append("camera_mode: single\n");
        if (shots.Count > 0)
        {
            double fx = shots.Average(s => s.Intrinsics.Fx), fy = shots.Average(s => s.Intrinsics.Fy);
            double cx = shots.Average(s => s.Intrinsics.Cx), cy = shots.Average(s => s.Intrinsics.Cy);
            sb.Append("cameras:\n");
            sb.Append("  - prefix: \"\"\n");
            sb.Append("    model: pinhole\n");
            sb.Append("    params: [").Append(string.Join(", ", new[] { fx, fy, cx, cy }.Select(Number))).Append("]\n");
        }
        sb.Append("sequences:\n");
        sb.Append("  - members: [\".\"]\n");
        return sb.ToString();
    }

    internal static string Number(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static string Number(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}

public static class MetricPositions
{
    public const string FileName = "metric_positions.txt";

    /// <summary>
    /// COLMAP's <c>image_name X Y Z</c>, which Spirula's <c>--metric-positions</c> reads: camera
    /// centres in the AR tracker's world, metres, y up.
    /// </summary>
    public static string Write(IReadOnlyList<CaptureShot> shots)
    {
        var sb = new StringBuilder("# image_name X Y Z: AR camera centres in metres, y up\n");
        foreach (var s in shots)
        {
            var p = s.Pose.Position;
            sb.Append(s.FileName).Append(' ').Append(SpirulaManifest.Number(p.X))
              .Append(' ').Append(SpirulaManifest.Number(p.Y))
              .Append(' ').Append(SpirulaManifest.Number(p.Z)).Append('\n');
        }
        return sb.ToString();
    }
}

public static class TransformsJson
{
    public const string FileName = "transforms.json";

    /// <summary>
    /// Nerfstudio's layout with per-frame pinhole intrinsics. ARKit's camera axes are
    /// Nerfstudio's (OpenGL), so poses only change from row-vector to column-vector layout.
    /// </summary>
    public static string Write(IReadOnlyList<CaptureShot> shots)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("camera_model", "PINHOLE");
            w.WriteStartArray("frames");
            foreach (var s in shots)
            {
                w.WriteStartObject();
                w.WriteString("file_path", "images/" + s.FileName);
                WriteIntrinsics(w, s.Intrinsics);
                w.WritePropertyName("transform_matrix");
                WriteColumnVectorMatrix(w, s.CameraToWorld);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    internal static void WriteIntrinsics(Utf8JsonWriter w, CameraIntrinsics k)
    {
        w.WriteNumber("fl_x", k.Fx);
        w.WriteNumber("fl_y", k.Fy);
        w.WriteNumber("cx", k.Cx);
        w.WriteNumber("cy", k.Cy);
        w.WriteNumber("w", k.Width);
        w.WriteNumber("h", k.Height);
    }

    /// <summary>Rows of the matrix as it multiplies a column vector: the transpose of <paramref name="m"/>.</summary>
    internal static void WriteColumnVectorMatrix(Utf8JsonWriter w, Matrix4x4 m)
    {
        var t = Matrix4x4.Transpose(m);
        w.WriteStartArray();
        for (int r = 0; r < 4; r++)
        {
            w.WriteStartArray();
            for (int c = 0; c < 4; c++) w.WriteNumberValue(t[r, c]);
            w.WriteEndArray();
        }
        w.WriteEndArray();
    }
}

public static class CaptureJson
{
    public const string FileName = "capture.json";

    /// <summary>MobileCap's own record of the capture, for review and for tuning thresholds.</summary>
    public static string Write(CaptureRecord capture)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", "mobilecap.capture");
            w.WriteNumber("version", 1);
            w.WriteString("name", capture.Name);
            w.WriteString("started_at", capture.StartedAt);
            w.WriteString("spirula_command", SpirulaManifest.Command);

            w.WriteStartObject("device");
            w.WriteString("make", capture.Device.Make);
            w.WriteString("model", capture.Device.Model);
            w.WriteString("software", capture.Device.Software);
            w.WriteEndObject();

            var st = capture.Settings;
            w.WriteStartObject("settings");
            w.WriteString("mode", st.Mode.ToString().ToLowerInvariant());
            w.WriteBoolean("gps_in_exif", st.WriteGpsToExif);
            w.WriteNumber("spacing_m", st.SceneSpacingMetres);
            w.WriteNumber("spacing_deg", st.SceneSpacingDegrees);
            w.WriteNumber("motion_blur_gate_px", st.MotionBlurGatePx);
            w.WriteNumber("max_blur_px", st.MaxBlurPx);
            w.WriteNumber("similar_bits", st.SimilarBits);
            w.WriteNumber("max_tries", st.MaxTries);
            w.WriteNumber("scene_depth_m", st.SceneDepthMetres);
            w.WriteNumber("min_overlap", st.MinOverlap);
            w.WriteEndObject();

            w.WriteStartArray("shots");
            foreach (var s in capture.Shots)
            {
                w.WriteStartObject();
                w.WriteString("file", "images/" + s.FileName);
                w.WriteString("taken_at", s.TakenAt);
                w.WriteNumber("ar_time_s", s.Timestamp.TotalSeconds);
                w.WriteNumber("exposure_s", s.ExposureDuration);
                w.WriteNumber("exif_orientation", s.ExifOrientation);
                if (s.PredictedBlurPx is { } predicted) w.WriteNumber("predicted_blur_px", predicted);
                w.WriteNumber("edge_width_px", s.Judgement.EdgeWidthPx);
                w.WriteNumber("blur_px", s.Judgement.BlurPx);
                w.WriteString("dhash", s.Judgement.Hash.ToString("x16", CultureInfo.InvariantCulture));
                w.WriteNumber("tries", s.Tries);
                w.WriteBoolean("flagged", s.Flagged);
                w.WriteStartObject("intrinsics");
                TransformsJson.WriteIntrinsics(w, s.Intrinsics);
                w.WriteEndObject();
                w.WritePropertyName("camera_to_world");
                TransformsJson.WriteColumnVectorMatrix(w, s.CameraToWorld);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("gaps");
            foreach (var g in capture.Gaps)
            {
                w.WriteStartObject();
                w.WriteString("file", "images/" + capture.Shots[g.Shot].FileName);
                w.WriteString("nearest", "images/" + capture.Shots[g.Nearest].FileName);
                w.WriteNumber("overlap", Math.Round(g.Overlap, 3));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
