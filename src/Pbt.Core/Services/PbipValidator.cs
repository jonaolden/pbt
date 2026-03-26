using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AnalysisServices.Tabular;
using Microsoft.AnalysisServices.Tabular.Tmdl;
using NJsonSchema;

namespace Pbt.Core.Services;

/// <summary>
/// Cross-platform PBIP validation for PBIR format and TMDL semantic models.
/// Validates structure, JSON parseability, encoding, and optionally JSON schema conformance.
/// </summary>
public static class PbipValidator
{
    private static readonly Regex NamePattern = new(@"^[\w-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Run all synchronous validations on a PBIP project root directory.
    /// Includes semantic model (TMDL), report structure (PBIR), JSON parseability, and encoding.
    /// </summary>
    public static List<string> ValidateAll(string pbipRoot)
    {
        var errors = new List<string>();
        errors.AddRange(ValidateSemanticModel(pbipRoot));

        var reportFolder = Directory
            .GetDirectories(pbipRoot, "*.Report")
            .FirstOrDefault();

        if (reportFolder == null)
        {
            errors.Add("No .Report folder found in project root.");
            return errors;
        }

        errors.AddRange(ValidateReportStructure(reportFolder));
        errors.AddRange(ValidateJsonFiles(reportFolder));
        return errors;
    }

    /// <summary>
    /// Run all validations including async JSON schema validation.
    /// </summary>
    public static async Task<List<string>> ValidateAllAsync(string pbipRoot, CancellationToken ct = default)
    {
        var errors = ValidateAll(pbipRoot);

        var reportFolder = Directory
            .GetDirectories(pbipRoot, "*.Report")
            .FirstOrDefault();

        if (reportFolder != null)
        {
            errors.AddRange(await ValidateSchemasAsync(reportFolder, ct));
        }

        return errors;
    }

    /// <summary>
    /// 1 - Semantic Model: TMDL Deserialization.
    /// Deserializes the TMDL definition/ folder inside .SemanticModel to validate structure and metadata.
    /// </summary>
    public static List<string> ValidateSemanticModel(string pbipRoot)
    {
        var errors = new List<string>();

        var semanticModelDir = Directory
            .GetDirectories(pbipRoot, "*.SemanticModel")
            .FirstOrDefault();

        if (semanticModelDir == null)
        {
            errors.Add("No .SemanticModel folder found in project root.");
            return errors;
        }

        var tmdlPath = Path.Combine(semanticModelDir, "definition");
        if (!Directory.Exists(tmdlPath))
        {
            errors.Add("No definition/ folder inside .SemanticModel — is this still using TMSL (model.bim)?");
            return errors;
        }

        try
        {
            TmdlSerializer.DeserializeDatabaseFromFolder(tmdlPath);
        }
        catch (TmdlFormatException ex)
        {
            errors.Add($"TMDL syntax error in '{ex.Document}' at line {ex.Line}: {ex.Message}");
        }
        catch (TmdlSerializationException ex)
        {
            errors.Add($"TMDL metadata error in '{ex.Document}' at line {ex.Line}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Catches CompatibilityViolationException,
            // TmdlDeserializationWithReferenceErrorsException,
            // ArgumentException (path doesn't exist), etc.
            errors.Add($"Model error ({ex.GetType().Name}): {ex.Message}");
        }

        return errors;
    }

    /// <summary>
    /// 2 - Report: Required PBIR File Structure.
    /// Validates the presence and structure of required PBIR files based on the definition.pbir version.
    /// </summary>
    public static List<string> ValidateReportStructure(string reportFolder)
    {
        var errors = new List<string>();

        // definition.pbir is always required
        var pbirPath = Path.Combine(reportFolder, "definition.pbir");
        if (!File.Exists(pbirPath))
        {
            errors.Add("Missing required file: definition.pbir");
            return errors;
        }

        // Parse version to determine format
        string? version = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pbirPath));
            if (doc.RootElement.TryGetProperty("version", out var vProp))
                version = vProp.GetString();
        }
        catch (JsonException ex)
        {
            errors.Add($"definition.pbir is not valid JSON: {ex.Message}");
            return errors;
        }

