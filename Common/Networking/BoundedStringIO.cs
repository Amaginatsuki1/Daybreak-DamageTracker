using System.Security.Cryptography;
using System.Text;

namespace DaybreakDamageTracker.Common.Networking;

internal static class BoundedStringIO
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string Read(BinaryReader reader, int maximumCharacters, string label)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));

        int byteCount = reader.Read7BitEncodedInt();
        int maximumBytes = Encoding.UTF8.GetMaxByteCount(maximumCharacters);
        if (byteCount < 0 || byteCount > maximumBytes)
            throw new IOException($"Invalid {label} byte length {byteCount}.");

        byte[] bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
            throw new EndOfStreamException($"Unexpected end of packet while reading {label}.");

        string value;
        try
        {
            value = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new IOException($"Invalid UTF-8 in {label}.", exception);
        }

        if (value.Length > maximumCharacters)
            throw new IOException($"Invalid {label} character length {value.Length}.");
        return value;
    }

    public static string StableIdentity(string? value, int maximumCharacters)
    {
        if (maximumCharacters <= 0)
            return string.Empty;

        string text = value ?? string.Empty;
        if (text.Length <= maximumCharacters)
            return text;

        const int hashCharacters = 16;
        if (maximumCharacters <= hashCharacters + 1)
            return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(text)))[..maximumCharacters];

        string hash = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(text)))[..hashCharacters];
        int prefixLength = maximumCharacters - hashCharacters - 1;
        if (char.IsHighSurrogate(text[prefixLength - 1]) &&
            prefixLength < text.Length &&
            char.IsLowSurrogate(text[prefixLength]))
        {
            prefixLength--;
        }
        return $"{text[..prefixLength]}~{hash}";
    }
}
