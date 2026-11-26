using System;
using System.Collections.Generic;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Shared validation for the etcd root definitions and the output configuration prefix.
/// All callers (DataAnnotations, the TLS/options validator, and the loader) use the same
/// rules so an invalid configuration is rejected before any etcd call.
/// </summary>
internal static class EtcdRootKeyValidator
{
    /// <summary>
    /// Validates the roots and the configuration prefix and returns every failure.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>All validation failures in a deterministic order.</returns>
    internal static IReadOnlyList<string> Validate(EtcdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        failures.AddRange(ValidateRootKeys(options.RootKeys));
        failures.AddRange(ValidateConfigurationPrefix(options.ConfigurationPrefix));
        return failures;
    }

    /// <summary>
    /// Validates the configured root definitions and returns every failure.
    /// Root definitions are compared case-insensitively for duplicates, nesting, and overlap;
    /// etcd scope matching itself remains case-sensitive.
    /// </summary>
    /// <param name="rootKeys">The configured root definitions.</param>
    /// <returns>All validation failures.</returns>
    internal static IReadOnlyList<string> ValidateRootKeys(IReadOnlyList<EtcdRootKey>? rootKeys)
    {
        var failures = new List<string>();

        if (rootKeys is null)
        {
            failures.Add("RootKeys must be set to a non empty list");
            return failures;
        }

        if (rootKeys.Count == 0)
        {
            failures.Add("At least one root key must be configured");
            return failures;
        }

        var priorities = new Dictionary<int, int>();
        var validRoots = new List<string>();

        for (var index = 0; index < rootKeys.Count; index++)
        {
            ValidateRootKey(rootKeys[index], index, priorities, validRoots, failures);
        }

        AddDuplicatePriorityFailures(priorities, failures);
        AddRootRelationshipFailures(validRoots, failures);
        return failures;
    }

    private static void ValidateRootKey(
        EtcdRootKey? root,
        int index,
        Dictionary<int, int> priorities,
        List<string> validRoots,
        List<string> failures)
    {
        if (root is null)
        {
            failures.Add($"RootKeys[{index}] must not be null.");
            return;
        }

        priorities[root.Priority] = priorities.TryGetValue(root.Priority, out var count)
            ? count + 1
            : 1;

        if (string.IsNullOrWhiteSpace(root.RootKey))
        {
            failures.Add("RootKey must be set to a non empty string");
            return;
        }

        var rootKey = root.RootKey.Trim();
        var error = ValidateRootKeyPath(rootKey);
        if (error is not null)
        {
            failures.Add(error);
            return;
        }

        validRoots.Add(rootKey);
    }

    private static void AddDuplicatePriorityFailures(
        Dictionary<int, int> priorities,
        List<string> failures)
    {
        foreach (var priority in priorities)
        {
            if (priority.Value > 1)
            {
                failures.Add(
                    $"RootKeys must have unique Priority values; priority {priority.Key} is used more than once.");
            }
        }
    }

    private static void AddRootRelationshipFailures(
        List<string> validRoots,
        List<string> failures)
    {
        for (var first = 0; first < validRoots.Count; first++)
        {
            for (var second = first + 1; second < validRoots.Count; second++)
            {
                var firstRoot = validRoots[first];
                var secondRoot = validRoots[second];

                if (string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"RootKeys must not contain duplicate roots; '{firstRoot}' and '{secondRoot}' " +
                        "are the same when compared case-insensitively.");
                }
                else if (IsNested(firstRoot, secondRoot))
                {
                    failures.Add(
                        $"RootKeys must not contain nested or overlapping roots; '{firstRoot}' and '{secondRoot}' " +
                        "overlap when compared case-insensitively.");
                }
            }
        }
    }

    /// <summary>
    /// Validates the output configuration prefix and returns every failure. The prefix is
    /// never queried from etcd; it only determines where the merged configuration is exposed.
    /// </summary>
    /// <param name="configurationPrefix">The configured output prefix.</param>
    /// <returns>All validation failures.</returns>
    internal static IReadOnlyList<string> ValidateConfigurationPrefix(string? configurationPrefix)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(configurationPrefix))
        {
            failures.Add("ConfigurationPrefix must be set to a non empty string");
            return failures;
        }

        var prefix = configurationPrefix.Trim();

        if (string.Equals(prefix, "/", StringComparison.Ordinal))
        {
            failures.Add("ConfigurationPrefix must not be '/'; choose a prefix scoped to this application.");
        }
        else if (!prefix.StartsWith('/'))
        {
            failures.Add("ConfigurationPrefix must start with '/'.");
        }
        else if (prefix.EndsWith('/'))
        {
            failures.Add("ConfigurationPrefix must not end with '/'.");
        }
        else if (prefix.Contains("//", StringComparison.Ordinal))
        {
            failures.Add("ConfigurationPrefix must not contain empty path segments ('//').");
        }
        else if (prefix.Contains(':'))
        {
            failures.Add("ConfigurationPrefix must not contain ':'.");
        }

        return failures;
    }

    private static bool IsNested(string firstRoot, string secondRoot) =>
        firstRoot.StartsWith(secondRoot, StringComparison.OrdinalIgnoreCase) ||
        secondRoot.StartsWith(firstRoot, StringComparison.OrdinalIgnoreCase);

    private static string? ValidateRootKeyPath(string rootKey)
    {
        if (string.Equals(rootKey, "/", StringComparison.Ordinal))
        {
            return "RootKey must be a non-root prefix; '/' would load every key in etcd.";
        }

        if (!rootKey.StartsWith('/'))
        {
            return "RootKey must start with '/'.";
        }

        if (rootKey.Contains("//", StringComparison.Ordinal))
        {
            return "RootKey must not contain empty path segments ('//').";
        }

        if (rootKey.Contains(':'))
        {
            return "RootKey must not contain ':'; use '/' to separate etcd path segments.";
        }

        if (!rootKey.EndsWith('/'))
        {
            return "RootKey must end with '/'.";
        }

        return null;
    }
}
