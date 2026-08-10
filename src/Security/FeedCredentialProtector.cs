using Dynamicweb.Security.SystemTools;

namespace SJS.DW.PrivateNugetProvider.Security;

internal static class FeedCredentialProtector
{
    private const string Prefix = "dwcrypto:v1:";

    internal static string? Protect(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Prefix + Crypto.Encrypt(value);
    }

    internal static string? Unprotect(string? storedValue, out bool shouldMigrate)
    {
        shouldMigrate = false;
        if (string.IsNullOrEmpty(storedValue))
            return null;

        if (storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            return Crypto.Decrypt(storedValue[Prefix.Length..]);

        // Values saved by earlier releases were encrypted without a format marker.
        try
        {
            var decrypted = Crypto.Decrypt(storedValue);
            shouldMigrate = true;
            return decrypted;
        }
        catch
        {
            // Very old rows may contain plaintext. Use it once and upgrade the row.
            shouldMigrate = true;
            return storedValue;
        }
    }
}
