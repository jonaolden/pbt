using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AnalysisServices.Tabular;
using Pbt.Core.Infrastructure;

namespace Pbt.Core.Services;

/// <summary>
/// Generates Power BI Project (.pbip) structure with TMDL semantic model and minimal report
/// </summary>
public static class PbipGenerator
{
    /// <summary>
    /// Generate PBIP project structure
    /// </summary>
    /// <param name="database">TOM Database to serialize</param>
    /// <param name="projectName">Project name (used for folder naming)</param>
    /// <param name="outputPath">Root output path for PBIP structure (typically target directory)</param>
    public static void GeneratePbipStructure(Database database, string projectName, string outputPath)
    {
        // Sanitize project name for filesystem
        var sanitizedModelName = FileNameSanitizer.Sanitize(projectName);

        // Define paths for PBIP structure
        var semanticModelPath = Path.Combine(outputPath, $"{sanitizedModelName}.SemanticModel");
        var reportPath = Path.Combine(outputPath, $"{sanitizedModelName}.Report");
        var pbipFilePath = Path.Combine(outputPath, $"{sanitizedModelName}.pbip");

        // 1. Create SemanticModel directory and definition subfolder
        if (Directory.Exists(semanticModelPath))
        {
            Directory.Delete(semanticModelPath, true);
        }
        Directory.CreateDirectory(semanticModelPath);
        var semanticModelDefinitionPath = Path.Combine(semanticModelPath, "definition");
        Directory.CreateDirectory(semanticModelDefinitionPath);

        // 2. Create Report directory and definition subfolder
        if (Directory.Exists(reportPath))
        {
            Directory.Delete(reportPath, true);
        }
        Directory.CreateDirectory(reportPath);
        var reportDefinitionPath = Path.Combine(reportPath, "definition");
        Directory.CreateDirectory(reportDefinitionPath);

        // Create StaticResources directories (expected by Power BI Desktop for PBIR format)
        Directory.CreateDirectory(Path.Combine(reportPath, "StaticResources", "RegisteredResources"));
        Directory.CreateDirectory(Path.Combine(reportPath, "StaticResources", "SharedResources", "BaseThemes"));

        // 3. Serialize TMDL files to SemanticModel/definition folder
        TmdlSerializer.SerializeDatabaseToFolder(database, semanticModelDefinitionPath);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // 4. Generate <name>.pbip file
        var pbipContent = new PbipRoot
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/pbip/pbipProperties/1.0.0/schema.json",
            Version = "1.0",
            Artifacts = new List<PbipArtifact>
            {
                new PbipArtifact
                {
                    Report = new PbipReportRef
                    {
                        Path = $"{sanitizedModelName}.Report"
                    }
                }
            },
            Settings = new PbipSettings
            {
                EnableAutoRecovery = true
            }
        };
        File.WriteAllText(pbipFilePath, System.Text.Json.JsonSerializer.Serialize(pbipContent, jsonOptions));

