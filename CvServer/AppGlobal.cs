using CvBase;
using CvBase.Share;
using CvPrints;

namespace CvServer;

public class AppGlobal {
	static InfoServer? _ver;
	public static int Counter = 0;
	public static AppGlobal Shared { get; } = new();
	/// <summary>
	/// アプリケーションのバージョン情報を取得します。
	/// </summary>
	public InfoServer VerInfo {
		get {
			if (_ver == null) {
				_ver = new InfoServer {
					BuildDate = BuildMetadata.BuildDate,
					BuildConfig = BuildMetadata.BuildConfiguration ?? string.Empty,
					StartTime = DateTime.Now,
					BaseDir = AppContext.BaseDirectory,
					MachineName = Environment.MachineName ?? string.Empty,
					UserName = Environment.UserName ?? string.Empty,
					OsVersion = BuildMetadata.OSVersion ?? string.Empty,
					DotNetVersion = BuildMetadata.DotNetVersion ?? string.Empty,
				};
			}
			return _ver;
		}
	}
	public AppGlobal() {
		Counter++;
	}

	/// <summary>
	/// 初期化 Asp.net Core の Run()の前に呼び出される
	/// テーブルはすべて存在する前提で、存在しないテーブルがあれば作成する
	/// </summary>
	public async Task InitAsync(ExDatabase db, string appName = "", string serverVersion = "0.0.0", CancellationToken ct = default) {
		VerInfo.Product = appName;
		VerInfo.Version = serverVersion;
		var defTable = new DefineDataTable();
		await defTable.InitializeAsync(db, false, ct);
	}
	/// <summary>
	/// PDFライブラリの初期化
	/// </summary>
	/// <param name="printServerConfig"></param>
	public async Task<bool> PdfInitAsync(IConfigurationSection printServerConfig) {
		if (printServerConfig == null) {
			return false;
		}
		var printService = new PrintAdapter();
		var licenses = await printService.CheckLicenseAsync();
		foreach (var license in licenses.Where(x => !x.Status)) {
			var key = printServerConfig.GetValue<string>(license.Product);
			if (string.IsNullOrEmpty(key))
				continue;
			if (!await printService.RegisterLicenseAsync(license.Product, key))
				return false;
		}
		return true;
	}
}
