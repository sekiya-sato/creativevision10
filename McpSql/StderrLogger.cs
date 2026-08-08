using Microsoft.Extensions.Logging;

namespace McpSql;

/// <summary>
/// 診断用の最小 ILoggerFactory。出力先は必ず stderr (stdout は JSON-RPC 専用のため)。
/// 環境変数 MCPSQL_DEBUG が設定されているときだけ Program から使われる。
/// </summary>
sealed class StderrLoggerFactory(LogLevel minLevel) : ILoggerFactory {

	public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, minLevel);

	public void AddProvider(ILoggerProvider provider) {
		// 追加のプロバイダは扱わない
	}

	public void Dispose() {
	}

	sealed class StderrLogger(string category, LogLevel minLevel) : ILogger {

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel && logLevel != LogLevel.None;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
			if (!IsEnabled(logLevel))
				return;
			var message = formatter(state, exception);
			Console.Error.WriteLine($"[{logLevel}] {category}: {message}");
			if (exception != null)
				Console.Error.WriteLine(exception);
		}
	}
}
