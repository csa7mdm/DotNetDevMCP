// Copyright (c) 2025 Ahmed Mustafa

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotNetDevMCP.Core;

/// <summary>
/// Validates the values that name things in dotnet/MSBuild command lines (framework, runtime,
/// configuration, MSBuild property names) before they become process arguments. Paired with
/// <see cref="ProcessStartInfo.ArgumentList"/> (each value exactly one argument, no shell involved),
/// this closes argument injection like a framework of "net10.0 -p:CustomBeforeMicrosoftCommonTargets=
/// C:\evil.targets" that would otherwise import an attacker's targets file. Shared by BuildService and
/// TestRunner, both of which accept framework/runtime/configuration from callers.
/// </summary>
public static class DotnetArgumentValidation
{
    // TFM (net10.0, net8.0-windows) or RID (win-x64, linux-x64): letters, digits, dot, dash. Must start
    // with an alphanumeric so a value can never itself look like an option (e.g. "--logger:x").
    private static readonly Regex TfmOrRidShape = new(@"^[A-Za-z0-9][A-Za-z0-9.-]*$", RegexOptions.Compiled);
    private static readonly Regex ConfigurationShape = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex MSBuildPropertyNameShape = new(@"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    public static string? ValidateFramework(string? framework) =>
        framework is null || TfmOrRidShape.IsMatch(framework)
            ? null
            : $"Invalid framework '{framework}': expected a target framework moniker like net10.0 or net8.0-windows.";

    public static string? ValidateRuntime(string? runtime) =>
        runtime is null || TfmOrRidShape.IsMatch(runtime)
            ? null
            : $"Invalid runtime '{runtime}': expected a runtime identifier like win-x64 or linux-x64.";

    public static string? ValidateConfiguration(string? configuration) =>
        configuration is null || ConfigurationShape.IsMatch(configuration)
            ? null
            : $"Invalid configuration '{configuration}': letters, digits, '_' and '-' only.";

    public static string? ValidatePropertyName(string name) =>
        MSBuildPropertyNameShape.IsMatch(name)
            ? null
            : $"Invalid MSBuild property name '{name}': must start with a letter or '_', followed by letters, digits, '_', '.' or '-'.";

    /// <summary>
    /// MSBuild splits a "-p:Name=Value" switch on ';' and ',' into a property list, so either one unescaped in the
    /// value lets a caller smuggle in a second property (e.g. "1,CustomBeforeMicrosoftCommonTargets=C:\evil.targets").
    /// ponytail: only the two separators are escaped, not the full MSBuild %XX grammar ($, @, etc.); that covers
    /// the property-list-injection case this hardening pass targets.
    /// </summary>
    public static string EscapePropertyValue(string value) => value.Replace(";", "%3B").Replace(",", "%2C");
}
