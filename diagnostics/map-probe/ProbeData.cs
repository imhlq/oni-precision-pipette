using System;
using System.Globalization;
namespace OniU60MapProbe
{
    public static class ProbeData
    {
        public static int ChoiceIndex(int value, int length) => value >= 0 && value < length ? value : 0;
        public static string Number(float value) => ((double)value).ToString("R", CultureInfo.InvariantCulture);
        public static string Bits(float value) => "0x" + BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("X8", CultureInfo.InvariantCulture);
        public static string Label(string value) => (value ?? "").Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
        // Coordinate bounds before linearization prevent wrapping at left/right edges.
        public static int Cell(int x, int y, int width, int height) =>
            x < 0 || y < 0 || x >= width || y >= height ? -1 : checked(y * width + x);
        public static int Limit(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Max(1, Math.Min(600, n)) : 120;
    }
}
