using System.Buffers.Binary;
using System.Text;
using MobileCap.Core.Capture;

namespace MobileCap.Core.Export;

/// <summary>
/// Puts an EXIF block on a JPEG: the tags Spirula reads for its focal prior and orientation
/// (src/sfm/core/Exif.h), the exposure, the time, and GPS only when asked for.
/// </summary>
public static class ExifWriter
{
    private const ushort TypeByte = 1, TypeAscii = 2, TypeShort = 3, TypeLong = 4, TypeRational = 5, TypeUndefined = 7;

    /// <summary>
    /// <paramref name="jpeg"/> with any EXIF block it had replaced by this shot's, placed right
    /// after the start-of-image marker.
    /// </summary>
    public static byte[] Write(ReadOnlySpan<byte> jpeg, CaptureShot shot, DeviceInfo device, bool includeGps)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new ArgumentException("not a JPEG", nameof(jpeg));

        var tiff = BuildTiff(shot, device, includeGps && shot.Location is not null);
        var app1Length = 2 + 6 + tiff.Length;
        if (app1Length > ushort.MaxValue) throw new InvalidOperationException("EXIF block too large");

        using var output = new MemoryStream(jpeg.Length + app1Length + 2);
        output.Write([0xFF, 0xD8, 0xFF, 0xE1, (byte)(app1Length >> 8), (byte)app1Length]);
        output.Write("Exif\0\0"u8);
        output.Write(tiff);