        // Determine if this is PBIR or PBIR-Legacy
        var defFolder = Path.Combine(reportFolder, "definition");
        bool hasPbirFolder = Directory.Exists(defFolder);
        bool isVersionFourPlus = version != null
            && float.TryParse(version, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v >= 4.0f;

        if (!isVersionFourPlus)
        {
            // Version 1.0: must be PBIR-Legacy
            if (!File.Exists(Path.Combine(reportFolder, "report.json")))
                errors.Add("PBIR-Legacy: missing required file report.json at report root.");
            return errors;
        }

        if (!hasPbirFolder)
        {
            // Version 4.0+ but no definition/ folder — check for PBIR-Legacy fallback
            if (!File.Exists(Path.Combine(reportFolder, "report.json")))
                errors.Add("Version 4.0+ but neither definition/ folder (PBIR) nor report.json (PBIR-Legacy) found.");
            return errors; // Valid PBIR-Legacy under version 4.0
        }

        // From here on: validating PBIR format

        // Required files inside definition/
        foreach (var req in new[] { "version.json", "report.json" })
        {
            if (!File.Exists(Path.Combine(defFolder, req)))
                errors.Add($"Missing required file: definition/{req}");
        }

        // Pages
        var pagesFolder = Path.Combine(defFolder, "pages");
        if (!Directory.Exists(pagesFolder))
        {
            errors.Add("Missing required folder: definition/pages/");
            return errors;
        }

        var pageBindingNames = new HashSet<string>();

        foreach (var pageDir in Directory.GetDirectories(pagesFolder))
        {
            var pageFolderName = Path.GetFileName(pageDir);

            // Validate naming convention
            if (!NamePattern.IsMatch(pageFolderName))
                errors.Add($"Page folder name '{pageFolderName}' violates naming convention (must be [\\w-]+).");

            // page.json is required per page
            var pageJsonPath = Path.Combine(pageDir, "page.json");
            if (!File.Exists(pageJsonPath))
            {
                errors.Add($"Missing required file: pages/{pageFolderName}/page.json");
                continue;
            }

            // Check pageBinding.name uniqueness (blocking error per MS docs)
            try
            {
                using var pageDoc = JsonDocument.Parse(File.ReadAllText(pageJsonPath));
                if (pageDoc.RootElement.TryGetProperty("pageBinding", out var pb)
                    && pb.TryGetProperty("name", out var nameElem))
                {
                    var bindingName = nameElem.GetString();
                    if (bindingName != null && !pageBindingNames.Add(bindingName))
                        errors.Add($"Duplicate pageBinding.name '{bindingName}' in pages/{pageFolderName}/page.json — must be unique across all pages.");
                }
            }
            catch (JsonException)
            {
                // JSON parse errors caught in ValidateJsonFiles
            }

            // Visuals (folder is optional; visual.json required IF folder exists)
            var visualsFolder = Path.Combine(pageDir, "visuals");
            if (!Directory.Exists(visualsFolder))
                continue;

            foreach (var visualDir in Directory.GetDirectories(visualsFolder))
            {
                var visualFolderName = Path.GetFileName(visualDir);

                if (!NamePattern.IsMatch(visualFolderName))
                    errors.Add($"Visual folder name '{visualFolderName}' violates naming convention.");

                if (!File.Exists(Path.Combine(visualDir, "visual.json")))
                    errors.Add($"Missing required file: pages/{pageFolderName}/visuals/{visualFolderName}/visual.json");
            }
        }

        return errors;
    }

    /// <summary>
    /// 3 - Report: JSON Parseability and Encoding.
    /// Validates that all JSON and PBIR files are valid JSON with UTF-8 encoding (no BOM).
    /// </summary>
    public static List<string> ValidateJsonFiles(string reportFolder)
    {
        var errors = new List<string>();

        var targetFiles = Directory
            .GetFiles(reportFolder, "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(reportFolder, "*.pbir", SearchOption.AllDirectories));

        foreach (var file in targetFiles)
        {
            var rel = Path.GetRelativePath(reportFolder, file);

            // Check for UTF-8 BOM (byte order mark)
            var rawBytes = File.ReadAllBytes(file);
            if (rawBytes.Length >= 3
                && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
            {
                errors.Add($"File has UTF-8 BOM (should be UTF-8 without BOM): {rel}");
            }

            // Validate JSON syntax
            try
            {
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
            }
            catch (JsonException ex)
            {
                errors.Add($"Malformed JSON in {rel} (line {ex.LineNumber}): {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// 4 - Report: JSON Schema Validation.
    /// Validates JSON files against schemas declared in their $schema properties.
    /// Requires network access to fetch schemas from developer.microsoft.com.
    /// </summary>
    public static async Task<List<string>> ValidateSchemasAsync(
        string reportFolder,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var schemaCache = new Dictionary<string, NJsonSchema.JsonSchema>();

        var jsonFiles = Directory.GetFiles(reportFolder, "*.json", SearchOption.AllDirectories);

        foreach (var file in jsonFiles)
        {
            var content = await File.ReadAllTextAsync(file, ct);
            var rel = Path.GetRelativePath(reportFolder, file);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(content); }
            catch { continue; } // Already caught in ValidateJsonFiles

            if (!doc.RootElement.TryGetProperty("$schema", out var schemaProp))
                continue;

            var schemaUrl = schemaProp.GetString();
            if (string.IsNullOrEmpty(schemaUrl))
                continue;

            if (!schemaCache.TryGetValue(schemaUrl, out var schema))
            {
                try
                {
                    schema = await NJsonSchema.JsonSchema.FromUrlAsync(schemaUrl, ct);
                    schemaCache[schemaUrl] = schema;
                }
                catch (Exception ex)
                {
                    errors.Add($"Could not fetch schema for {rel}: {ex.Message}");
                    continue;
                }
            }

            var violations = schema.Validate(content);
            foreach (var violation in violations)
            {
                errors.Add($"Schema violation in {rel} at {violation.Path}: {violation.Kind}");
            }
        }

        return errors;
    }
}
