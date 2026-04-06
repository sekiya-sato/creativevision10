using System.Reflection;
using System.Runtime.InteropServices;

namespace CvServer;

public static class BuildMetadata {
	public static readonly DateTime BuildDate = GetBuildDate();
	public static readonly string OSVersion = RuntimeInformation.OSDescription;
	public static readonly string DotNetVersion = RuntimeInformation.FrameworkDescription;
	public static readonly string BuildConfiguration = GetBuildConfiguration();
	public const string CI = "false";
	public static readonly string GeneratedAt = DateTime.Now.ToString("yyyyMMddHHmmss");

	public static string GetSummary() {
		return $".net: {DotNetVersion} | Built: {BuildDate:yyyy-MM-dd HH:mm:ss} | Env: {BuildConfiguration}";
	}

	private static string GetBuildConfiguration() {
#if DEBUG
		return "Debug";
#else
		return "Release";
#endif
	}

	private static DateTime GetBuildDate() {
		var raw = GetAssemblyMetadata("BuildDate");
		if (string.IsNullOrWhiteSpace(raw)) {
			return DateTime.Now;
		}

		var parts = raw.Split(',');
		if (parts.Length != 6) {
			return DateTime.Now;
		}

		if (!int.TryParse(parts[0], out var year)) return DateTime.Now;
		if (!int.TryParse(parts[1], out var month)) return DateTime.Now;
		if (!int.TryParse(parts[2], out var day)) return DateTime.Now;
		if (!int.TryParse(parts[3], out var hour)) return DateTime.Now;
		if (!int.TryParse(parts[4], out var minute)) return DateTime.Now;
		if (!int.TryParse(parts[5], out var second)) return DateTime.Now;

		try {
			return new DateTime(year, month, day, hour, minute, second);
		}
		catch (ArgumentOutOfRangeException) {
			return DateTime.Now;
		}
	}

	private static string? GetAssemblyMetadata(string key) {
		var assembly = Assembly.GetExecutingAssembly();
		foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
			if (attribute.Key == key) {
				return attribute.Value;
			}
		}

		return null;
	}
}

