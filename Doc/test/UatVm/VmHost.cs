using System.IO;
using System.Windows;
using System.Windows.Threading;
using CvWpfclient;
using CvWpfclient.Helpers;
using Microsoft.Extensions.Configuration;

namespace UatVm;

/// <summary>
/// VM駆動UATハーネスのホスト。
/// STAスレッド上でCvWpfclientの実Applicationとリソースを立ち上げ、実DI経路（gRPC）を起動し、
/// MessageExのテスト専用ルートを有効にしたうえでシナリオを実行する。
/// </summary>
/// <remarks>
/// 画面操作のエミュレーション（UIA/SendKeys）は行わない。実Viewを生成してViewModelのコマンドを駆動する。
/// シナリオは常にSTA（Dispatcher）スレッド上で動くため、ViewModelへ直接触ってよい。
/// </remarks>
public static class VmHost {
	/// <summary>ホストの起動条件。</summary>
	public sealed class Options {
		/// <summary>
		/// CvWpfclientのappsettings.jsonがあるフォルダ。ここをカレントディレクトリにする。
		/// 未指定なら実行アセンブリから上位へ辿って`CvWpfclient/appsettings.json`を探す。
		/// </summary>
		public string? ClientBaseDirectory { get; set; }
		/// <summary>接続先CvServerのURL。未指定ならappsettings.jsonの値を使う。</summary>
		public string? ServerUrl { get; set; }
		/// <summary>
		/// CvServerをハーネスが起動・停止するか。trueのとき、終了時にCtrl+C相当で正規終了させる。
		/// falseなら既に動作しているCvServerへ接続する（その場合の停止は呼び出し側の責任）。
		/// </summary>
		public bool ManageServer { get; set; }
		/// <summary>証跡JSONLの出力先。未指定なら`Doc/test/UatVm/out/<name>-<stamp>.jsonl`。</summary>
		public string? EvidencePath { get; set; }
		/// <summary>シナリオ名。証跡ファイル名と記録に使う。</summary>
		public string ScenarioName { get; set; } = "scenario";
		/// <summary>Viewを画面に表示するか。既定は表示する（実描画とバインディング評価を伴わせるため）。</summary>
		public bool ShowViews { get; set; } = true;
		/// <summary>
		/// 網羅データの投入。CvServerを起動する前に、対象DBのパスを引数として呼ばれる。
		/// 実DBへ直接書くため、サーバーが動いていない状態で実行する必要がある。
		/// </summary>
		public Action<string>? Seed { get; set; }
	}

