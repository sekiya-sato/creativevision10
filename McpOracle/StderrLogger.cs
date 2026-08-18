using Microsoft.Extensions.Logging;

namespace McpOracle;

/// <summary>環境変数 MCPORACLE_DEBUG 指定時だけ stderr へ出力するロガー。</summary>
sealed class StderrLoggerFactory(LogLevel minimumLevel) : ILoggerFactory {
	public void AddProvider(ILoggerProvider provider) { }
	public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, minimumLevel);
	public void Dispose() { }
}

sealed class StderrLogger(string categoryName, LogLevel minimumLevel) : ILogger {
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
	public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
		if (IsEnabled(logLevel)) Console.Error.WriteLine($"[McpOracle] {logLevel} {categoryName}: {formatter(state, exception)} {exception}");
	}
}
