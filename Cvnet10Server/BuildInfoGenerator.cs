using Microsoft.CodeAnalysis;

[Generator]
public class BuildInfoGenerator : ISourceGenerator {
	public void Initialize(GeneratorInitializationContext context) {
	}

	public void Execute(GeneratorExecutionContext context) {
		var options = context.AnalyzerConfigOptions.GlobalOptions;

		options.TryGetValue("build_property.BuildDate", out var buildDateStr);
		options.TryGetValue("build_property.MachineName", out var machineName);
		options.TryGetValue("build_property.UserName", out var userName);
		options.TryGetValue("build_property.OSDescription", out var osVersion);
		options.TryGetValue("build_property.FrameworkDescription", out var dotnetVersion);
		options.TryGetValue("build_property.BuildConfiguration", out var config);
		options.TryGetValue("build_property.CI", out var ci);

		// デフォルト値
		buildDateStr ??= "2023,1,1,0,0,0";
		machineName ??= "Unknown";
		userName ??= "Unknown";
		osVersion ??= "Unknown";
		dotnetVersion ??= "Unknown";
		config ??= "Debug";
		ci ??= "false";

		var source = $@"
using System;

namespace Cvnet10Server;

public static class BuildMetadata {{
    public static readonly DateTime BuildDate = new DateTime({buildDateStr});
    public const string MachineName = ""{EscapeString(machineName)}"";
    public const string UserName = ""{EscapeString(userName)}"";
    public const string OSVersion = @""{EscapeString(osVersion)}"";
    public const string DotNetVersion = ""{EscapeString(dotnetVersion)}"";
    public const string BuildConfiguration = ""{EscapeString(config)}"";
    public const string CI = ""{EscapeString(ci)}"";

    public static string GetSummary() {{
        return $ "".net: {{DotNetVersion}} | Built: {{BuildDate:yyyy-MM-dd HH:mm:ss}} | Env: {{BuildConfiguration}} | Machine: {{MachineName}} ({{UserName}})"";
    }}
}}
";
		context.AddSource("BuildInfo.g.cs", source);
	}

	private static string EscapeString(string s) => s.Replace("\"", "\\\"");
}
