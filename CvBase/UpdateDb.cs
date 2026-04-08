using NPoco;

namespace CvBase;

/// <summary>
/// DBバージョン情報クラス
/// </summary>
public record InnerVersion(string DbVersion, string Sql, string Memo);

/// <summary>
/// DBのテーブル変更を管理する
/// </summary>
public class UpdateDb {
	private static InnerVersion[] versions = [
		new ("2026.04.08 01","","SysUpdateDbテーブル 2026.04.08定義"),
		new ("2020.04.08 02","ALTER TABLE TranVulcanHht ADD COLUMN ErrorMsg TEXT;","2026.04.08定義")
];



	static public async Task WriteVersionInfoAsync(IDatabase db, CancellationToken ct = default) {
		await WriteVersionInfoAsync(db, versions, ct);
	}
	/// <summary>
	/// バージョン情報を書き込む＆バージョンアップされた場合にテーブルの整合性を保つ
	/// </summary>
	static public async Task WriteVersionInfoAsync(IDatabase db, InnerVersion[] verupSql, CancellationToken ct = default) {
		if (verupSql.Length == 0) return;

		var latestDb = await db.FirstOrDefaultAsync<SysUpdateDb>("order by DbVersion desc", ct);
		var logger = NLog.LogManager.GetCurrentClassLogger();
		// vreupSqlがあり、DBにバージョンレコードがない場合は、プログラム最新かつDBも新規の場合なので、verupSqlの最新バージョンをDBに書き込む
		var latestVersion = verupSql[^1]; // verupSqlの最新バージョンは、DBの最新バージョンとする
		if (latestDb == null) {
			var verNow = new SysUpdateDb {
				DbVersion = latestVersion.DbVersion,
				DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
				Sql = "",
				NewVersion = latestVersion.DbVersion,
				Memo = "新規レコード作成"
			};
			await db.InsertAsync(verNow, ct);
			logger.Debug($"DBバージョン新規書込({latestVersion.DbVersion})");
			return;
		}
		if (string.Compare(latestDb.DbVersion, latestVersion.DbVersion) <= 0) { // DBに最新までレコードがある
			logger.Debug($"DBバージョンは最新({latestVersion.DbVersion})");
			return;
		}
		foreach (var record in verupSql) {
			ct.ThrowIfCancellationRequested();
			if (string.Compare(latestDb.DbVersion, record.DbVersion) > 0) { // verupSqlのバージョンがDBのバージョンより新しい場合は、DBをverupSqlのバージョンに合わせるためのSQLを実行する
				var errorMsg = await SubInsertRecordAsync(db, record, latestDb.DbVersion, logger, ct);
			}
		}
		logger.Debug($"DBバージョンアップ({latestDb.DbVersion} -> {latestVersion.DbVersion})");
	}

	/// <summary>
	/// 個別のバージョンアップレコードの処理
	/// </summary>
	static async Task<string> SubInsertRecordAsync(IDatabase db, InnerVersion verInfo, string orgVersion, NLog.Logger logger, CancellationToken ct) {
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
			NewVersion = orgVersion,
		};
		await db.InsertAsync(item, ct);
		return errorMsg ?? "";
	}
}
