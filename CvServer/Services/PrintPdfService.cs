using CodeShare;
using CvAsset;
using CvBase;
using CvPrints;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;
using System.Text;

namespace CvServer.Services;

public partial class CoreService {
	private const int PrintPostCheckIntervalMilliseconds = 500;
	private static readonly TimeSpan PrintPostCheckTimeout = TimeSpan.FromMinutes(30);

	// Product : テストが終わったら、[AllowAnonymous] を [Authorize] へ変更
	[AllowAnonymous]
	public async IAsyncEnumerable<PrintOperation> PrintPdfAsync(PrintOperation request, CallContext context = default) {
		string? clientId = context.RequestHeaders?.GetValue("x-clientid");
		var cancellationToken = context.CancellationToken;
		// 処理のステップと対応するアクションを定義
		var steps = new (string Name, Func<CancellationToken, Task<PrintResult>> Action)[] {
			("プリント前処理", ct => Task.Run(() => PrintPre(request, clientId), ct)),
			("プリント本処理", _ => ExecutePrintAsync(request)),
			("プリント後処理", ct => PrintPostAsync(request, ct)),
		};
		// ステップ数を取得
		int totalSteps = steps.Length;

		for (int i = 0; i < totalSteps; i++) {
			var start = DateTime.Now;
			var (Name, Action) = steps[i];

			// 現在のステップを実行
			var result = await Action(cancellationToken);

			// Progress を計算 (現在のステップ数 / 総ステップ数 * 100)
			int progress = (int)((i + 1) / (double)totalSteps * 100);

			// PrintOperation を返す
			int status = result.IsSuccess ? 0 : (result.Message == "印刷対象データが0件です" ? -2 : -1);
			yield return new PrintOperation {
				DataType = typeof(string),
				DataMsg = result.Message,
				Status = status,
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

	Encoding? sjis_internal = null;
	Encoding Sjis {
		get {
			if (sjis_internal == null) {
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				sjis_internal = Encoding.GetEncoding("Shift_JIS");
			}
			return sjis_internal;
		}
	}

	private static string BuildTempFolderName(string? clientId) {
		string timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
		if (string.IsNullOrWhiteSpace(clientId)) {
			return timestamp;
		}

		string sanitizedClientId = string.Concat(clientId.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
		if (string.IsNullOrWhiteSpace(sanitizedClientId)) {
			return timestamp;
		}

		return $"{sanitizedClientId}-{timestamp}";
	}


	/// <summary>
	/// Print処理本体
	/// </summary>
	/// <returns></returns>
	private async Task<PrintResult> ExecutePrintAsync(PrintOperation request) {

		// printPre で生成した一時フォルダ名を request から取得
		string timestamp = request.TempFolder;
		if (string.IsNullOrEmpty(timestamp)) {
			return new PrintResult(false, "一時フォルダ名が設定されていません");
		}
		_logger.LogWarning($"Print処理開始: Form={request.TempFormFullPath}, Out={request.TempOutputFullPath}");
		var context = new PrintContext {
			BasePath = string.Empty,
			FormPath = request.TempFormFullPath,
			DataPath = request.TempDataFullPath,
			OutputDir = Path.GetDirectoryName(request.TempOutputFullPath) ?? "",
			OutputFileName = Path.GetFileName(request.TempOutputFullPath),
		};
		var printService = new PrintAdapter();
		return await printService.ExecutePrintAsync(context);
	}
	/// <summary>
	/// Print前処理(SQLでデータ取得など)
	/// </summary>
	private PrintResult PrintPre(PrintOperation request, string? clientId) {
		var start = DateTime.Now;
		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		var paths = PrintServerPathResolver.Resolve(_configuration, _env);

		// 一時フォルダ名を生成し、request に保存して printPdf と共有
		string timestamp = BuildTempFolderName(clientId);
		request.TempFolder = timestamp;
		string tempDir = Path.Combine(paths.OutputDir, timestamp);
		Directory.CreateDirectory(tempDir);
		request.TempDataFullPath = Path.Combine(tempDir, "data.txt");
		string outname = timestamp[^17..^5]; // GUID-yyyyMMddHHmmssfff から yyyyMMddHHmm を抽出
		request.TempOutputFullPath = Path.Combine(tempDir, $"outfile{outname}.pdf");
		request.TempFormFullPath = Path.Combine(paths.FormDir, request.FormFile);

		if (param is PrintByCsvParam printParam) {
			if (string.IsNullOrWhiteSpace(printParam.CsvData)) {
				return new PrintResult(false, "印刷対象データが0件です");
			}
			File.WriteAllText(request.TempDataFullPath, printParam.CsvData, Sjis);
		}
		else if (param is QueryListSqlParam listParam) {
			var sql = (listParam.Sql ?? string.Empty).ReplaceServerSqlQuery();
			var dataList = _db.RawExecCmd(sql, listParam.Parameters).Cast<IDictionary<string, object>>().ToList();
			// RawExecCmd は例外を握り潰して [{"Error": ...}] の1行を返すため、必ず RawLastError で判定する。
			// これを見ないと SQL 失敗が「1列だけのCSV」として下流へ流れ、qfm 側の原因不明な失敗に化ける。
			if (!string.IsNullOrEmpty(_db.RawLastError)) {
				return new PrintResult(false, $"印刷データの取得に失敗しました: {_db.RawLastError}");
			}
			if (dataList.Count == 0) {
				return new PrintResult(false, "印刷対象データが0件です");
			}
			using (var writer = new StreamWriter(request.TempDataFullPath, false, Sjis)) {
				dataList.WriteDynamicCsv(writer);
			}
		}
		else {
			return new PrintResult(false, "不正なパラメータの型");
		}
		var timespan = DateTime.Now - start;
		var ret = new PrintResult(true, $"Print前処理(CSV準備): {timespan}");
		return ret;
	}

	/// <summary>
	/// Print後処理(PDFが生成されたか確認)
	/// </summary>
	private async Task<PrintResult> PrintPostAsync(PrintOperation request, CancellationToken cancellationToken) {
		var start = DateTime.Now;

		// request から一時フォルダ名を取得し、出力ファイルパスを再構築
		string timestamp = request.TempFolder;
		if (string.IsNullOrEmpty(timestamp)) {
			return new PrintResult(false, "一時フォルダ名が設定されていません");
		}
		var checkfile = request.TempOutputFullPath + "_";

		while (File.Exists(checkfile)) {
			if (DateTime.Now - start > PrintPostCheckTimeout) {
				return new PrintResult(false, $"Print後処理(PDF確認): タイムアウト {checkfile}");
			}
			await Task.Delay(PrintPostCheckIntervalMilliseconds, cancellationToken);
		}
		// 2026/06/15 commit 09eeecb7a5f5c29d0522be07ed3bdcbe1c72c74e WebpdfView のブラウザコア初期化ロジック追加 によりPDF生成が安定
		var ret = new PrintResult(true, $"{timestamp}/{Path.GetFileName(request.TempOutputFullPath)}");
		return ret;
	}

}
