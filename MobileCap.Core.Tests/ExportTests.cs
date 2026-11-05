using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;
using MobileCap.Core.Export;
using MobileCap.Core.Scene;
using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Tests;

public class ExportTests
{
    private static readonly DeviceInfo Device = new("Apple", "iPhone 15 Pro", "MobileCap 0.1");

    private static CaptureShot Shot(int index, Vector3 position, GeoLocation? location = null) => new(
        index,
        CaptureShot.FileNameFor(index),
        new DateTimeOffset(2026, 11, 5, 14, 32, 7, TimeSpan.FromHours(-5)),
        TimeSpan.FromSeconds(10 + index),
        TestImages.Pose(position, 90),
        TestImages.Intrinsics,
        1 / 120.0,
        6,
        0.4,
        new ShotJudgement(10, 1.5f, 0xABCDEFul, ShotVerdict.Sharp),
        1,
        false,
        location);

    [Fact]
    public void Exif_carries_the_tags_spirula_reads_and_no_gps_by_default()
    {
        var shot = Shot(0, Vector3.Zero, new GeoLocation(51.5, -0.125, 12));

        var tags = ReadExif(ExifWriter.Write(TestImages.Jpeg(), shot, Device, includeGps: false));

        Assert.Equal("Apple", tags[(0, 0x010F)]);
        Assert.Equal("iPhone 15 Pro", tags[(0, 0x0110)]);
        Assert.Equal(6u, tags[(0, 0x0112)]);
        Assert.Equal("2026:11:05 14:32:07", tags[(1, 0x9003)]);
        Assert.Equal("-05:00", tags[(1, 0x9011)]);
        Assert.Equal((1u, 120u), tags[(1, 0x829A)]);
        Assert.Equal(4032u, tags[(1, 0xA002)]);
        // 3000 px over a 5040 px diagonal is 25.76 mm in 35 mm terms.
        Assert.Equal(26u, tags[(1, 0xA405)]);
        Assert.False(tags.ContainsKey((0, 0x8825)));
        Assert.DoesNotContain(tags.Keys, k => k.Ifd == 2);
    }

    [Fact]
    public void Gps_is_written_only_when_asked_and_known()
    {
        var shot = Shot(0, Vector3.Zero, new GeoLocation(51.5, -0.125, -3.5));

        var tags = ReadExif(ExifWriter.Write(TestImages.Jpeg(), shot, Device, includeGps: true));

        Assert.Equal("N", tags[(2, 0x0001)]);
        Assert.Equal("W", tags[(2, 0x0003)]);
        Assert.Equal(new[] { (51u, 1u), (30u, 1u), (0u, 10000u) }, tags[(2, 0x0002)]);
        Assert.Equal(new[] { (0u, 1u), (7u, 1u), (300000u, 10000u) }, tags[(2, 0x0004)]);
        Assert.Equal((byte)1, tags[(2, 0x0005)]);
        Assert.Equal((7u, 2u), tags[(2, 0x0006)]);

        var noFix = ReadExif(ExifWriter.Write(TestImages.Jpeg(), Shot(0, Vector3.Zero), Device, includeGps: true));
        Assert.DoesNotContain(noFix.Keys, k => k.Ifd == 2);
    }

    [Fact]
    public void An_existing_exif_block_is_replaced_and_the_rest_kept()
    {
        var once = ExifWriter.Write(TestImages.Jpeg(), Shot(0, Vector3.Zero), Device, false);
        var twice = ExifWriter.Write(once, Shot(0, Vector3.Zero) with { ExifOrientation = 3 }, Device, false);

        Assert.Equal(1, CountExifBlocks(twice));
        Assert.Equal(3u, ReadExif(twice)[(0, 0x0112)]);
        Assert.Equal(TestImages.Jpeg()[2..], twice[(4 + BinaryPrimitives.ReadUInt16BigEndian(twice.AsSpan(4)))..]);
    }

    [Fact]
    public void Exif_orientation_follows_world_up_in_the_image()
    {
        CameraPose Rolled(double degrees) =>
            CameraPose.FromCameraToWorld(Matrix4x4.CreateRotationZ((float)(degrees * Math.PI / 180)));

        Assert.Equal(1, ExifOrientation.FromPose(Rolled(0)));
        // Phone upright: the sensor's long side runs up and down, its top edge to the left.
        Assert.Equal(6, ExifOrientation.FromPose(Rolled(-90)));
        Assert.Equal(8, ExifOrientation.FromPose(Rolled(90)));
        Assert.Equal(3, ExifOrientation.FromPose(Rolled(180)));
    }

