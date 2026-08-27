using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace UatVm;

/// <summary>
/// UAT実行中のCvServerを起動・停止する。
/// </summary>
/// <remarks>
/// <para>
/// 停止は強制終了ではなく、人の Ctrl+C と同じ経路（コンソール制御イベント）で行う。
/// 強制終了するとKestrelの停止処理とSQLiteのWAL後始末が飛び、`server-user163.db-wal` が
/// 残ったままになる。10GB級の実DBを対象にするため、毎回正規の終了経路を通す。
/// </para>
/// <para>
/// そのために <c>CREATE_NEW_PROCESS_GROUP</c> で起動し、そのグループへ
/// <c>CTRL_BREAK_EVENT</c> を送る。<see cref="Process"/> はこのフラグを指定できないため
/// <c>CreateProcess</c> を直接呼ぶ。標準出力・標準エラーは親のハンドルを継承させるので、
/// 呼び出し側でリダイレクトしていればログはそのまま取れる。
/// </para>
/// <para>
/// なお <c>CTRL_C_EVENT</c> はプロセスグループを指定して送れない（グループ0＝自分の
/// コンソール全体にしか送れない）ため、グループ指定できる <c>CTRL_BREAK_EVENT</c> を使う。
/// ASP.NET Core の ConsoleLifetime は <see cref="Console.CancelKeyPress"/> 経由で
/// どちらも受け取り、同じ graceful shutdown へ入る。
/// </para>
/// </remarks>
public sealed class CvServerProcess : IDisposable {
	private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
	private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
	private const uint CTRL_BREAK_EVENT = 1;

	private readonly Action<string> _trace;
	private bool _stopped;

	private CvServerProcess(int processId, string url, Action<string> trace) {
		ProcessId = processId;
		Url = url;
		_trace = trace;
	}

	/// <summary>起動したCvServerのプロセスID。</summary>
	public int ProcessId { get; }
	/// <summary>待ち受けURL。</summary>
	public string Url { get; }

	/// <summary>
	/// CvServerを起動し、待ち受け開始まで待つ。
	/// </summary>
	/// <param name="repoRoot">リポジトリのルート。</param>
	/// <param name="url">待ち受けURL（例: http://127.0.0.1:5002）。</param>
	/// <param name="trace">経過の記録先。</param>
	/// <param name="timeoutSeconds">待ち受け開始を待つ秒数。</param>
	public static CvServerProcess Start(string repoRoot, string url, Action<string> trace, int timeoutSeconds = 120) {
		var serverDir = Path.Combine(repoRoot, "CvServer");
		var dll = Path.Combine(serverDir, "bin", "Debug", "net10.0", "CvServer.dll");
		if (!File.Exists(dll)) {
			throw new FileNotFoundException(
				$"CvServer がビルドされていません。`dotnet build CvServer\\CvServer.csproj` を先に実行してください。", dll);
		}

		var port = new Uri(url).Port;
		if (IsListening(port)) {
			throw new InvalidOperationException(
				$"ポート {port} は既に使用されています。動作中のCvServerを停止してから実行してください。");
		}

		// CvServerフォルダをカレントにしないと接続文字列(sqlite)の解決に失敗する。
		// 環境変数はDevelopment固定。ASPNETCORE_ENVIRONMENTも必要。
		var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
			["DOTNET_ENVIRONMENT"] = "Development",
			["ASPNETCORE_ENVIRONMENT"] = "Development",
			["Kestrel__Endpoints__Http__Url"] = url,
		};

		var processId = CreateProcessInNewGroup(
			commandLine: $"dotnet \"{dll}\"",
			workingDirectory: serverDir,
			environment: environment);

		var server = new CvServerProcess(processId, url, trace);
		trace($"CvServer 起動 PID={processId} url={url} cwd={serverDir}");

		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (DateTime.UtcNow < deadline) {
			if (IsListening(port)) {
				trace($"CvServer 待ち受け開始 port={port}");
				return server;
			}
			if (HasExited(processId)) {
				throw new InvalidOperationException("CvServer が待ち受け前に終了しました。ログを確認してください。");
			}
			Thread.Sleep(300);
		}