        // 5. Generate <name>.Report/definition.pbir (directly in Report folder, NOT in definition/)
        var pbirContent = new PbirDefinition
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json",
            Version = "4.0",
            DatasetReference = new DatasetReference
            {
                ByPath = new ByPathReference
                {
                    Path = $"../{sanitizedModelName}.SemanticModel"
                }
            }
        };
        File.WriteAllText(Path.Combine(reportPath, "definition.pbir"), System.Text.Json.JsonSerializer.Serialize(pbirContent, jsonOptions));

        // 6. Generate <name>.Report/definition/ content (PBIR enhanced format)
        //    Uses individual page/visual files instead of a monolithic report.json

        // 6a. report.json — minimal valid structure per schema 3.0.0
        //     Required: $schema, themeCollection (with baseTheme containing name, reportVersionAtImport object, type)
        //     reportVersionAtImport is an object with visual/page/report version strings, NOT a plain string
        //     These versions must match realistic PBI Desktop internal versions (not schema versions)
        //     datasetBinding is NOT allowed by the schema (additionalProperties: false)
        var reportJsonObj = new Dictionary<string, object>
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/report/3.0.0/schema.json",
            ["themeCollection"] = new Dictionary<string, object>
            {
                ["baseTheme"] = new Dictionary<string, object>
                {
                    ["name"] = "CY24SU05",
                    ["reportVersionAtImport"] = new Dictionary<string, object>
                    {
                        ["visual"] = "1.8.90",
                        ["page"] = "1.3.90",
                        ["report"] = "2.0.90"
                    },
                    ["type"] = "SharedResources"
                }
            }
        };
        File.WriteAllText(Path.Combine(reportDefinitionPath, "report.json"), System.Text.Json.JsonSerializer.Serialize(reportJsonObj, jsonOptions));

        // 6b. Create a default blank page
        var pageName = "ReportSection";
        var pageDir = Path.Combine(reportDefinitionPath, "pages", pageName);
        Directory.CreateDirectory(pageDir);

        var pageJsonObj = new Dictionary<string, object>
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.0.0/schema.json",
            ["name"] = pageName,
            ["displayName"] = "Page 1",
            ["displayOption"] = "FitToPage",
            ["height"] = 720.00,
            ["width"] = 1280.00
        };
        File.WriteAllText(Path.Combine(pageDir, "page.json"), System.Text.Json.JsonSerializer.Serialize(pageJsonObj, jsonOptions));

        // 6c. pages.json — page order and active page (inside pages/ per PBI Desktop convention)
        var pagesJsonObj = new Dictionary<string, object>
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.0.0/schema.json",
            ["pageOrder"] = new[] { pageName },
            ["activePageName"] = pageName
        };
        File.WriteAllText(Path.Combine(reportDefinitionPath, "pages", "pages.json"), System.Text.Json.JsonSerializer.Serialize(pagesJsonObj, jsonOptions));

        // 6d. version.json — PBIR definition version (2.0 is the current stable version)
        var versionJson = new PbirVersionDefinition
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/versionMetadata/1.0.0/schema.json",
            Version = "2.0.0"
        };
        File.WriteAllText(Path.Combine(reportDefinitionPath, "version.json"), System.Text.Json.JsonSerializer.Serialize(versionJson, jsonOptions));

        // 8. Generate <name>.Report/.platform (directly in Report folder, NOT in definition/)
        var reportPlatform = new PlatformFile
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json",
            Metadata = new PlatformMetadata
            {
                Type = "Report",
                DisplayName = sanitizedModelName
            },
            Config = new PlatformConfig
            {
                Version = "2.0",
                LogicalId = GenerateDeterministicGuid(projectName, "Report")
            }
        };
        File.WriteAllText(Path.Combine(reportPath, ".platform"), System.Text.Json.JsonSerializer.Serialize(reportPlatform, jsonOptions));

        // 9. Generate <name>.SemanticModel/definition.pbism
        var pbismContent = new PbismDefinition
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json",
            Version = "4.2",
            Settings = new PbismSettings()
        };
        File.WriteAllText(Path.Combine(semanticModelPath, "definition.pbism"), System.Text.Json.JsonSerializer.Serialize(pbismContent, jsonOptions));

        // 10. Generate <name>.SemanticModel/.platform
        var semanticModelPlatform = new PlatformFile
        {
            Schema = "https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json",
            Metadata = new PlatformMetadata
            {
                Type = "SemanticModel",
                DisplayName = sanitizedModelName
            },
            Config = new PlatformConfig
            {
                Version = "2.0",
                LogicalId = GenerateDeterministicGuid(projectName, "SemanticModel")
            }
        };
        File.WriteAllText(Path.Combine(semanticModelPath, ".platform"), System.Text.Json.JsonSerializer.Serialize(semanticModelPlatform, jsonOptions));
    }

    /// <summary>
    /// Validate that a PBIP output directory contains all required files in the correct locations.
    /// Uses PbipValidator for comprehensive PBIR-format validation including:
    /// TMDL deserialization, report structure, JSON parseability, and encoding checks.
    /// Returns a list of errors (empty if valid).
    /// </summary>
    public static List<string> ValidatePbipStructure(string outputPath, string projectName)
    {
        var errors = new List<string>();
        var sanitizedName = FileNameSanitizer.Sanitize(projectName);

        // Validate .pbip root file and platform files exist (not covered by PbipValidator)
        void RequireFile(string relativePath)
        {
            if (!File.Exists(Path.Combine(outputPath, relativePath)))
                errors.Add($"Missing required file: {relativePath}");
        }

        void RequireDirectory(string relativePath)
        {
            if (!Directory.Exists(Path.Combine(outputPath, relativePath)))
                errors.Add($"Missing required directory: {relativePath}");
        }

        RequireFile($"{sanitizedName}.pbip");
        RequireDirectory($"{sanitizedName}.SemanticModel");
        RequireFile($"{sanitizedName}.SemanticModel/.platform");
        RequireFile($"{sanitizedName}.SemanticModel/definition.pbism");
        RequireDirectory($"{sanitizedName}.Report");
        RequireFile($"{sanitizedName}.Report/.platform");

        // Delegate to PbipValidator for comprehensive PBIR validation
        errors.AddRange(PbipValidator.ValidateSemanticModel(outputPath));

        var reportFolder = Path.Combine(outputPath, $"{sanitizedName}.Report");
        if (Directory.Exists(reportFolder))
        {
            errors.AddRange(PbipValidator.ValidateReportStructure(reportFolder));
            errors.AddRange(PbipValidator.ValidateJsonFiles(reportFolder));
        }

        return errors;
    }

    /// <summary>
    /// Generate a deterministic GUID from project name and artifact type,
    /// ensuring identical builds produce identical output.
    /// </summary>
    private static string GenerateDeterministicGuid(string projectName, string artifactType)
    {
        var seed = $"pbt:{projectName}:{artifactType}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        // Take first 16 bytes of SHA256 to form a GUID
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    #region JSON Model Classes

    private class PbipRoot
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("artifacts")]
        public List<PbipArtifact> Artifacts { get; set; } = new();

        [JsonPropertyName("settings")]
        public PbipSettings? Settings { get; set; }
    }

    private class PbipArtifact
    {
        [JsonPropertyName("report")]
        public PbipReportRef Report { get; set; } = new();
    }

    private class PbipReportRef
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    private class PbipSettings
    {
        [JsonPropertyName("enableAutoRecovery")]
        public bool EnableAutoRecovery { get; set; }
    }

    private class PbirDefinition
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("datasetReference")]
        public DatasetReference DatasetReference { get; set; } = new();
    }

    private class DatasetReference
    {
        [JsonPropertyName("byPath")]
        public ByPathReference ByPath { get; set; } = new();
    }

    private class ByPathReference
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;
    }

    private class PbirReportDefinition
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("themeCollection")]
        public ReportThemeCollection ThemeCollection { get; set; } = new();

        [JsonPropertyName("datasetBinding")]
        public ReportDatasetBinding DatasetBinding { get; set; } = new();
    }

    private class ReportThemeCollection
    {
        [JsonPropertyName("baseTheme")]
        public ReportTheme BaseTheme { get; set; } = new();
    }

    private class ReportTheme
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "CY24SU06";

        [JsonPropertyName("reportVersionAtImport")]
        public string ReportVersionAtImport { get; set; } = "5.50";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "SharedResources";
    }

    private class ReportDatasetBinding
    {
        [JsonPropertyName("datasetReference")]
        public ReportDatasetReference DatasetReference { get; set; } = new();
    }

    private class ReportDatasetReference
    {
        [JsonPropertyName("targetType")]
        public int TargetType { get; set; } = 1;
    }

    private class PbirVersionDefinition
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    private class PbismDefinition
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("settings")]
        public PbismSettings? Settings { get; set; }
    }

    private class PbismSettings
    {
        // Empty settings object as per spec
    }

    private class PlatformFile
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; } = string.Empty;

        [JsonPropertyName("metadata")]
        public PlatformMetadata Metadata { get; set; } = new();

        [JsonPropertyName("config")]
        public PlatformConfig Config { get; set; } = new();
    }

    private class PlatformConfig
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("logicalId")]
        public string LogicalId { get; set; } = string.Empty;
    }

    private class PlatformMetadata
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    #endregion
}
