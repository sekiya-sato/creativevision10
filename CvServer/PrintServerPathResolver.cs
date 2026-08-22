namespace CvServer;

/// <summary>
/// PrintServer 設定の相対パスを ContentRoot 基準の絶対パスへ解決する。
/// </summary>
internal static class PrintServerPathResolver {
	public static PrintServerPaths Resolve(IConfiguration configuration, IWebHostEnvironment environment) {
		var printServer = configuration.GetSection("PrintServer");
		var configuredBaseDir = printServer.GetValue<string>("PrintBaseDir") ?? ".";
		var configuredFormDir = printServer.GetValue<string>("PrintFormDir") ?? ".";
		var configuredOutputDir = printServer.GetValue<string>("PrintOutputDir") ?? ".";
		var baseDir = Path.GetFullPath(Path.IsPathRooted(configuredBaseDir)
			? configuredBaseDir
			: Path.Combine(environment.ContentRootPath, configuredBaseDir));

		return new PrintServerPaths(
			Path.GetFullPath(Path.Combine(baseDir, configuredFormDir)),
			Path.GetFullPath(Path.Combine(baseDir, configuredOutputDir)));
	}
}

internal readonly record struct PrintServerPaths(string FormDir, string OutputDir);
