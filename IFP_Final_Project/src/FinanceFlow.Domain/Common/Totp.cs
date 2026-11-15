using System.Security.Cryptography;
using System.Text;

namespace FinanceFlow.Domain.Common;

/// <summary>
/// The one time password of the authenticator (RFC 6238).
/// The function is pure: the same secret and the same moment always produce the same code,
/// so the behaviour can be proved with the test vectors of the standard.
/// </summary>
public static class Totp
{
    /// <summary>The length of a time step in seconds.</summary>
    public const int StepSeconds = 30;

    /// <summary>The number of digits of a code.</summary>
    public const int Digits = 6;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Calculates the code of the given moment.
    /// </summary>
    /// <param name="secret">The shared secret in the base32 alphabet.</param>
    /// <param name="moment">The moment the code is valid for.</param>
    /// <param name="stepSeconds">The length of the time step.</param>
    /// <param name="digits">The number of digits of the code.</param>
    public static string GenerateCode(
        string secret,
        DateTimeOffset moment,
        int stepSeconds = StepSeconds,
        int digits = Digits)
    {
        ArgumentNullException.ThrowIfNull(secret);

        byte[] key = FromBase32(secret);
        long counter = moment.ToUnixTimeSeconds() / stepSeconds;

        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        using HMACSHA1 hmac = new(key);
        byte[] hash = hmac.ComputeHash(counterBytes.ToArray());

        int offset = hash[^1] & 0x0F;

        int binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        int modulus = (int)Math.Pow(10, digits);

        return (binary % modulus).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Checks a code. The window allows a small difference of the clocks of the phone and the server.
    /// </summary>
    /// <param name="secret">The shared secret in the base32 alphabet.</param>
    /// <param name="code">The code the user typed.</param>
    /// <param name="moment">The current moment.</param>
    /// <param name="window">The number of steps that are accepted in both directions.</param>
    public static bool VerifyCode(string secret, string code, DateTimeOffset moment, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string candidate = code.Trim().Replace(" ", string.Empty);

        for (int step = -window; step <= window; step++)
        {
            string expected = GenerateCode(secret, moment.AddSeconds(step * StepSeconds));

            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(expected),
                    System.Text.Encoding.ASCII.GetBytes(candidate.PadLeft(expected.Length, '0'))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Creates a random shared secret in the base32 alphabet.</summary>
    /// <param name="length">The number of characters of the secret.</param>
    public static string GenerateSecret(int length = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 10);

        StringBuilder builder = new(length);

        for (int index = 0; index < length; index++)
        {
            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>Groups the secret in blocks of four, the way the applications show it.</summary>
    public static string FormatSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        StringBuilder builder = new(secret.Length + (secret.Length / 4));

        for (int index = 0; index < secret.Length; index++)
        {
            if (index > 0 && index % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(secret[index]);
        }

        return builder.ToString();
    }

    /// <summary>Converts a secret of the base32 alphabet into the bytes of the key.</summary>
    public static byte[] FromBase32(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        string cleaned = secret.Replace(" ", string.Empty).TrimEnd('=').ToUpperInvariant();
        byte[] result = new byte[cleaned.Length * 5 / 8];

        int buffer = 0;
        int bitsLeft = 0;
        int index = 0;

        foreach (char character in cleaned)
        {
            int value = Alphabet.IndexOf(character);

            if (value < 0)
            {
                throw new FormatException("The secret of the authenticator is not valid.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                result[index++] = (byte)(buffer >> (bitsLeft - 8));
                bitsLeft -= 8;
            }
        }

        return result;
    }
}
