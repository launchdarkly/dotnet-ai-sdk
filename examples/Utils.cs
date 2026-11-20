using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk;

namespace LaunchDarkly.AI.Examples
{
    /// <summary>
    /// Helpers the scenarios share.
    /// </summary>
    internal static class Utils
    {
        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Returns a new user context with a random key, so each run is a new context.</summary>
        internal static Context NewContext() => Context.New("example-user-" + RandomHex(4));

        /// <summary>Prints <paramref name="value"/> as indented JSON on stdout.</summary>
        internal static void PrintJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value, _json));

        /// <summary>
        /// Writes <paramref name="value"/> as indented JSON to a new file under <c>output/</c> in the
        /// current directory, and returns the file's path.
        /// </summary>
        internal static string WriteOutput(object value)
        {
            Directory.CreateDirectory("output");
            var name = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss-fffffff'Z'", CultureInfo.InvariantCulture) + ".json";
            var path = Path.Combine("output", name);
            File.WriteAllText(path, JsonSerializer.Serialize(value, _json));
            Console.Error.WriteLine($"[examples] wrote {path}");
            return path;
        }

        /// <summary>
        /// Returns a base64 PNG of <paramref name="size"/> by <paramref name="size"/> pixels in one
        /// colour, so an example with an image needs no binary fixture.
        /// </summary>
        internal static string SolidColorPngBase64(byte red, byte green, byte blue, int size = 8)
        {
            // Each row is a filter byte (0, none) followed by the RGB pixels.
            var rows = new byte[size * ((3 * size) + 1)];
            for (var y = 0; y < size; y++)
            {
                var row = y * ((3 * size) + 1);
                for (var x = 0; x < size; x++)
                {
                    rows[row + 1 + (3 * x)] = red;
                    rows[row + 2 + (3 * x)] = green;
                    rows[row + 3 + (3 * x)] = blue;
                }
            }

            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, size);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
            header[8] = 8; // bit depth
            header[9] = 2; // colour type: RGB

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(rows, 0, rows.Length);
            }

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return Convert.ToBase64String(png.ToArray());
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            var typeAndData = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, typeAndData, 0);
            data.CopyTo(typeAndData, 4);

            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));

            png.Write(length, 0, 4);
            png.Write(typeAndData, 0, typeAndData.Length);
            png.Write(crc, 0, 4);
        }

        private static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return ~crc;
        }

        private static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    }
}
