using System.Globalization;
using System.IO.Compression;
using MobileCap.Core.Capture;

namespace MobileCap.Core.Export;

/// <summary>
/// A capture on disk, laid out as it is shared: images/ plus the sidecars, which Spirula's GUI
/// opens as a photo folder and its CLI reads with the manifest and metric positions.
/// </summary>
public sealed class CaptureFolder(string root)
{
    public string Root { get; } = root;

    public string ImagesDirectory => Path.Combine(Root, "images");

    public static string NameFor(DateTimeOffset startedAt) =>
        "capture_" + startedAt.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);

    /// <summary>Writes the still with this shot's EXIF; GPS only where the settings allow it.</summary>
    public string WriteImage(CaptureShot shot, ReadOnlySpan<byte> jpeg, DeviceInfo device, CaptureSettings settings)
    {
        Directory.CreateDirectory(ImagesDirectory);
        var path = Path.Combine(ImagesDirectory, shot.FileName);
        File.WriteAllBytes(path, ExifWriter.Write(jpeg, shot, device, settings.WriteGpsToExif));
        return path;
    }

    public void DeleteImage(CaptureShot shot) => File.Delete(Path.Combine(ImagesDirectory, shot.FileName));

    public void WriteSidecars(CaptureRecord capture)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, SpirulaManifest.FileName), SpirulaManifest.Write(capture.Shots));
        File.WriteAllText(Path.Combine(Root, MetricPositions.FileName), MetricPositions.Write(capture.Shots));
        File.WriteAllText(Path.Combine(Root, TransformsJson.FileName), TransformsJson.Write(capture.Shots));
        File.WriteAllText(Path.Combine(Root, CaptureJson.FileName), CaptureJson.Write(capture));
    }

    /// <summary>
    /// The folder as a .zip whose entries sit under one folder named after it. JPEGs are stored
    /// rather than deflated: they do not shrink, and storing them is far quicker on a phone.
    /// </summary>
    public void ZipTo(Stream output)
    {
        var top = Path.GetFileName(Path.TrimEndingDirectorySeparator(Root));
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Root, file).Replace(Path.DirectorySeparatorChar, '/');
            var level = file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            zip.CreateEntryFromFile(file, top + "/" + relative, level);
        }
    }
}
