using Pbt.Core.Models;
using Pbt.Core.Services;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pbt.Core.Infrastructure;

/// <summary>
/// Service for serializing and deserializing YAML files with snake_case convention
/// </summary>
public class YamlSerializer
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    public YamlSerializer()
    {
        // Configure deserializer (YAML -> C#)
        // snake_case in YAML maps to PascalCase in C#
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new RelationshipDefinitionYamlConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        // Configure serializer (C# -> YAML)
        // PascalCase in C# maps to snake_case in YAML
        _serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    /// <summary>
    /// Load and deserialize a YAML file
    /// </summary>
    public T LoadFromFile<T>(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"YAML file not found: {filePath}");
        }

        try
        {
            var yaml = File.ReadAllText(filePath);
            return _deserializer.Deserialize<T>(yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse YAML file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Deserialize YAML string to object
    /// </summary>
    public T Deserialize<T>(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<T>(yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to deserialize YAML", ex);
        }
    }

    /// <summary>
    /// Save object to YAML file
    /// </summary>
    public void SaveToFile<T>(T obj, string filePath)
    {
        try
        {
            var yaml = _serializer.Serialize(obj);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save YAML file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Serialize object to YAML string
    /// </summary>
    public string Serialize<T>(T obj)
    {
        try
        {
            return _serializer.Serialize(obj);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to serialize to YAML", ex);
        }
    }
}

/// <summary>
/// Custom YAML type converter that supports both shorthand string syntax
/// and verbose object syntax for relationship definitions.
/// Shorthand: "Sales.CustomerID -> Customers.CustomerID"
/// Verbose: { from_table: Sales, from_column: CustomerID, ... }
/// </summary>
internal class RelationshipDefinitionYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(RelationshipDefinition);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Check if the value is a scalar (shorthand string)
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            var parsed = RelationshipShorthandParser.TryParseShorthand(scalar.Value);
            if (parsed != null)
                return parsed;

            throw new YamlException(scalar.Start, scalar.End,
                $"Invalid relationship shorthand: '{scalar.Value}'. Expected format: 'Table.Column -> Table.Column'");
        }

        // Otherwise, deserialize as a mapping (verbose object syntax)
        // We need to manually parse the mapping since we've consumed the type converter
        parser.Consume<MappingStart>();

        var rel = new RelationshipDefinition();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            var value = parser.Consume<Scalar>().Value;

            switch (key)
            {
                case "from_table":
                    rel.FromTable = value;
                    break;
                case "from_column":
                    rel.FromColumn = value;
                    break;
                case "to_table":
                    rel.ToTable = value;
                    break;
                case "to_column":
                    rel.ToColumn = value;
                    break;
                case "cardinality":
                    rel.Cardinality = value;
                    break;
                case "cross_filter_direction":
                    rel.CrossFilterDirection = value;
                    break;
                case "active":
                    rel.Active = bool.Parse(value);
                    break;
                case "rely_on_referential_integrity":
                    rel.RelyOnReferentialIntegrity = bool.Parse(value);
                    break;
            }
        }

        return rel;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        // Always write as verbose mapping for serialization
        var rel = (RelationshipDefinition)value!;

        emitter.Emit(new MappingStart());

        EmitScalar(emitter, "from_table", rel.FromTable);
        EmitScalar(emitter, "from_column", rel.FromColumn);
        EmitScalar(emitter, "to_table", rel.ToTable);
        EmitScalar(emitter, "to_column", rel.ToColumn);

        if (rel.Cardinality != "ManyToOne")
        {
            EmitScalar(emitter, "cardinality", rel.Cardinality);
        }

        if (!string.IsNullOrWhiteSpace(rel.CrossFilterDirection))
        {
            EmitScalar(emitter, "cross_filter_direction", rel.CrossFilterDirection);
        }

        if (!rel.Active)
        {
            EmitScalar(emitter, "active", "false");
        }

        if (rel.RelyOnReferentialIntegrity)
        {
            EmitScalar(emitter, "rely_on_referential_integrity", "true");
        }

        emitter.Emit(new MappingEnd());
    }

    private static void EmitScalar(IEmitter emitter, string key, string value)
    {
        emitter.Emit(new Scalar(key));
        emitter.Emit(new Scalar(value));
    }
}
