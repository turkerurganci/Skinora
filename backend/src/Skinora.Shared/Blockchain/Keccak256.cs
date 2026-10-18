using System.Buffers.Binary;
using System.Numerics;

namespace Skinora.Shared.Blockchain;

/// <summary>
/// Keccak-256 as Ethereum uses it — the original Keccak padding (<c>0x01</c>),
/// <b>not</b> the FIPS-202 SHA3-256 padding (<c>0x06</c>) that
/// <c>System.Security.Cryptography.SHA3_256</c> implements. The two produce
/// different digests for the same input, so the framework class cannot be
/// substituted.
/// </summary>
/// <remarks>
/// Used for the EIP-55 address checksum only (<see cref="EvmAddress"/>): no key
/// material passes through it and a wrong digest can only reject or accept an
/// address checksum. The permutation follows the reference "tiny_sha3" layout
/// (Saarinen); the unit tests pin it to digests produced by an independent
/// implementation (ethers v6 <c>keccak256</c>), including inputs that span
/// more than one 136-byte block.
/// </remarks>
public static class Keccak256
{
    private const int RateBytes = 136; // 1088-bit rate for a 256-bit digest
    private const int DigestBytes = 32;

    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
    ];

    private static readonly int[] RotationOffsets =
    [
        1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 2, 14, 27, 41, 56, 8, 25, 43, 62, 18, 39, 61, 20, 44,
    ];

    private static readonly int[] PiLanes =
    [
        10, 7, 11, 17, 18, 3, 5, 16, 8, 21, 24, 4, 15, 23, 19, 13, 12, 2, 20, 14, 22, 9, 6, 1,
    ];

    /// <summary>Keccak-256 digest (32 bytes) of <paramref name="input"/>.</summary>
    public static byte[] Hash(ReadOnlySpan<byte> input)
    {
        var state = new ulong[25];

        var offset = 0;
        while (input.Length - offset >= RateBytes)
        {
            Absorb(state, input.Slice(offset, RateBytes));
            Permute(state);
            offset += RateBytes;
        }

        Span<byte> last = stackalloc byte[RateBytes];
        last.Clear();
        input[offset..].CopyTo(last);
        last[input.Length - offset] ^= 0x01;
        last[RateBytes - 1] ^= 0x80;
        Absorb(state, last);
        Permute(state);

        var digest = new byte[DigestBytes];
        for (var i = 0; i < DigestBytes / 8; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(digest.AsSpan(i * 8), state[i]);
        return digest;
    }

    private static void Absorb(ulong[] state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < RateBytes / 8; i++)
            state[i] ^= BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i * 8, 8));
    }

    private static void Permute(ulong[] st)
    {
        Span<ulong> bc = stackalloc ulong[5];

        for (var round = 0; round < 24; round++)
        {
            // Theta
            for (var i = 0; i < 5; i++)
                bc[i] = st[i] ^ st[i + 5] ^ st[i + 10] ^ st[i + 15] ^ st[i + 20];

            for (var i = 0; i < 5; i++)
            {
                var t = bc[(i + 4) % 5] ^ BitOperations.RotateLeft(bc[(i + 1) % 5], 1);
                for (var j = 0; j < 25; j += 5)
                    st[j + i] ^= t;
            }

            // Rho + Pi
            var carry = st[1];
            for (var i = 0; i < 24; i++)
            {
                var lane = PiLanes[i];
                var saved = st[lane];
                st[lane] = BitOperations.RotateLeft(carry, RotationOffsets[i]);
                carry = saved;
            }

            // Chi
            for (var j = 0; j < 25; j += 5)
            {
                for (var i = 0; i < 5; i++)
                    bc[i] = st[j + i];
                for (var i = 0; i < 5; i++)
                    st[j + i] ^= ~bc[(i + 1) % 5] & bc[(i + 2) % 5];
            }

            // Iota
            st[0] ^= RoundConstants[round];
        }
    }
}
