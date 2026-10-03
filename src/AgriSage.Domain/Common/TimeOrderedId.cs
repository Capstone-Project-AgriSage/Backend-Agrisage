using System.Security.Cryptography;

namespace AgriSage.Domain.Common;

// UUID v7 (RFC 9562) that also increases between calls inside one process, even within the same millisecond:
// 48-bit Unix milliseconds, then a 12-bit counter that counts up inside a millisecond, then random bits.
// Guid.CreateVersion7 randomizes everything after the millisecond, so rows created in one SaveChanges (same CreatedAt)
// would otherwise sort in random order; with this, ordering by (created_at, id) is the creation order.
// It stays compatible with PostgreSQL uuid and keeps primary-key indexes compact.
public static class TimeOrderedId
{
    private const int MaxSequence = 0xFFF;

    private static readonly object Gate = new();
    private static long _lastMilliseconds;
    private static int _sequence;

    public static Guid New()
    {
        long milliseconds;
        int sequence;

        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now > _lastMilliseconds)
            {
                _lastMilliseconds = now;
                // Start low enough to leave room to count up within the millisecond.
                _sequence = RandomNumberGenerator.GetInt32(0, 0x400);
            }
            else if (++_sequence > MaxSequence)
            {
                // More ids than one millisecond can number: borrow the next millisecond.
                _lastMilliseconds++;
                _sequence = 0;
            }

            milliseconds = _lastMilliseconds;
            sequence = _sequence;
        }

        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[8..]);
        for (var i = 0; i < 6; i++)
        {
            bytes[i] = (byte)(milliseconds >> (8 * (5 - i)));
        }

        bytes[6] = (byte)(0x70 | (sequence >> 8));
        bytes[7] = (byte)sequence;
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
