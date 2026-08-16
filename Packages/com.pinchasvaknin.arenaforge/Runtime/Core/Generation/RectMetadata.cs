using System;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Writes and reads the rectangles the generator records in object metadata — doorways and
    /// spawn areas — as <c>minX,minZ,maxX,maxZ</c>.
    /// </summary>
    /// <remarks>
    /// Object metadata is a flat string dictionary, so a rectangle has to become text somewhere.
    /// Four round-trip floats separated by commas keep the document readable in a diff, and going
    /// through the invariant culture keeps a machine with a comma decimal separator from writing
    /// eight fields where there should be four.
    /// </remarks>
    public static class RectMetadata
    {
        /// <summary>Formats a rectangle for storage in metadata.</summary>
        public static string Format(Rect2 rect) => string.Concat(
            rect.MinX.ToString("R", CultureInfo.InvariantCulture), ",",
            rect.MinZ.ToString("R", CultureInfo.InvariantCulture), ",",
            rect.MaxX.ToString("R", CultureInfo.InvariantCulture), ",",
            rect.MaxZ.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>Reads a rectangle back. Returns false rather than throwing on malformed text.</summary>
        public static bool TryParse(string text, out Rect2 rect)
        {
            rect = Rect2.Zero;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(',');
            if (parts.Length != 4)
            {
                return false;
            }

            var values = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(
                        parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            rect = new Rect2(values[0], values[1], values[2], values[3]);
            return true;
        }

        /// <summary>Reads a rectangle back, naming the offending value if it cannot be read.</summary>
        /// <exception cref="FormatException">The text is not four round-trip floats.</exception>
        public static Rect2 Parse(string text)
        {
            if (!TryParse(text, out Rect2 rect))
            {
                throw new FormatException($"'{text}' is not a rectangle of the form minX,minZ,maxX,maxZ.");
            }

            return rect;
        }
    }
}
