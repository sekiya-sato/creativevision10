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

	[AllowAnonymous]
	public async IAsyncEnumerable<PrintOperation> PrintPdfAsync(PrintOperation request, CallContext context = default) {
		// 処理のステップと対応するアクションを定義
		var steps = new (string Name, Func<PrintOperation, PrintResult> Action)[] {
			("プリント前処理", (req) => printPre(req)),
			("プリント本処理", (req) => printPdf(req)),
			("プリント後処理", (req) => printPost(req))
		};
		// ステップ数を取得
		int totalSteps = steps.Length;

		for (int i = 0; i < totalSteps; i++) {
			var start = DateTime.Now;
			var (Name, Action) = steps[i];

			// 現在のステップを実行
			var result = await Task.Run(() => Action(request), context.CancellationToken);

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


	/// <summary>
	/// Print処理本体
	/// </summary>
	/// <returns></returns>
	private PrintResult printPdf(PrintOperation request) {

		var printServer = _configuration.GetSection("PrintServer");
		string contentRootPath = _env.ContentRootPath;
		string configuredBaseDir = printServer.GetValue<string>("PrintBaseDir") ?? ".";
		string configuredFormDir = printServer.GetValue<string>("PrintFormDir") ?? ".";
		string configuredDataDir = printServer.GetValue<string>("PrintDataDir") ?? ".";
		string configuredOutputDir = printServer.GetValue<string>("PrintOutputDir") ?? ".";
		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		string resolvedBaseDir = Path.GetFullPath(Path.IsPathRooted(configuredBaseDir)
			? configuredBaseDir
			: Path.Combine(contentRootPath, configuredBaseDir));
		string resolvedFormDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredFormDir));
		string resolvedDataDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredDataDir));
		string resolvedOutputDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredOutputDir));

		// ToDo: 現在はテスト的に固定で設定、request から受け取るようにする
		string form = string.Empty;
		string data = "data.txt";
		string outFile = string.Empty;
		outFile = $"outfile{DateTime.Now:yyyyMMddHHmmssfff}.pdf";
		if (param is PrintByCsvParam printParam) {
			form = request.FormFile;
		}
		else if (param is QueryListSqlParam listParam) {
			form = request.FormFile;
		}
		else {
			// エラー: パラメータの型が不正
			return new PrintResult(false, "不正なパラメータの型");
		}

		// --------------------------------
		Directory.CreateDirectory(resolvedOutputDir);
		string formPath = Path.Combine(resolvedFormDir, form);
		string dataPath = Path.Combine(resolvedDataDir, data);
		string outfileName = outFile;
		_logger.LogWarning($"Print処理開始: ContentRoot={contentRootPath}, PrintBaseDir={configuredBaseDir}, ResolvedBaseDir={resolvedBaseDir}");
		_logger.LogWarning($"    FormPath={formPath}");
		_logger.LogWarning($"    DataPath={dataPath}");
		_logger.LogWarning($"    OutputDir={resolvedOutputDir}, File={outfileName}");
		outputFile = Path.Combine(resolvedOutputDir, outfileName);
		var context = new PrintContext {
			BasePath = string.Empty,
			FormPath = formPath,
			DataPath = dataPath,
			OutputDir = resolvedOutputDir,
			OutputFileName = outfileName,
		};
		var printService = new PrintAdapter();
		var licenseTask = printService.CheckLicenseAsync().Result;
		foreach (var lic in licenseTask)
			if (!lic.Status)
				printService.RegisterLicenseAsync(lic.Product, printServer.GetValue<string>(lic.Product) ?? "").Wait();


		var ret = printService.ExecutePrintAsync(context);
		return ret.Result;
	}
	/// <summary>
	/// Print前処理(SQLでデータ取得など)
	/// </summary>
	private PrintResult printPre(PrintOperation request) {
		var start = DateTime.Now;
		var printServer = _configuration.GetSection("PrintServer");
		string contentRootPath = _env.ContentRootPath;
		string configuredBaseDir = printServer.GetValue<string>("PrintBaseDir") ?? ".";
		string configuredDataDir = printServer.GetValue<string>("PrintDataDir") ?? ".";
		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		string resolvedBaseDir = Path.GetFullPath(Path.IsPathRooted(configuredBaseDir)
			? configuredBaseDir
			: Path.Combine(contentRootPath, configuredBaseDir));
		string resolvedDataDir = Path.GetFullPath(Path.Combine(resolvedBaseDir, configuredDataDir));
		string data = "data.txt";
		if (param is PrintByCsvParam printParam) {
			File.WriteAllText(Path.Combine(resolvedDataDir, data), printParam.CsvData, Sjis);
		}
		else if (param is QueryListSqlParam listParam) {
			var sql = (listParam.Sql ?? string.Empty).ReplaceServerSqlQuery();
			var dataList = _db.Fetch<dynamic>(sql, listParam.Parameters).Cast<IDictionary<string, object>>().ToList();
			using (var writer = new StreamWriter(Path.Combine(resolvedDataDir, data), false, Sjis)) {
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
	string outputFile = "";

	/// <summary>
	/// Print後処理(PDFが生成されたか確認)
	/// </summary>
	private PrintResult printPost(PrintOperation request) {
		var start = DateTime.Now;
		var checkfile = outputFile + "_";

		while (File.Exists(checkfile)) {
			if (DateTime.Now - start > PrintPostCheckTimeout) {
				return new PrintResult(false, $"Print後処理(PDF確認): タイムアウト {checkfile}");
			}
			Thread.Sleep(PrintPostCheckIntervalMilliseconds);
		}

		var timespan = DateTime.Now - start;
		var ret = new PrintResult(true, $"Print後処理(PDF確認): {timespan}");
		return ret;
	}



}
