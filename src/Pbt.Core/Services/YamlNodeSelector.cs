using System.Text.RegularExpressions;
using Pbt.Core.Models;
using YamlDotNet.RepresentationModel;

namespace Pbt.Core.Services;

public class YamlNodeSelector
{
    public List<YamlNodeMatch> SelectNodes(YamlMappingNode root, string path, StepFilter? filter = null)
    {
        var tokens = ParsePath(path);
        var matches = new List<YamlNodeMatch>();
        SelectNodesRecursive(root, tokens, 0, "", matches, filter);
        return matches;
    }

    private void SelectNodesRecursive(
        YamlNode currentNode,
        List<PathToken> tokens,
        int tokenIndex,
        string currentPath,
        List<YamlNodeMatch> matches,
        StepFilter? filter = null)
    {
        if (tokenIndex >= tokens.Count)
        {
            // Reached the end of the path - this is a match
            if (currentNode is YamlScalarNode scalar)
            {
                matches.Add(new YamlNodeMatch
                {
                    Node = scalar,
                    Path = currentPath
                });
            }
            return;
        }

        var token = tokens[tokenIndex];

        if (token.Type == PathTokenType.Property)
        {
            if (currentNode is YamlMappingNode mapping)
            {
                var key = new YamlScalarNode(token.Value);
                if (mapping.Children.TryGetValue(key, out var childNode))
                {
                    var newPath = string.IsNullOrEmpty(currentPath)
                        ? token.Value
                        : $"{currentPath}.{token.Value}";
                    SelectNodesRecursive(childNode, tokens, tokenIndex + 1, newPath, matches, filter);
                }
            }
        }
        else if (token.Type == PathTokenType.ArrayWildcard)
        {
            if (currentNode is YamlSequenceNode sequence)
            {
                for (int i = 0; i < sequence.Children.Count; i++)
                {
                    var arrayItem = sequence.Children[i];

                    // Apply filter if specified
                    if (filter != null && !MatchesFilter(arrayItem, filter))
                    {
                        continue; // Skip this item
                    }

                    var newPath = $"{currentPath}[{i}]";
                    SelectNodesRecursive(arrayItem, tokens, tokenIndex + 1, newPath, matches, filter);
                }
            }
        }
        else if (token.Type == PathTokenType.ArrayIndex)
        {
            if (currentNode is YamlSequenceNode sequence)
            {
                if (token.Index >= 0 && token.Index < sequence.Children.Count)
                {
                    var arrayItem = sequence.Children[token.Index];

                    // Apply filter if specified
                    if (filter != null && !MatchesFilter(arrayItem, filter))
                    {
                        return; // Skip this item
                    }

                    var newPath = $"{currentPath}[{token.Index}]";
                    SelectNodesRecursive(arrayItem, tokens, tokenIndex + 1, newPath, matches, filter);
                }
            }
        }
    }

    private bool MatchesFilter(YamlNode node, StepFilter filter)
    {
        if (node is not YamlMappingNode mapping)
        {
            return false;
        }

        var propertyKey = new YamlScalarNode(filter.Property);
        if (!mapping.Children.TryGetValue(propertyKey, out var propertyNode))
        {
            return false;
        }

        if (propertyNode is not YamlScalarNode scalarValue)
        {
            return false;
        }

        var value = scalarValue.Value ?? string.Empty;
        var options = ParseRegexFlags(filter.Flags);

        return Regex.IsMatch(value, filter.Pattern, options);
    }

    private RegexOptions ParseRegexFlags(string? flags)
    {
        if (string.IsNullOrEmpty(flags))
        {
            return RegexOptions.None;
        }

        var options = RegexOptions.None;

        if (flags.Contains('i'))
            options |= RegexOptions.IgnoreCase;

        if (flags.Contains('m'))
            options |= RegexOptions.Multiline;

        if (flags.Contains('s'))
            options |= RegexOptions.Singleline;

        if (flags.Contains('x'))
            options |= RegexOptions.IgnorePatternWhitespace;

        return options;
    }

    private List<PathToken> ParsePath(string path)
    {
        var tokens = new List<PathToken>();

        // Pattern to match: property, [*], or [0]
        var pattern = @"([a-zA-Z_][a-zA-Z0-9_]*)|(\[\*\])|(\[(\d+)\])";
        var matches = Regex.Matches(path, pattern);

        foreach (Match match in matches)
        {
            if (match.Groups[1].Success)
            {
                // Property name
                tokens.Add(new PathToken
                {
                    Type = PathTokenType.Property,
                    Value = match.Groups[1].Value
                });
            }
            else if (match.Groups[2].Success)
            {
                // Array wildcard [*]
                tokens.Add(new PathToken
                {
                    Type = PathTokenType.ArrayWildcard
                });
            }
            else if (match.Groups[3].Success)
            {
                // Array index [N]
                tokens.Add(new PathToken
                {
                    Type = PathTokenType.ArrayIndex,
                    Index = int.Parse(match.Groups[4].Value)
                });
            }
        }

        if (tokens.Count == 0)
        {
            throw new ArgumentException($"Invalid path: {path}");
        }

        return tokens;
    }
}

public class YamlNodeMatch
{
    public YamlScalarNode Node { get; set; } = null!;
    public string Path { get; set; } = string.Empty;
}

public enum PathTokenType
{
    Property,
    ArrayWildcard,
    ArrayIndex
}

public class PathToken
{
    public PathTokenType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public int Index { get; set; }
}
