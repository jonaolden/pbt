using System.Text.RegularExpressions;
using Pbt.Core.Models;

namespace Pbt.Core.Services;

/// <summary>
/// Parses relationship shorthand syntax like "Sales.CustomerID -> Customers.CustomerID"
/// into RelationshipDefinition objects.
/// </summary>
public static class RelationshipShorthandParser
{
    private static readonly Regex ShorthandPattern = new(
        @"^(\w+)\.(\w+)\s*->\s*(\w+)\.(\w+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Try to parse a shorthand relationship string.
    /// Returns null if the string doesn't match the shorthand pattern.
    /// Shorthand defaults: ManyToOne cardinality, Single cross-filter direction, Active.
    /// </summary>
    public static RelationshipDefinition? TryParseShorthand(string shorthand)
    {
        var match = ShorthandPattern.Match(shorthand.Trim());
        if (!match.Success)
            return null;

        return new RelationshipDefinition
        {
            FromTable = match.Groups[1].Value,
            FromColumn = match.Groups[2].Value,
            ToTable = match.Groups[3].Value,
            ToColumn = match.Groups[4].Value,
            Cardinality = "ManyToOne",
            CrossFilterDirection = "Single",
            Active = true
        };
    }

    /// <summary>
    /// Check if a string looks like a relationship shorthand
    /// </summary>
    public static bool IsShorthand(string value)
    {
        return value.Contains("->");
    }
}
