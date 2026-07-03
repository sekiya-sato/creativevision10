using Microsoft.Extensions.Logging;
using NPoco;

namespace CvBase;

/// <summary>
/// DBバージョン情報クラス (SysUpdateDb を使用)
/// </summary>
public record InnerVersion(int DbVersion, string Sql, string Memo);

/// <summary>
/// SqlDepends: DBのテーブル変更を管理する versions配列を定義し、実稼働DBとプログラムの整合性をとる
/// </summary>
public class UpdateDb {
	private static InnerVersion[] versions = [ // バージョン番号8桁=年月日+連番
		new (26_04_01_01,"ALTER TABLE TranVulcanHht ADD COLUMN ErrorMsg TEXT;","SysUpdateDbテーブル 2026.04.08定義"),
		new (26_06_10_01,"ALTER TABLE MasterSysman ADD COLUMN TaxRegistrationNumber TEXT;","MasterSysman 列追加 2026.06.10定義"),
		// new (26_06_10_02,"ALTER TABLE SysUpdateDb RENAME COLUMN NewVersion To PreVersion;","SysUpdateDb 列名変更 2026.06.10定義"), SysUpdateDb のみ直接変更する
		new (26_06_10_02,"ALTER TABLE MasterTokui ADD COLUMN TaxRegistrationNumber TEXT;ALTER TABLE MasterShiire ADD COLUMN TaxRegistrationNumber TEXT;","MasterTokui MasterShiire 列追加 2026.06.10定義"),
		new (26_06_11_01,"ALTER TABLE DerivedShohinColSiz ADD COLUMN Vdc NUMBER not null default 0;ALTER TABLE DerivedShohinColSiz ADD COLUMN Vdu NUMBER not null default 0;update DerivedShohinColSiz set (vdc,vdu)=(select s.vdc,s.vdu from MasterShohin s where s.Id=Id_Shohin);","DerivedShohinColSiz 列追加 2026.06.11定義"),
		new (26_06_18_01,"ALTER TABLE TranHhtData ADD COLUMN Jan1 TEXT NOT NULL DEFAULT '';ALTER TABLE TranHhtData ADD COLUMN Jan2 TEXT NOT NULL DEFAULT '';ALTER TABLE TranHhtData DROP COLUMN TanaNo;ALTER TABLE TranHhtData ADD COLUMN TanaNo NUMBER not null default 0;","2026.06.18定義"),
		new (26_06_23_01,"ALTER TABLE MasterShain ADD COLUMN ExpireDate TEXT NOT NULL DEFAULT '';","2026.06.23定義")
	];

	public static async Task WriteVersionInfoAsync(IDatabase db, CancellationToken ct = default) {
		await WriteVersionInfoAsync(db, versions, ct);
	}
	/// <summary>
	/// バージョン情報を書き込む＆バージョンアップされた場合にテーブルの整合性を保つ
	/// </summary>
	public static async Task WriteVersionInfoAsync(IDatabase db, InnerVersion[] verupSql, CancellationToken ct = default) {
		if (verupSql.Length == 0) return;

		var latestDb = await db.FirstOrDefaultAsync<SysUpdateDb>("order by DbVersion desc", ct); // DB上の最新バージョン情報を取得
		var logger = new NLogExtender<UpdateDb>();
		// vreupSqlがあり、DBにバージョンレコードがない場合は、プログラム最新かつDBも新規の場合なので、verupSqlの最新バージョンをDBに書き込む
		var latestVersion = verupSql[^1]; // verupSqlの最新バージョンは、DBの最新バージョンとする
		if (latestDb == null) {
			var verNow = new SysUpdateDb {
				DbVersion = latestVersion.DbVersion,
				DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
				Sql = "",
				PreVersion = latestVersion.DbVersion,
				Memo = "新規レコード作成"
			};
			await db.InsertAsync(verNow, ct);
			logger.LogDebug($"UpdateDb: DBバージョン新規書込({latestVersion.DbVersion})");
			return;
		}
		if (latestDb.DbVersion >= latestVersion.DbVersion) { // DBに最新までレコードがある
			logger.LogInformation($"UpdateDb: DBバージョンは最新({latestVersion.DbVersion})");
			return;
		}
		foreach (var record in verupSql) { // 配列はforeachで必ず順番に処理される
			ct.ThrowIfCancellationRequested();
			if (record.DbVersion > latestDb.DbVersion) { // verupSqlのバージョンがDBのバージョンより新しい場合は、DBをverupSqlのバージョンに合わせるためのSQLを実行する
				var errorMsg = await SubInsertRecordAsync(db, record, latestDb.DbVersion, logger, ct);
				if (!string.IsNullOrEmpty(errorMsg)) {
					logger.LogError($"UpdateDb: DBバージョンアップ時エラー rec={record.DbVersion}: {errorMsg} : SQL={record.Sql}");
				}
			}
		}
		logger.LogDebug($"DBバージョンアップ({latestDb.DbVersion} -> {latestVersion.DbVersion})");
	}

	/// <summary>
	/// 個別のバージョンアップレコードの処理
	/// </summary>
	static async Task<string> SubInsertRecordAsync(IDatabase db, InnerVersion verInfo, int orgVersion, NLogExtender<UpdateDb> logger, CancellationToken ct) {
		string? errorMsg = null;
		var sqls = verInfo.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (var oneSql in sqls) {
			if (string.IsNullOrWhiteSpace(oneSql)) continue;
			try {
				await db.ExecuteAsync(oneSql, ct);
			}
			catch (Exception ex) {
				errorMsg += $"{ex.Message};";
			}
		}
		var item = new SysUpdateDb {
			DbVersion = verInfo.DbVersion,
			DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
			Sql = verInfo.Sql,
			Memo = errorMsg ?? verInfo.Memo,
			PreVersion = orgVersion,
		};
		await db.InsertAsync(item, ct);
		return errorMsg ?? "";
	}
}
