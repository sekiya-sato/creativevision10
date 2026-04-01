using Cvnet10Base;
using Cvnet10Base.Share;
using Cvnet10DomainLogic;

namespace Cvnet10Server;

public class AppGlobal {
	readonly string appName = "Cvnet10Server";
	// ToDo: バージョン番号は手動で更新すること
	readonly string appVer = "0.0.099";
	static VersionInfo? _ver;
	public static int Counter = 0;
	/// <summary>
	/// アプリケーションのバージョン情報を取得します。
	/// </summary>
	public VersionInfo VerInfo {
		get {
			if (_ver == null) {
				_ver = new VersionInfo {
					Product = appName,
					Version = appVer,
					BuildDate = BuildMetadata.BuildDate,
					StartTime = DateTime.Now,
					BaseDir = AppContext.BaseDirectory
				};
#pragma warning disable RS1035 // アナライザーに対して禁止された API を使用しない
				_ver.MachineName = Environment.MachineName ?? string.Empty;
				_ver.UserName = Environment.UserName ?? string.Empty;
				_ver.OsVersion = BuildMetadata.OSVersion ?? string.Empty;
				_ver.DotNetVersion = BuildMetadata.DotNetVersion ?? string.Empty;
#pragma warning restore RS1035 // アナライザーに対して禁止された API を使用しない
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
	public void Init(ExDatabase db) {
		// ToDo: テーブルの存在チェックと作成は、テーブルごとに行うのではなく、まとめて行うようにすること
		var ret = false;
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
	}

}
