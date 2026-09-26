using System.Diagnostics.CodeAnalysis;

namespace DisplayRotate.Core.Updates;

/// <summary>
/// A release's <c>SHA256SUMS.txt</c>: one <c>&lt;64 hex&gt;  &lt;file name&gt;</c> per line, as
/// sha256sum writes it (a <c>*</c> binary marker is accepted).
/// </summary>
/// <remarks>
/// Strict on purpose. Any non-blank line that is not a digest and a name refuses the whole
/// file - a 404 page or a truncated upload is not a checksum file - and a name listed twice with
/// different digests is refused rather than resolved either way.
/// </remarks>
public sealed class Sha256Sums
{
    private readonly IReadOnlyDictionary<string, string> _digests;

    private Sha256Sums(IReadOnlyDictionary<string, string> digests) => _digests = digests;

    public int Count => _digests.Count;

    public string? DigestOf(string fileName) =>
        _digests.TryGetValue(fileName, out var digest) ? digest : null;

    public bool Matches(string fileName, ReadOnlySpan<byte> digest) =>
        DigestOf(fileName) is { } recorded
        && string.Equals(recorded, Convert.ToHexStringLower(digest), StringComparison.Ordinal);

    public static bool TryParse(string text, [NotNullWhen(true)] out Sha256Sums? sums, [NotNullWhen(false)] out string? problem)
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;

        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim('\r', ' ', '\t', '﻿');

            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length < 64 || !IsHex(line.AsSpan(0, 64)) || (line.Length > 64 && line[64] != ' '))
            {
                problem = $"line {number} does not start with a SHA-256 digest";
                sums = null;
                return false;
            }

            var name = line[64..].TrimStart(' ').TrimStart('*');

            if (name.Length == 0)
            {
                problem = $"line {number} names no file";
                sums = null;
                return false;
            }

            var digest = line[..64].ToLowerInvariant();

            if (digests.TryGetValue(name, out var earlier) && !string.Equals(earlier, digest, StringComparison.Ordinal))
            {
                problem = $"'{name}' is listed twice with different digests";
                sums = null;
                return false;
            }

            digests[name] = digest;
        }

        if (digests.Count == 0)
        {
            problem = "the file lists nothing";
            sums = null;
            return false;
        }

        sums = new Sha256Sums(digests);
        problem = null;
        return true;
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
