using System;
using System.Text.RegularExpressions;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Maps etcd-native path keys (segments separated by '/') to the ':'-separated keys
/// used by <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// </summary>
/// <remarks>
/// etcd stores keys as opaque byte strings; '/' is only a naming convention. This mapper
/// treats '/' as the hierarchy separator and converts it to ':', while leaving collection
/// index suffixes such as <c>key:0</c> attached to their segment.
/// Scope matching against a root key is case-sensitive, matching etcd. Relative configuration
/// keys are merged case-insensitively by the configuration loader.
/// </remarks>
internal static partial class EtcdKeyMapper
{
    /// <summary>
    /// Matches a path segment: a non-colon name optionally followed by one or more canonical
    /// collection indices, for example <c>key</c>, <c>key:0</c>, or <c>key:0:1</c>.
    /// </summary>
    /// <returns>The generated pattern used to validate path segments.</returns>
    [GeneratedRegex("^[^:]+(?::(0|[1-9][0-9]*))*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ValidSegmentPattern();

    /// <summary>
    /// Trims whitespace and normalizes the root key to exactly one leading and one trailing '/'.
    /// The trailing '/' is required so an etcd range query for <c>/app/</c> cannot read keys
    /// under <c>/application/</c>.
    /// </summary>
    /// <param name="rootKey">The configured root key.</param>
    /// <returns>The normalized root key.</returns>
    internal static string NormalizeRootKey(string rootKey)
    {
        var trimmedRootKey = rootKey.Trim().Trim('/');

        if (string.IsNullOrEmpty(trimmedRootKey))
        {
            return "/";
        }

        return "/" + trimmedRootKey + "/";
    }

    /// <summary>
    /// Gets whether the etcd key is a strict descendant of the root key. Scope is determined
    /// by the path boundary, so <c>/prefix-other/key</c> is not in the scope of <c>/prefix/</c>.
    /// The comparison is case-sensitive because etcd keys are case-sensitive.
    /// </summary>
    /// <param name="etcdKey">The raw etcd key.</param>
    /// <param name="rootKey">The configured root key.</param>
    /// <returns>True when the key is under the root key; otherwise false.</returns>
    internal static bool IsInScope(string etcdKey, string rootKey)
    {
        var normalizedRootKey = NormalizeRootKey(rootKey);
        var prefix = normalizedRootKey.EndsWith('/')
            ? normalizedRootKey
            : normalizedRootKey + "/";

        return etcdKey.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets whether the etcd key uses the removed ':' hierarchy format under the root key.
    /// The comparison is case-sensitive because etcd keys are case-sensitive.
    /// </summary>
    /// <param name="etcdKey">The raw etcd key.</param>
    /// <param name="rootKey">The configured root key.</param>
    /// <returns>True when the key uses the old format; otherwise false.</returns>
    internal static bool IsOldFormat(string etcdKey, string rootKey)
    {
        var normalizedRootKey = NormalizeRootKey(rootKey);
        return etcdKey.StartsWith(normalizedRootKey.TrimEnd('/') + ":", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates an etcd key and maps it to the ':'-separated configuration key format.
    /// </summary>
    /// <param name="etcdKey">The raw etcd key.</param>
    /// <returns>The mapped configuration key, for example <c>/prefix:key:subkey</c>.</returns>
    /// <exception cref="EtcdConfigurationKeyFormatException">
    /// The key is empty, contains an empty segment, or uses ':' outside a collection index suffix.
    /// </exception>
    internal static string ToConfigurationKey(string etcdKey)
    {
        if (string.IsNullOrEmpty(etcdKey))
        {
            throw new EtcdConfigurationKeyFormatException(
                "The etcd key is empty. Configuration keys must be non-empty paths such as '/prefix/key'.");
        }

        var segments = SplitAndValidateSegments(etcdKey, etcdKey.TrimStart('/'));
        return "/" + string.Join(':', segments);
    }

    /// <summary>
    /// Strips the normalized root prefix from an in-scope etcd key and maps the remaining
    /// path segments to a relative configuration key. The result starts with ':' so it can be
    /// appended directly to <see cref="EtcdOptions.ConfigurationPrefix"/>.
    /// </summary>
    /// <param name="etcdKey">The raw etcd key, which must be a strict descendant of the root.</param>
    /// <param name="rootKey">The configured root key.</param>
    /// <returns>The relative configuration key, for example <c>:database:host</c>.</returns>
    /// <exception cref="EtcdConfigurationKeyFormatException">
    /// The key has no relative path, contains an empty segment, or uses ':' outside a
    /// collection index suffix.
    /// </exception>
    internal static string ToRelativeConfigurationKey(string etcdKey, string rootKey)
    {
        var normalizedRootKey = NormalizeRootKey(rootKey);
        var prefix = normalizedRootKey.EndsWith('/')
            ? normalizedRootKey
            : normalizedRootKey + "/";

        if (!etcdKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new EtcdConfigurationKeyFormatException(
                $"The etcd key '{etcdKey}' is not a strict descendant of the root key '{normalizedRootKey}'. " +
                "Only keys below the root path boundary can be mapped.");
        }

        var relativePath = etcdKey[prefix.Length..];
        var segments = SplitAndValidateSegments(etcdKey, relativePath);
        return ":" + string.Join(':', segments);
    }

    /// <summary>
    /// Gets whether a path segment is valid. ':' is allowed only as a collection index suffix.
    /// </summary>
    /// <param name="segment">The path segment to validate.</param>
    /// <returns>True when the segment is valid; otherwise false.</returns>
    internal static bool IsValidSegment(string segment) =>
        !string.IsNullOrEmpty(segment) && ValidSegmentPattern().IsMatch(segment);

    private static string[] SplitAndValidateSegments(string etcdKey, string path)
    {
        if (path.Length == 0)
        {
            throw new EtcdConfigurationKeyFormatException(
                $"The etcd key '{etcdKey}' does not contain a path segment. Use a key such as '/prefix/key'.");
        }

        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                throw new EtcdConfigurationKeyFormatException(
                    $"The etcd key '{etcdKey}' contains an empty path segment. Remove duplicate or trailing '/' characters.");
            }

            if (!IsValidSegment(segment))
            {
                throw new EtcdConfigurationKeyFormatException(
                    $"The etcd key '{etcdKey}' contains the invalid path segment '{segment}'. " +
                    "':' is only allowed as a collection index suffix such as 'key:0'. " +
                    "Use '/' to separate path segments.");
            }
        }

        return segments;
    }
}

/// <summary>
/// Thrown when an etcd key cannot be mapped to a configuration key because it uses an
/// unsupported format, such as the removed ':' hierarchy separator or an empty path segment.
/// </summary>
internal sealed class EtcdConfigurationKeyFormatException : InvalidOperationException
{
    internal EtcdConfigurationKeyFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when two in-scope etcd keys map to the same configuration key.
/// </summary>
internal sealed class EtcdConfigurationKeyCollisionException : InvalidOperationException
{
    internal EtcdConfigurationKeyCollisionException(string message)
        : base(message)
    {
    }
}
