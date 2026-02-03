using Cvnet10Base;
using Cvnet10Server.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System;
using System.Configuration;

namespace Cvnet10Server;
public class AppInit {
	private readonly IConfiguration _configuration;
	// ToDo: バージョン番号は手動で更新すること
	readonly string verStr = "0.0.099";
	readonly DateTime buildDate = new DateTime(2026,2,1);
	static VersionInfo? _ver;

	public static VersionInfo Ver {
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
			StartTime = DateTime.Now
		};
	}

	public void Init() {
		var sqliteFileName = _configuration.GetConnectionString("sqlite");
		if (string.IsNullOrWhiteSpace(sqliteFileName))
			throw new InvalidOperationException("Connection string 'sqlite' is missing. Configure it in appsettings.json under ConnectionStrings.");

		var db = ExSqliteDatabase.GetDbConn(sqliteFileName);
		// システムテーブル
		db.CreateTable<SysLogin>();
		db.CreateTable<SysHistJwt>();
		// マスタテーブル1
		db.CreateTable<MasterSysman>();
		db.CreateTable<MasterMeisho>();



	}

}
