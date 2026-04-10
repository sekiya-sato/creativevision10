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
		// ToDo: テーブルの存在チェックと作成は、テーブルごとに行うのではなく、まとめて行うようにすること
		// ToDo: テーブルが追加された場合、事前作成が必要なものはここに追加すること
		var ret = false;
		// システムテーブル
		ret = db.CreateTable<SysUpdateDb>();
		ret = db.CreateTable<SysSequence>();
		// システムテーブル
		ret = db.CreateTable<SysLogin>();
		ret = db.CreateTable<SysHistJwt>();
		// マスタテーブル1
		ret = db.CreateTable<MasterSysman>();
		ret = db.CreateTable<MasterMeisho>();
		// マスタテーブル2
		ret = db.CreateTable<MasterShain>();
		ret = db.CreateTable<MasterEndCustomer>();
		ret = db.CreateTable<MasterShohin>();
		// マスタテーブル3
		ret = db.CreateTable<MasterTokui>();
		ret = db.CreateTable<MasterShiire>();
		ret = db.CreateTable<MasterConfig>();
		// トランザクションテーブル
		ret = db.CreateTable<Tran00Uriage>();
		ret = db.CreateTable<Tran01Tenuri>();
		ret = db.CreateTable<Tran03Shiire>();
		ret = db.CreateTable<Tran05Ido>();
		ret = db.CreateTable<Tran06Nyukin>();
		ret = db.CreateTable<Tran07Shiharai>();
		ret = db.CreateTable<Tran60Tana>();
		ret = db.CreateTable<Tran10IdoOut>();
		ret = db.CreateTable<Tran11IdoIn>();
		ret = db.CreateTable<Tran12Jyuchu>();
		ret = db.CreateTable<Tran13Hachu>();
		ret = db.CreateTable<TranHhtData>();
		ret = db.CreateTable<TranVulcanHht>();
		// DBの整合性を管理
		UpdateDb.WriteVersionInfoAsync(db).Wait();

	}

}
