using System.Text;
using Pbt.Core.Models;
using YamlDotNet.RepresentationModel;

namespace Pbt.Core.Services;

public class TableMerger
{
    private readonly YamlNodeSelector _selector;

    public TableMerger()
    {
        _selector = new YamlNodeSelector();
    }

    public MergeResult MergeFiles(
        string sourceFilePath,
        string targetFilePath,
        MergeConfig mergeConfig,
        bool dryRun)
    {
        var result = new MergeResult
        {
            SourceFile = sourceFilePath,
            TargetFile = targetFilePath
        };

        // Load both YAML files
        var sourceYaml = LoadYaml(sourceFilePath);
        var targetYaml = LoadYaml(targetFilePath);

        if (sourceYaml.Documents.Count == 0 || targetYaml.Documents.Count == 0)
        {
            throw new InvalidOperationException("Cannot merge empty YAML files");
        }

        if (sourceYaml.Documents.Count > 1 || targetYaml.Documents.Count > 1)
        {
            throw new InvalidOperationException(
                "Multi-document YAML files are not supported for merge operations");
        }

        var sourceRoot = sourceYaml.Documents[0].RootNode as YamlMappingNode;
        var targetRoot = targetYaml.Documents[0].RootNode as YamlMappingNode;

        if (sourceRoot == null || targetRoot == null)
        {
            throw new InvalidOperationException("Root nodes must be mappings");
        }

        // Perform merge based on strategy
        bool fileChanged = PerformMerge(sourceRoot, targetRoot, mergeConfig, result);

        result.HasChanges = fileChanged;

        // Write back to target file if changes were made and not dry run
        if (fileChanged && !dryRun)
        {
            using var writer = new StreamWriter(targetFilePath, false, Encoding.UTF8);
            targetYaml.Save(writer, assignAnchors: false);
        }

        return result;
    }

    private YamlStream LoadYaml(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return yaml;
    }

    private bool PerformMerge(
        YamlMappingNode sourceRoot,
        YamlMappingNode targetRoot,
        MergeConfig config,
        MergeResult result)
    {
        bool hasChanges = false;

        // Get the sequence name from target_nodes (e.g., "columns[*]" -> "columns")
        var sequenceName = GetSequenceName(config.TargetNodes);
        var identifierProperty = GetIdentifierProperty(config.Identifier);

        // Get sequences from both roots
        var sourceSequence = GetSequenceNode(sourceRoot, sequenceName);
        var targetSequence = GetSequenceNode(targetRoot, sequenceName);

        if (sourceSequence == null)
        {
            throw new InvalidOperationException(
                $"Source file does not contain a '{sequenceName}' array");
        }

        if (targetSequence == null)
        {
            throw new InvalidOperationException(
                $"Target file does not contain a '{sequenceName}' array");
        }

        // Build lookup dictionaries based on identifier
        var sourceNodesByIdentifier = BuildIdentifierLookup(sourceSequence, identifierProperty);
        var targetNodesByIdentifier = BuildIdentifierLookup(targetSequence, identifierProperty);

        result.SourceNodesMatched = sourceNodesByIdentifier.Count;
        result.TargetNodesMatched = targetNodesByIdentifier.Count;

        // Apply merge strategy
        var strategy = config.Strategy.ToLower();

        switch (strategy)
        {
            case "union":
                hasChanges = MergeUnion(sourceNodesByIdentifier, targetNodesByIdentifier,
                    targetSequence, config, result);
                break;

            case "intersection":
                hasChanges = MergeIntersection(sourceNodesByIdentifier, targetNodesByIdentifier,
                    targetSequence, config, result);
                break;

            case "overwrite":
                hasChanges = MergeOverwrite(sourceNodesByIdentifier, targetNodesByIdentifier,
                    config, result);
                break;

            case "append":
                hasChanges = MergeAppend(sourceNodesByIdentifier, targetNodesByIdentifier,
                    targetSequence, config, result);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown merge strategy: {config.Strategy}. " +
                    $"Supported strategies: union, intersection, overwrite, append");
        }