		server.Stop();
		throw new TimeoutException($"CvServer が {timeoutSeconds} 秒以内に待ち受けを開始しませんでした。");
	}

	/// <summary>
	/// Ctrl+C 相当のイベントを送って正規終了させる。応じない場合だけ強制終了へ落とす。
	/// </summary>
	public void Stop(int timeoutSeconds = 60) {
		if (_stopped) return;
		_stopped = true;

		if (HasExited(ProcessId)) {
			_trace("CvServer は既に終了しています。");
			return;
		}

		if (GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, (uint)ProcessId)) {
			_trace($"CvServer へ終了要求を送信 PID={ProcessId}");
		}
		else {
			_trace($"終了要求の送信に失敗 PID={ProcessId} err={Marshal.GetLastWin32Error()}");
		}

		var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		while (DateTime.UtcNow < deadline) {
			if (HasExited(ProcessId)) {
				_trace($"CvServer 正常終了 PID={ProcessId}");
				return;
			}
			Thread.Sleep(200);
		}

		// ここへ来るとWALが残る可能性がある。黙って強制終了せず、必ず記録する。
		_trace($"CvServer が {timeoutSeconds} 秒で終了しないため強制終了します（WALが残る可能性あり） PID={ProcessId}");
		try {
			using var process = Process.GetProcessById(ProcessId);
			process.Kill(entireProcessTree: true);
		}
		catch (ArgumentException) { /* 既に終了 */ }
		catch (InvalidOperationException) { /* 既に終了 */ }
	}

	public void Dispose() => Stop();

	private static bool HasExited(int processId) {
		try {
			using var process = Process.GetProcessById(processId);
			return process.HasExited;
		}
		catch (ArgumentException) {
			return true;
		}
	}

	private static bool IsListening(int port) {
		try {
			using var client = new TcpClient();
			return client.ConnectAsync("127.0.0.1", port).Wait(500) && client.Connected;
		}
		catch (SocketException) {
			return false;
		}
		catch (AggregateException) {
			return false;
		}
	}

	/// <summary>
	/// CREATE_NEW_PROCESS_GROUP を付けてプロセスを起動する。標準出力・標準エラーは親から継承する。
	/// </summary>
	private static int CreateProcessInNewGroup(string commandLine, string workingDirectory, Dictionary<string, string?> environment) {
		var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

		// 親の環境へ上書き分を足した環境ブロックを作る（\0区切り、末尾\0\0）。
		var merged = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
			merged[(string)entry.Key] = entry.Value as string;
		}
		foreach (var (key, value) in environment) merged[key] = value;

		var block = new StringBuilder();
		foreach (var (key, value) in merged) block.Append(key).Append('=').Append(value).Append('\0');
		block.Append('\0');

		var environmentPtr = Marshal.StringToHGlobalUni(block.ToString());
		try {
			var created = CreateProcess(
				lpApplicationName: null,
				lpCommandLine: new StringBuilder(commandLine),
				lpProcessAttributes: IntPtr.Zero,
				lpThreadAttributes: IntPtr.Zero,
				bInheritHandles: true,
				dwCreationFlags: CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT,
				lpEnvironment: environmentPtr,
				lpCurrentDirectory: workingDirectory,
				lpStartupInfo: ref startupInfo,
				lpProcessInformation: out var processInformation);
			if (!created) {
				throw new InvalidOperationException(
					$"CvServer の起動に失敗しました。err={Marshal.GetLastWin32Error()} cmd={commandLine}");
			}
			CloseHandle(processInformation.hThread);
			CloseHandle(processInformation.hProcess);
			return processInformation.dwProcessId;
		}
		finally {
			Marshal.FreeHGlobal(environmentPtr);
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern bool CreateProcess(
		string? lpApplicationName,
		StringBuilder lpCommandLine,
		IntPtr lpProcessAttributes,
		IntPtr lpThreadAttributes,
		bool bInheritHandles,
		uint dwCreationFlags,
		IntPtr lpEnvironment,
		string lpCurrentDirectory,
		ref STARTUPINFO lpStartupInfo,
		out PROCESS_INFORMATION lpProcessInformation);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr hObject);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct STARTUPINFO {
		public int cb;
		public string? lpReserved;
		public string? lpDesktop;
		public string? lpTitle;
		public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
		public short wShowWindow, cbReserved2;
		public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct PROCESS_INFORMATION {
		public IntPtr hProcess, hThread;
		public int dwProcessId, dwThreadId;
	}
}