    [Fact]
    public void Manifest_names_one_pinhole_camera_and_one_sequence()
    {
        var yaml = SpirulaManifest.Write([Shot(0, Vector3.Zero), Shot(1, Vector3.UnitX)]);

        Assert.Equal(
            "# Written by MobileCap. Run from this folder:\n" +
            "# spirula sfm auto images -o ws --manifest spirula_manifest.yaml --metric-positions metric_positions.txt\n" +
            "image_dir: images\n" +
            "camera_mode: single\n" +
            "cameras:\n" +
            "  - prefix: \"\"\n" +
            "    model: pinhole\n" +
            "    params: [3000, 3000, 2016, 1512]\n" +
            "sequences:\n" +
            "  - members: [\".\"]\n",
            yaml);
    }

    [Fact]
    public void Metric_positions_are_camera_centres_by_image_name()
    {
        var text = MetricPositions.Write([Shot(0, new Vector3(0.5f, 1.25f, -2)), Shot(1, Vector3.UnitX)]);

        Assert.Equal(
            "# image_name X Y Z: AR camera centres in metres, y up\n" +
            "000001.jpg 0.5 1.25 -2\n" +
            "000002.jpg 1 0 0\n",
            text);
    }

    [Fact]
    public void Transforms_json_holds_column_vector_poses_and_intrinsics()
    {
        var shot = Shot(0, new Vector3(1, 2, 3));

        using var doc = JsonDocument.Parse(TransformsJson.Write([shot]));

        var frame = doc.RootElement.GetProperty("frames")[0];
        Assert.Equal("PINHOLE", doc.RootElement.GetProperty("camera_model").GetString());
        Assert.Equal("images/000001.jpg", frame.GetProperty("file_path").GetString());
        Assert.Equal(3000, frame.GetProperty("fl_x").GetDouble());
        Assert.Equal(3024, frame.GetProperty("h").GetInt32());
        var m = frame.GetProperty("transform_matrix").EnumerateArray()
            .Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Assert.Equal([1, 2, 3, 1], m.Select(r => Math.Round(r[3], 6)));
        // Turned 90 degrees about y, the camera's x axis is world -z: the matrix's first column.
        Assert.Equal([0, 0, -1, 0], m.Select(r => Math.Round(r[0], 6)));
    }

    [Fact]
    public void Capture_json_records_every_shot_and_gap()
    {
        var shots = new[] { Shot(0, Vector3.Zero), Shot(1, new Vector3(3, 0, 0)) with { Flagged = true, Tries = 3 } };
        var record = new CaptureRecord("capture_2026-11-05_1432", shots[0].TakenAt, Device, new CaptureSettings(),
            shots, [new GapWarning(1, 0, 0.1)]);

        using var doc = JsonDocument.Parse(CaptureJson.Write(record));

        var root = doc.RootElement;
        Assert.Equal("mobilecap.capture", root.GetProperty("format").GetString());
        Assert.False(root.GetProperty("settings").GetProperty("gps_in_exif").GetBoolean());
        var second = root.GetProperty("shots")[1];
        Assert.Equal("images/000002.jpg", second.GetProperty("file").GetString());
        Assert.True(second.GetProperty("flagged").GetBoolean());
        Assert.Equal("0000000000abcdef", second.GetProperty("dhash").GetString());
        Assert.Equal("images/000001.jpg", root.GetProperty("gaps")[0].GetProperty("nearest").GetString());
    }

    [Fact]
    public void Capture_json_finds_gap_shots_by_index_when_one_is_missing()
    {
        var shots = new[] { Shot(1, Vector3.UnitX), Shot(2, new Vector3(3, 0, 0)) };
        var record = new CaptureRecord("capture_2026-11-05_1432", shots[0].TakenAt, Device, new CaptureSettings(),
            shots, [new GapWarning(2, 1, 0.1)]);

        using var doc = JsonDocument.Parse(CaptureJson.Write(record));

        var gap = doc.RootElement.GetProperty("gaps")[0];
        Assert.Equal("images/000003.jpg", gap.GetProperty("file").GetString());
        Assert.Equal("images/000002.jpg", gap.GetProperty("nearest").GetString());
    }

