using CvBase;
using CvBase.Share;

namespace CvServer;

public class AppGlobal {
	static InfoServer? _ver;
	public static int Counter = 0;
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
	public void Init(ExDatabase db, string appName = "", string serverVersion = "0.0.0") {
		VerInfo.Product = appName;
		VerInfo.Version = serverVersion;
		var ret = false;
		var defTable = new DefineDataTable();
		ret = defTable.Initialize(db, false);
	}

}
