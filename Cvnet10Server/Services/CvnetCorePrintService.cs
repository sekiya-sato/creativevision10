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
		var start = DateTime.Now;
		// ToDo: フォルダの位置決定のロジックを考慮 debug実行時 / windows実行時  / linux実行時
		// 環境変数を見て切り分け？
		string currentDirectory = new AppGlobal().VerInfo.BaseDir;
		string baseDirectory = Directory.GetParent(currentDirectory)?.Parent?.Parent?.Parent?.FullName ?? string.Empty;
		string baseParent = Directory.GetParent(baseDirectory)?.FullName ?? string.Empty;
		if (currentDirectory.StartsWith("var")) { // linux環境
			baseDirectory = currentDirectory;
			baseParent = Directory.GetParent(baseDirectory)?.FullName ?? string.Empty;
		}
		string dataPath = "printdata";
		_logger.LogWarning($"Print処理開始: current={currentDirectory}, BaseDirectory={baseDirectory}, BaseParent={baseParent}");
		_logger.LogWarning($"    FormPath={Path.Combine(baseParent, dataPath, "data.txt")}");
		_logger.LogWarning($"    DataPath={Path.Combine(baseParent, dataPath, "data.txt")}");
		_logger.LogWarning($"    OutputDir={Path.Combine(baseParent, "Cvnet10Server", "wrk")}");
		var context = new PrintContext {
			BasePath = "",
			FormPath = Path.Combine(baseParent, dataPath, "cvnet57prnhinShouka.qfm"),
			DataPath = Path.Combine(baseParent, dataPath, "data.txt"),
			OutputDir = Path.Combine(baseParent, "Cvnet10Server", "wrk"),
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