	/// <summary>
	/// シナリオを実行する。戻り値は失敗件数（0なら全PASS）。
	/// </summary>
	public static int Run(Options options, Func<VmSession, Task> scenario) {
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(scenario);

		var baseDir = options.ClientBaseDirectory ?? LocateClientBaseDirectory()
			?? throw new InvalidOperationException(
				"CvWpfclient/appsettings.json が見つかりません。Options.ClientBaseDirectory を指定してください。");

		var exitCode = 0;
		Exception? failure = null;

		var thread = new Thread(() => {
			try {
				exitCode = RunOnStaThread(baseDir, options, scenario);
			}
			catch (Exception ex) {
				failure = ex;
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		if (failure != null) throw new InvalidOperationException("UATハーネスの実行に失敗しました。", failure);
		return exitCode;
	}

	/// <summary>
	/// 起動段階の記録。App生成やリソース読込で止まった場合に位置を特定するため、
	/// 証跡ライターを作る前から書ける独立したログへ出す。
	/// </summary>
	private static void Boot(string baseDir, string step) {
		var line = $"{DateTime.Now:HH:mm:ss.fff} {step}";
		Console.WriteLine($"[boot] {step}");
		try {
			var repoRoot = Directory.GetParent(baseDir)?.FullName ?? baseDir;
			var dir = Path.Combine(repoRoot, "Doc", "test", "UatVm", "out");
			Directory.CreateDirectory(dir);
			File.AppendAllText(Path.Combine(dir, "boot.log"), line + Environment.NewLine);
		}
		catch (IOException) { /* 起動ログの失敗で本処理を止めない */ }
	}

	private static int RunOnStaThread(string baseDir, Options options, Func<VmSession, Task> scenario) {
		// CvWpfclientはカレントディレクトリを基準に設定を読むため、必ず合わせる。
		// （CvWpfclientフォルダ以外から起動するとリソース・相対パスの解決に失敗する）
		Directory.SetCurrentDirectory(baseDir);
		Boot(baseDir, $"cwd={baseDir}");

		var evidence = new EvidenceWriter(options.EvidencePath ?? DefaultEvidencePath(baseDir, options.ScenarioName));
		var session = new VmSession(options, evidence);
		// ダイアログ抑止はApplication生成より先に有効化しておく。
		MessageExTestRoute.Enable(session.OnDialog);

		// 素の Application を使う。CvWpfclient の App を生成すると、Dispatcherを回した時点で
		// OnStartup が走り、StartupUriでMainMenuViewが開き、保存テーマの適用と起動時更新確認
		// （ダイアログを伴う）まで実行されてしまうため、UATでは使わない。
		// gRPCホストと AppGlobal.Init は App.RestartHostAsync（静的）で通せるので、これで足りる。
		Boot(baseDir, "Application 生成");
		var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

		// Viewは`{StaticResource}`でApp.xamlのリソースを参照するため、View生成前に構築が必須。
		try {
			Boot(baseDir, "リソース構築 開始");
			var summary = ClientResources.Load(app, Path.Combine(baseDir, "App.xaml"));
			Boot(baseDir, $"リソース構築 完了 辞書={summary.Dictionaries} 単体={summary.Objects} 未処理={summary.Skipped.Count}");
			evidence.Write("resources", "loaded", summary);
			if (summary.Skipped.Count > 0) {
				// App.xamlに解釈できない定義が増えた場合。Viewのバインディングが落ちる前に気づけるようにする。
				session.Fail("resources:未処理の定義", string.Join(" / ", summary.Skipped));
			}
		}
		catch (Exception ex) {
			Boot(baseDir, $"リソース構築 失敗: {ex.GetType().Name}: {ex.Message}");
			session.Fail("resources", ex.ToString());
			var failed = session.Complete();
			MessageExTestRoute.Disable();
			evidence.Dispose();
			return failed;
		}

		// 網羅データの投入は、CvServerがDBを開く前に済ませる。
		if (options.Seed != null) {
			var repoRootForSeed = Directory.GetParent(baseDir)?.FullName ?? baseDir;
			var dbPath = Path.Combine(repoRootForSeed, "CvServer", "server-user163.db");
			try {
				Boot(baseDir, "シード投入 開始");
				options.Seed(dbPath);
				Boot(baseDir, "シード投入 完了");
			}
			catch (Exception ex) {
				Boot(baseDir, $"シード投入 失敗: {ex.GetType().Name}: {ex.Message}");
				session.Fail("seed", ex.ToString());
				var failed = session.Complete();
				MessageExTestRoute.Disable();
				evidence.Dispose();
				return failed;
			}
		}

		// CvServerを面倒見る場合はここで起動する。終了は必ずfinallyで正規経路を通す。
		CvServerProcess? server = null;
		if (options.ManageServer) {
			var repoRoot = Directory.GetParent(baseDir)?.FullName ?? baseDir;
			var url = options.ServerUrl ?? "http://127.0.0.1:5002";
			options.ServerUrl = url;
			try {
				server = CvServerProcess.Start(repoRoot, url, message => {
					Boot(baseDir, message);
					evidence.Write("server", message);
				});
			}
			catch (Exception ex) {
				Boot(baseDir, $"CvServer 起動失敗: {ex.Message}");
				session.Fail("server:start", ex.ToString());
				var failed = session.Complete();
				MessageExTestRoute.Disable();
				evidence.Dispose();
				return failed;
			}
		}

		var exitCode = 0;
		var dispatcher = Dispatcher.CurrentDispatcher;
		dispatcher.InvokeAsync(async () => {
			try {
				// 実DI経路でホストを起動し、AppGlobal.Init を通す（gRPCクライアントはここで解決可能になる）。
				// AppのOnStartupがappsettingsのURLでホストを起動しているため、ここで指定URLへ張り替える。
				Boot(baseDir, "RestartHostAsync 開始");
				await App.RestartHostAsync(CancellationToken.None, BuildSetting(baseDir, options.ServerUrl));
				Boot(baseDir, "RestartHostAsync 完了");
				evidence.Write("host", "started", new { baseDir, url = options.ServerUrl ?? "(appsettings)" });

				await scenario(session);
			}
			catch (Exception ex) {
				session.Fail("unhandled", ex.ToString());
			}
			finally {
				MessageExTestRoute.Disable();
				// クライアント側のホストを止めてから、サーバーをCtrl+C相当で正規終了させる。
				server?.Stop();
				exitCode = session.Complete();
				evidence.Dispose();
				dispatcher.InvokeShutdown();
			}
		});

		Dispatcher.Run();
		return exitCode;
	}

	/// <summary>
	/// appsettings.json / appsettings.Development.json を読み、必要ならURLを差し替えた構成を作る。
	/// App.RestartHostAsync に設定を渡すと appsettings は読まれないため、ここで引き継ぐ。
	/// </summary>
	private static Dictionary<string, string?> BuildSetting(string baseDir, string? serverUrl) {
		var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
		var config = new ConfigurationBuilder()
			.SetBasePath(baseDir)
			.AddJsonFile("appsettings.json", optional: true)
			.AddJsonFile($"appsettings.{environment}.json", optional: true)
			.Build();

		var setting = config.AsEnumerable()
			.Where(x => x.Value != null)
			.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

		if (!string.IsNullOrWhiteSpace(serverUrl)) setting["ConnectionStrings:Url"] = serverUrl;
		if (!setting.ContainsKey("ConnectionStrings:Url")) {
			throw new InvalidOperationException($"ConnectionStrings:Url が構成にありません（basePath={baseDir}）。");
		}
		return setting;
	}

	private static string DefaultEvidencePath(string baseDir, string scenarioName) {
		// baseDir は <repo>/CvWpfclient なので、その親がリポジトリルート。
		var repoRoot = Directory.GetParent(baseDir)?.FullName ?? baseDir;
		var dir = Path.Combine(repoRoot, "Doc", "test", "UatVm", "out");
		Directory.CreateDirectory(dir);
		var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		return Path.Combine(dir, $"{scenarioName}-{stamp}.jsonl");
	}

	private static string? LocateClientBaseDirectory() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null) {
			var candidate = Path.Combine(dir.FullName, "CvWpfclient", "appsettings.json");
			if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
			dir = dir.Parent;
		}
		return null;
	}
}
