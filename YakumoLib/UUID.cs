using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib
{
    public sealed class UUID : IEquatable<UUID>
    {

        public required uint a { get; init; }
        public required uint b { get; init; }
        public required uint c { get; init; }
        public required uint d { get; init; }

        public bool Equals(UUID? other)
        {
            if (other is null) return false;
            return a == other.a && b == other.b && c == other.c && d == other.d;
        }
        public override bool Equals(object? obj) => Equals(obj as UUID);
        public override int GetHashCode() => HashCode.Combine(a, b, c, d);

        // TODO: Read from and write to string.

        public string GetString()
        {
            return $"{a:x8}-{b:x8}-{c:x8}-{d:x8}";
        }


    }

    public static class UUIDParser
    {
        public static UUID Parse(string str)
        {
            if (!TryParse(str, out var id))
                throw new FormatException($"'{str}' is not a valid UUID.");
            return id;
        }

        public static bool TryParse(string str, out UUID uuid)
        {
            uuid = default!;

            Span<Range> parts = stackalloc Range[4];
            int count = str.AsSpan().Split(parts, '-');

            if (count != 4)
                return false;

            Span<uint> values = stackalloc uint[4];
            for (int i = 0; i < 4; i++)
            {
                var token = str.AsSpan(parts[i]);
                if (!uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }

            uuid = new UUID
            {
                a = unchecked((uint)values[0]),
                b = unchecked((uint)values[1]),
                c = unchecked((uint)values[2]),
                d = unchecked((uint)values[3])
            };
            return true;
        }
    }


}