    [Fact]
    public void Folder_zips_under_its_own_name_with_jpegs_stored()
    {
        var root = Path.Combine(Path.GetTempPath(), "mobilecap-test-" + Guid.NewGuid().ToString("N"), "capture_2026-11-05_1432");
        try
        {
            var folder = new CaptureFolder(root);
            var shots = new[] { Shot(0, Vector3.Zero), Shot(1, Vector3.UnitX) };
            foreach (var s in shots) folder.WriteImage(s, TestImages.Jpeg(), Device, new CaptureSettings());
            folder.WriteSidecars(new CaptureRecord("capture_2026-11-05_1432", shots[0].TakenAt, Device,
                new CaptureSettings(), shots, []));

            using var zipped = new MemoryStream();
            folder.ZipTo(zipped);
            zipped.Position = 0;
            using var zip = new ZipArchive(zipped);

            Assert.Equal(
                [
                    "capture_2026-11-05_1432/capture.json",
                    "capture_2026-11-05_1432/images/000001.jpg",
                    "capture_2026-11-05_1432/images/000002.jpg",
                    "capture_2026-11-05_1432/metric_positions.txt",
                    "capture_2026-11-05_1432/spirula_manifest.yaml",
                    "capture_2026-11-05_1432/transforms.json",
                ],
                zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            var jpeg = zip.GetEntry("capture_2026-11-05_1432/images/000001.jpg")!;
            Assert.Equal(jpeg.Length, jpeg.CompressedLength);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
        }
    }

    [Fact]
    public void Capture_names_carry_date_and_time() =>
        Assert.Equal("capture_2026-11-05_1432", CaptureFolder.NameFor(new DateTimeOffset(2026, 11, 5, 14, 32, 59, TimeSpan.Zero)));

    private static int CountExifBlocks(byte[] jpeg)
    {
        int n = 0, at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF && jpeg[at + 1] is >= 0xE0 and <= 0xEF)
        {
            if (jpeg[at + 1] == 0xE1 && Encoding.ASCII.GetString(jpeg, at + 4, 4) == "Exif") n++;
            at += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
        }
        return n;
    }

    /// <summary>
    /// Every tag of IFD0 (0), the Exif IFD (1) and the GPS IFD (2): strings, single numbers,
    /// a rational as a pair, several rationals as an array.
    /// </summary>
    private static Dictionary<(int Ifd, int Tag), object> ReadExif(byte[] jpeg)
    {
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE1], jpeg[..4]);
        Assert.Equal("Exif\0\0", Encoding.ASCII.GetString(jpeg, 6, 6));
        var t = jpeg.AsSpan(12, BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4)) - 8).ToArray();
        Assert.Equal("II*\0", Encoding.ASCII.GetString(t, 0, 4));

        var tags = new Dictionary<(int, int), object>();
        void Walk(int ifd, int at)
        {
            int n = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(at));
            for (int i = 0; i < n; i++)
            {
                var e = t.AsSpan(at + 2 + 12 * i, 12);
                int tag = BinaryPrimitives.ReadUInt16LittleEndian(e), type = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
                int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
                int size = count * (type switch { 3 => 2, 4 => 4, 5 => 8, _ => 1 });
                var v = size <= 4 ? e.Slice(8, size).ToArray() : t.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(e[8..]), size).ToArray();
                (uint, uint) R(int k) => (BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(8 * k)), BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(8 * k + 4)));
                tags[(ifd, tag)] = type switch
                {
                    1 when count == 1 => v[0],
                    2 => Encoding.ASCII.GetString(v, 0, count - 1),
                    3 => (uint)BinaryPrimitives.ReadUInt16LittleEndian(v),
                    4 => BinaryPrimitives.ReadUInt32LittleEndian(v),
                    5 when count == 1 => R(0),
                    5 => Enumerable.Range(0, count).Select(R).ToArray(),
                    _ => v,
                };
            }
        }
        Walk(0, (int)BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(4)));
        Walk(1, (int)(uint)tags[(0, 0x8769)]);
        if (tags.TryGetValue((0, 0x8825), out var gps)) Walk(2, (int)(uint)gps);
        return tags;
    }
}
