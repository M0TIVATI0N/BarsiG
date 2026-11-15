using System.Security.Cryptography;

using FinanceFlow.Application.Abstractions;

namespace FinanceFlow.Infrastructure.Security;

/// <summary>
/// Hashes passwords with PBKDF2 and SHA-256.
/// The salt is stored next to the hash, and the comparison uses a constant time
/// comparison so that the answer does not depend on how many characters matched.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>The size of the random salt in bytes.</summary>
    public const int SaltSize = 16;

    /// <summary>The size of the derived key in bytes.</summary>
    public const int KeySize = 32;

    /// <summary>The number of hashing iterations.</summary>
    public const int Iterations = 100_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <inheritdoc />
    public (string Hash, string Salt) Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        byte[] saltBytes;
        byte[] expected;

        try
        {
            saltBytes = Convert.FromBase64String(salt);
            expected = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algorithm, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
