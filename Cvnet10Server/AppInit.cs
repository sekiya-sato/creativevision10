using Cvnet10Base;
using Cvnet10DomainLogic;

namespace Cvnet10Server;
public class AppInit {
	private readonly IConfiguration _configuration;
	// ToDo: バージョン番号は手動で更新すること
	readonly string verStr = "0.0.099";
	readonly DateTime buildDate = BuildMetadata.BuildDate;
	static VersionInfo? _ver;

	/// <summary>
	/// アプリケーションのバージョン情報を取得します。
	/// </summary>
	public static VersionInfo Version {
		get {
			return _ver ?? throw new ArgumentNullException(nameof(VersionInfo));
		}
	}

	public AppInit(IConfiguration configuration) {
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_ver = new VersionInfo {
			Product = "Cvnet10Server",
			Version = verStr,
			BuildDate = buildDate,
			StartTime = DateTime.Now,
			BaseDir = AppContext.BaseDirectory
		};
	}

	/// <summary>
	/// 初期化 Asp.net Core の Run()の前に呼び出される
	/// </summary>
	public void Init(ExDatabase db) {

		// システムテーブル
		db.CreateTable<SysLogin>();
		db.CreateTable<SysHistJwt>();
		// マスタテーブル1
		db.CreateTable<MasterSysman>();
		db.CreateTable<MasterMeisho>();
		db.CreateTable<MasterShain>();
		db.CreateTable<MasterEndCustomer>();
		db.CreateTable<MasterShohin>();


	}

}
