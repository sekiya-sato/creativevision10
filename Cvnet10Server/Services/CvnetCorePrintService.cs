using CodeShare;
using Cvnet10Prints;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;


namespace Cvnet10Server.Services;

public partial class CvnetCoreService {

	[AllowAnonymous]
	public async IAsyncEnumerable<PrintOperation> PrintPdfAsync(PrintOperation request, CallContext context = default) {
		// 処理のステップと対応するアクションを定義
		var steps = new (string Name, Func<PrintResult> Action)[] {
			("プリント前処理", () => printPre()),
			("プリント本処理", () => printPdf()),
		};
		// ステップ数を取得
		int totalSteps = steps.Length;

		for (int i = 0; i < totalSteps; i++) {
			var start = DateTime.Now;
			var (Name, Action) = steps[i];

			// 現在のステップを実行
			var result = await Task.Run(Action, context.CancellationToken);

			// Progress を計算 (現在のステップ数 / 総ステップ数 * 100)
			int progress = (int)((i + 1) / (double)totalSteps * 100);

			// PrintOperation を返す
			yield return new PrintOperation {
				DataType = typeof(string),
				DataMsg = result.Message,
				Status = result.IsSuccess ? 0 : -1,
				StatusString = $"{Name} (処理時間: {DateTime.Now - start})",
				Progress = progress, // 進捗率を設定
				IsCompleted = i == totalSteps - 1, // 最終ステップで完了フラグを設定
			};
			if (!result.IsSuccess) {
				yield break; // エラーが発生したら以降の処理を中止
			}
		}
		//throw new NotImplementedException();
	}

	/// <summary>
	/// Print処理本体
	/// </summary>
	/// <returns></returns>
	private PrintResult printPdf() {
		var printServer = _configuration.GetSection("PrintServer");
		string contentRootPath = _env.ContentRootPath;
		string configuredBaseDir = printServer.GetValue<string>("PrintBaseDir") ?? ".";
		string configuredFormDir = printServer.GetValue<string>("PrintFormDir") ?? "printdata";
		string configuredDataDir = printServer.GetValue<string>("PrintDataDir") ?? "printdata";
		string configuredOutputDir = printServer.GetValue<string>("PrintOutputDir") ?? Path.Combine("Cvnet10Server", "wrk");

		string resolvedBaseDir = Path.GetFullPath(Path.IsPathRooted(configuredBaseDir)
			? configuredBaseDir
			: Path.Combine(contentRootPath, configuredBaseDir));
		string resolvedFormDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredFormDir));
		string resolvedDataDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredDataDir));
		string resolvedOutputDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredOutputDir));

		Directory.CreateDirectory(resolvedOutputDir);

		string formPath = Path.Combine(resolvedFormDir, "cvnet57prnhinShouka.qfm");
		string dataPath = Path.Combine(resolvedDataDir, "data.txt");
		_logger.LogWarning("Print処理開始: ContentRoot={ContentRoot}, PrintBaseDir={PrintBaseDir}, ResolvedBaseDir={ResolvedBaseDir}", contentRootPath, configuredBaseDir, resolvedBaseDir);
		_logger.LogWarning("    FormPath={FormPath}", formPath);
		_logger.LogWarning("    DataPath={DataPath}", dataPath);
		_logger.LogWarning("    OutputDir={OutputDir}", resolvedOutputDir);
		var context = new PrintContext {
			BasePath = string.Empty,
			FormPath = formPath,
			DataPath = dataPath,
			OutputDir = resolvedOutputDir,
			OutputFileName = "test_server.pdf",
		};
		var printService = new PrintAdapter();
		var ret = printService.ExecutePrintAsync(context);
		return ret.Result;
	}
	/// <summary>
	/// Print前処理(SQLでデータ取得など)
	/// </summary>
	private PrintResult printPre() {
		var start = DateTime.Now;
		SleepTask(10);
		var timespan = DateTime.Now - start;
		var ret = new PrintResult(true, $"Print前処理(ダミーSQL処理): {timespan}");
		return ret;
	}

}
