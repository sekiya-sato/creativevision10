using Microsoft.Extensions.Logging;
using NLog;

namespace CvBase;

/// <summary>
/// DIが使えない/使わないクラスでNLogを利用するためのILogger<T>実装、Nlog依存を集約
/// </summary>
/// <typeparam name="T"></typeparam>
public class NLogExtender<T> : ILogger<T> {
	private readonly Logger _nlogLogger;

	public NLogExtender() {
		_nlogLogger = LogManager.GetLogger(typeof(T).Name);
	}
	public NLogExtender(string loggerName) {
		_nlogLogger = LogManager.GetLogger(loggerName);
	}

	public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance; // Null許容性制約を追加

	public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) {
		// LogLevelをNLogのLogLevelに変換して判定
		var nlogLevel = ConvertLogLevel(logLevel);
		return _nlogLogger.IsEnabled(nlogLevel);
	}

	public void Log<TState>(
		Microsoft.Extensions.Logging.LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter) {
		if (!IsEnabled(logLevel)) return;

		var message = formatter?.Invoke(state, exception) ?? state?.ToString() ?? string.Empty;
		if (string.IsNullOrEmpty(message) && exception == null) return;

		var nlogLevel = ConvertLogLevel(logLevel);

		// 例外がある場合は例外付きでログ
		if (exception != null) {
			_nlogLogger.Log(nlogLevel, exception, message);
		}
		else {
			_nlogLogger.Log(nlogLevel, message);
		}
	}

	private static NLog.LogLevel ConvertLogLevel(Microsoft.Extensions.Logging.LogLevel logLevel) {
		return logLevel switch {
			Microsoft.Extensions.Logging.LogLevel.Trace => NLog.LogLevel.Trace,
			Microsoft.Extensions.Logging.LogLevel.Debug => NLog.LogLevel.Debug,
			Microsoft.Extensions.Logging.LogLevel.Information => NLog.LogLevel.Info,
			Microsoft.Extensions.Logging.LogLevel.Warning => NLog.LogLevel.Warn,
			Microsoft.Extensions.Logging.LogLevel.Error => NLog.LogLevel.Error,
			Microsoft.Extensions.Logging.LogLevel.Critical => NLog.LogLevel.Fatal,
			_ => NLog.LogLevel.Off
		};
	}

	public void Shutdown() {
		LogManager.Shutdown();
	}

	private sealed class NullScope : IDisposable {
		public static readonly NullScope Instance = new();
		private NullScope() { }
		public void Dispose() { }
	}
}
