using System.CommandLine;
using Pbt.Commands;
using Pbt.Infrastructure;

var rootCommand = new RootCommand(
    "pbt - Power BI Build Tool for semantic model composition");

// Global options
var outputOption = new Option<string?>(
    "--output-format",
    "Output format: text (default) or json");
outputOption.AddAlias("-of");

var ciOption = new Option<bool>(
    "--ci",
    "CI mode: no color, non-interactive, strict exit codes");

rootCommand.AddGlobalOption(outputOption);
rootCommand.AddGlobalOption(ciOption);

// Set up global middleware to configure output mode
rootCommand.AddValidator((result) =>
{
    var outputFormat = result.GetValueForOption(outputOption);
    var ciMode = result.GetValueForOption(ciOption);

    // Also detect CI environment variable
    if (!ciMode && Environment.GetEnvironmentVariable("CI")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
    {
        ciMode = true;
    }

    OutputFormatter.JsonMode = outputFormat?.Equals("json", StringComparison.OrdinalIgnoreCase) == true;
    OutputFormatter.CiMode = ciMode;
});

// Add all commands
rootCommand.AddCommand(InitCommand.Create());
rootCommand.AddCommand(BuildCommand.Create());
rootCommand.AddCommand(ValidateCommand.Create());
rootCommand.AddCommand(ListCommand.Create());
rootCommand.AddCommand(ImportCommand.Create());
rootCommand.AddCommand(LineageCommand.Create());
rootCommand.AddCommand(RunOperationCommand.Create());
rootCommand.AddCommand(DiffCommand.Create());

return await rootCommand.InvokeAsync(args);