        return hasChanges;
    }

    private string GetSequenceName(string targetNodesPath)
    {
        // Extract sequence name from path like "columns[*]" -> "columns"
        return targetNodesPath.Replace("[*]", "").Trim();
    }

    private string GetIdentifierProperty(string identifierPath)
    {
        // Extract property name from path like "columns[*].source_column" -> "source_column"
        var parts = identifierPath.Split('.');
        return parts[^1]; // Last part
    }

    private YamlSequenceNode? GetSequenceNode(YamlMappingNode root, string sequenceName)
    {
        var key = new YamlScalarNode(sequenceName);
        if (root.Children.TryGetValue(key, out var node))
        {
            return node as YamlSequenceNode;
        }
        return null;
    }

    private Dictionary<string, YamlMappingNode> BuildIdentifierLookup(
        YamlSequenceNode sequence,
        string identifierProperty)
    {
        var lookup = new Dictionary<string, YamlMappingNode>();

        foreach (var item in sequence.Children)
        {
            if (item is not YamlMappingNode mapping)
            {
                continue;
            }

            // Get the identifier value from the mapping
            var identifierKey = new YamlScalarNode(identifierProperty);
            if (mapping.Children.TryGetValue(identifierKey, out var identifierNode))
            {
                if (identifierNode is YamlScalarNode scalarNode && scalarNode.Value != null)
                {
                    lookup[scalarNode.Value] = mapping;
                }
            }
        }

        return lookup;
    }

    private bool MergeUnion(
        Dictionary<string, YamlMappingNode> sourceNodes,
        Dictionary<string, YamlMappingNode> targetNodes,
        YamlSequenceNode targetSequence,
        MergeConfig config,
        MergeResult result)
    {
        bool hasChanges = false;

        // Union: Keep all nodes from target, add nodes from source that don't exist in target
        foreach (var (identifier, sourceNode) in sourceNodes)
        {
            if (!targetNodes.ContainsKey(identifier))
            {
                // This node exists in source but not in target - add it
                hasChanges = true;
                result.NodesAdded++;
                result.Changes.Add($"Added node with identifier: {identifier}");

                // Clone the source node and add to target sequence
                var clonedNode = CloneNode(sourceNode) as YamlMappingNode;
                if (clonedNode != null)
                {
                    targetSequence.Add(clonedNode);
                }
            }
        }

        return hasChanges;
    }

    private bool MergeIntersection(
        Dictionary<string, YamlMappingNode> sourceNodes,
        Dictionary<string, YamlMappingNode> targetNodes,
        YamlSequenceNode targetSequence,
        MergeConfig config,
        MergeResult result)
    {
        bool hasChanges = false;

        // Intersection: Keep only nodes that exist in both source and target
        var nodesToRemove = new List<string>();

        foreach (var (identifier, _) in targetNodes)
        {
            if (!sourceNodes.ContainsKey(identifier))
            {
                // This node exists in target but not in source - mark for removal
                nodesToRemove.Add(identifier);
            }
        }

        foreach (var identifier in nodesToRemove)
        {
            hasChanges = true;
            result.NodesRemoved++;
            result.Changes.Add($"Removed node with identifier: {identifier}");

            targetSequence.Children.Remove(targetNodes[identifier]);
        }

        return hasChanges;
    }

    private bool MergeOverwrite(
        Dictionary<string, YamlMappingNode> sourceNodes,
        Dictionary<string, YamlMappingNode> targetNodes,
        MergeConfig config,
        MergeResult result)
    {
        bool hasChanges = false;

        // Overwrite: Update target nodes with source node values where identifiers match
        foreach (var (identifier, sourceNode) in sourceNodes)
        {
            if (targetNodes.TryGetValue(identifier, out var targetNode))
            {
                // Node exists in both - overwrite target with source values
                if (OverwriteNodeProperties(sourceNode, targetNode, config))
                {
                    hasChanges = true;
                    result.NodesUpdated++;
                    result.Changes.Add($"Overwritten node with identifier: {identifier}");
                }
            }
        }

        return hasChanges;
    }

    private bool MergeAppend(
        Dictionary<string, YamlMappingNode> sourceNodes,
        Dictionary<string, YamlMappingNode> targetNodes,
        YamlSequenceNode targetSequence,
        MergeConfig config,
        MergeResult result)
    {
        bool hasChanges = false;

        // Append: Add all nodes from source to target (even duplicates)
        foreach (var (identifier, sourceNode) in sourceNodes)
        {
            hasChanges = true;
            result.NodesAdded++;
            result.Changes.Add($"Appended node with identifier: {identifier}");

            var clonedNode = CloneNode(sourceNode) as YamlMappingNode;
            if (clonedNode != null)
            {
                targetSequence.Add(clonedNode);
            }
        }

        return hasChanges;
    }

    private bool OverwriteNodeProperties(
        YamlMappingNode sourceNode,
        YamlMappingNode targetNode,
        MergeConfig config)
    {
        bool hasChanges = false;
        var excludeSet = new HashSet<string>(config.Exclude ?? new List<string>());

        foreach (var (key, value) in sourceNode.Children)
        {
            if (key is YamlScalarNode scalarKey && scalarKey.Value != null)
            {
                // Check if this property should be excluded
                if (excludeSet.Contains(scalarKey.Value))
                {
                    continue;
                }

                // Check if the value is different
                if (!targetNode.Children.TryGetValue(key, out var targetValue) ||
                    !NodesAreEqual(value, targetValue))
                {
                    hasChanges = true;
                    targetNode.Children[key] = CloneNode(value);
                }
            }
        }

        return hasChanges;
    }

    private YamlNode CloneNode(YamlNode node)
    {
        return node switch
        {
            YamlScalarNode scalar => new YamlScalarNode(scalar.Value)
            {
                Style = scalar.Style,
                Tag = scalar.Tag
            },
            YamlSequenceNode sequence => new YamlSequenceNode(
                sequence.Children.Select(CloneNode))
            {
                Style = sequence.Style,
                Tag = sequence.Tag
            },
            YamlMappingNode mapping => new YamlMappingNode(
                mapping.Children.Select(kvp => new KeyValuePair<YamlNode, YamlNode>(
                    CloneNode(kvp.Key),
                    CloneNode(kvp.Value))))
            {
                Style = mapping.Style,
                Tag = mapping.Tag
            },
            _ => node
        };
    }

    private bool NodesAreEqual(YamlNode node1, YamlNode node2)
    {
        if (node1.GetType() != node2.GetType())
        {
            return false;
        }

        return (node1, node2) switch
        {
            (YamlScalarNode s1, YamlScalarNode s2) => s1.Value == s2.Value,
            (YamlSequenceNode seq1, YamlSequenceNode seq2) =>
                seq1.Children.Count == seq2.Children.Count &&
                seq1.Children.Zip(seq2.Children).All(pair => NodesAreEqual(pair.First, pair.Second)),
            (YamlMappingNode m1, YamlMappingNode m2) =>
                m1.Children.Count == m2.Children.Count &&
                m1.Children.All(kvp =>
                    m2.Children.TryGetValue(kvp.Key, out var value) &&
                    NodesAreEqual(kvp.Value, value)),
            _ => false
        };
    }
}

public class MergeResult
{
    public string SourceFile { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public int SourceNodesMatched { get; set; }
    public int TargetNodesMatched { get; set; }
    public int NodesAdded { get; set; }
    public int NodesRemoved { get; set; }
    public int NodesUpdated { get; set; }
    public bool HasChanges { get; set; }
    public List<string> Changes { get; set; } = new();
}