        int at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            bool isApp = marker is >= 0xE0 and <= 0xEF;
            if (!isApp && marker != 0xFE) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(at + 2));
            var segment = jpeg.Slice(at, Math.Min(2 + length, jpeg.Length - at));
            bool isExif = marker == 0xE1 && segment.Length >= 10 && segment.Slice(4, 6).SequenceEqual("Exif\0\0"u8);
            if (!isExif) output.Write(segment);
            at += segment.Length;
        }
        output.Write(jpeg.Slice(at));
        return output.ToArray();
    }

    private static byte[] BuildTiff(CaptureShot shot, DeviceInfo device, bool gps)
    {
        var k = shot.Intrinsics;
        var local = shot.TakenAt;
        var when = local.ToString("yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var offset = (local.Offset < TimeSpan.Zero ? "-" : "+") + local.Offset.ToString(@"hh\:mm");

        var ifd0 = new Ifd();
        ifd0.Ascii(0x010F, device.Make);
        ifd0.Ascii(0x0110, device.Model);
        ifd0.Short(0x0112, (ushort)shot.ExifOrientation);
        ifd0.Ascii(0x0131, device.Software);
        ifd0.Ascii(0x0132, when);

        var exif = new Ifd();
        if (shot.ExposureDuration > 0) exif.Rational(0x829A, ExposureFraction(shot.ExposureDuration));
        exif.Add(0x9000, TypeUndefined, 4, "0232"u8.ToArray());
        exif.Ascii(0x9003, when);
        exif.Ascii(0x9011, offset);
        if (device.FocalLengthMm is { } mm) exif.Rational(0x920A, Fraction(mm));
        exif.Long(0xA002, (uint)k.Width);
        exif.Long(0xA003, (uint)k.Height);
        // CIPA's definition, which Spirula inverts: focal over the image diagonal, times 43.27 mm.
        var diagonal = Math.Sqrt((double)k.Width * k.Width + (double)k.Height * k.Height);
        exif.Short(0xA405, (ushort)Math.Round((k.Fx + k.Fy) / 2 * 43.27 / diagonal));

        Ifd? gpsIfd = null;
        if (gps)
        {
            var at = shot.Location!;
            gpsIfd = new Ifd();
            gpsIfd.Add(0x0000, TypeByte, 4, [2, 3, 0, 0]);
            gpsIfd.Ascii(0x0001, at.Latitude >= 0 ? "N" : "S");
            gpsIfd.Rational(0x0002, Dms(at.Latitude));
            gpsIfd.Ascii(0x0003, at.Longitude >= 0 ? "E" : "W");
            gpsIfd.Rational(0x0004, Dms(at.Longitude));
            if (at.AltitudeMetres is { } alt)
            {
                gpsIfd.Add(0x0005, TypeByte, 1, [alt < 0 ? (byte)1 : (byte)0]);
                gpsIfd.Rational(0x0006, Fraction(Math.Abs(alt)));
            }
            if (at.HorizontalAccuracyMetres is { } err) gpsIfd.Rational(0x001F, Fraction(err));
        }

        // Pointers are 4-byte values, so their size is known before their targets are placed.
        ifd0.Long(0x8769, 0);
        if (gpsIfd is not null) ifd0.Long(0x8825, 0);
        uint exifAt = 8 + (uint)ifd0.Size;
        uint gpsAt = exifAt + (uint)exif.Size;
        ifd0.Long(0x8769, exifAt);
        if (gpsIfd is not null) ifd0.Long(0x8825, gpsAt);

        using var tiff = new MemoryStream();
        tiff.Write("II*\0"u8);
        tiff.Write(LittleEndian(8u));
        ifd0.WriteTo(tiff, 8);
        exif.WriteTo(tiff, exifAt);
        gpsIfd?.WriteTo(tiff, gpsAt);
        return tiff.ToArray();
    }

    /// <summary>1/N, as cameras write it, where that is within 1%.</summary>
    private static (uint, uint) ExposureFraction(double seconds)
    {
        var n = Math.Round(1 / seconds);
        return n >= 1 && Math.Abs(1 / n - seconds) <= 0.01 * seconds ? (1, (uint)n) : Fraction(seconds);
    }

    private static (uint, uint) Fraction(double value)
    {
        uint den = value < 1 ? 1_000_000u : 1000u;
        var num = (uint)Math.Round(value * den);
        var g = Gcd(num, den);
        return g == 0 ? (0, 1) : (num / g, den / g);
    }

    private static uint Gcd(uint a, uint b) => b == 0 ? a : Gcd(b, a % b);

    private static (uint, uint)[] Dms(double degrees)
    {
        degrees = Math.Abs(degrees);
        var d = Math.Floor(degrees);
        var m = Math.Floor((degrees - d) * 60);
        var s = ((degrees - d) * 60 - m) * 60;
        return [((uint)d, 1), ((uint)m, 1), ((uint)Math.Round(s * 10000), 10000)];
    }

    private static byte[] LittleEndian(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private sealed class Ifd
    {
        private readonly SortedDictionary<ushort, (ushort Type, uint Count, byte[] Value)> _entries = [];

        public int Size => 2 + 12 * _entries.Count + 4 + _entries.Values.Sum(e => e.Value.Length > 4 ? Even(e.Value.Length) : 0);

        public void Add(ushort tag, ushort type, uint count, byte[] value) => _entries[tag] = (type, count, value);

        public void Ascii(ushort tag, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\0");
            Add(tag, TypeAscii, (uint)bytes.Length, bytes);
        }

        public void Short(ushort tag, ushort value)
        {
            var b = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(b, value);
            Add(tag, TypeShort, 1, b);
        }

        public void Long(ushort tag, uint value) => Add(tag, TypeLong, 1, LittleEndian(value));

        public void Rational(ushort tag, params (uint Num, uint Den)[] values)
        {
            var b = new byte[8 * values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8 * i), values[i].Num);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8 * i + 4), values[i].Den);
            }
            Add(tag, TypeRational, (uint)values.Length, b);
        }

        /// <param name="at">This IFD's offset from the TIFF header; values follow the IFD.</param>
        public void WriteTo(Stream s, uint at)
        {
            var head = new byte[2 + 12 * _entries.Count + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(head, (ushort)_entries.Count);
            uint data = at + (uint)head.Length;
            var tail = new MemoryStream();
            int i = 2;
            foreach (var (tag, (type, count, value)) in _entries)
            {
                var e = head.AsSpan(i, 12);
                BinaryPrimitives.WriteUInt16LittleEndian(e, tag);
                BinaryPrimitives.WriteUInt16LittleEndian(e[2..], type);
                BinaryPrimitives.WriteUInt32LittleEndian(e[4..], count);
                if (value.Length <= 4)
                    value.CopyTo(e[8..]);
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(e[8..], data + (uint)tail.Length);
                    tail.Write(value);
                    if (value.Length % 2 == 1) tail.WriteByte(0);
                }
                i += 12;
            }
            s.Write(head);
            tail.WriteTo(s);
        }

        private static int Even(int n) => n + (n & 1);
    }
}
