using System.Linq;
using NLog;
using NPoco.Expressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Base.Share;
using NPoco;
using System.ComponentModel;
using System.IO;


namespace Cvnet10Base;

/// <summary>
/// DBバージョン情報クラス
/// </summary>
public record InnerVersion(string Version, string Sql, string Memo);

/// <summary>
/// POSデータのバージョン管理テーブル(主にテーブル変換用,テーブル追加については変更SQLは使用しない)
/// </summary>
[PrimaryKey("DbVersion", AutoIncrement = false)]
public partial class VersionTable : ObservableObject
{
	/// <summary>
	/// 各バージョン用変換SQL (ver, 次のバージョンに対応させるためのSQL(;区切り), memo
	/// </summary>
	/// <remarks>
	/// DB定義が新しいバージョンになったときに前バージョンからのDB変更のSQL定義を記述
	/// バージョン文字列,SQL文(対象バージョンから次行バージョンへの差分),定義作成日
	/// 最終行は、バージョン文字列(1つ上の行の定義日),空白,"現行バージョン"
	/// </remarks>
	static public InnerVersion[] VersionSql = [
		new("3.2.170601", "alter table PosDetail add column Tag TEXT;alter table PosToriokiDetail add column Tag TEXT", "2017/07/13定義"),
		new("3.2.170713", "alter table PosHeader add column TransIdAlipay TEXT;alter table PosTorioki add column TransIdAlipay TEXT", "2017/07/20定義"),
		new("3.2.170720", "alter table PosHeader add column PointCalcDate TEXT;alter table PosHeader add column Nationality TEXT;alter table PosHeader add column BirthDate TEXT;alter table PosDetail add column InnerTax NUMBER;alter table PosTorioki add column PointCalcDate TEXT;alter table PosTorioki add column Nationality TEXT;alter table PosTorioki add column BirthDate TEXT;alter table PosToriokiDetail add column InnerTax NUMBER", "2017/11/08定義"),
		new("3.2.171108", "alter table InboundRecord add column Gender TEXT;alter table InboundRecord add column Barcode TEXT;alter table InboundRecord add column Yobi0 TEXT", "2017/12/28定義"),
		new("3.2.171228", "alter table PosDailySeisan add column PointDiscount NUMBER;alter table PosDailySeisan add column KinYobi0 NUMBER;alter table PosDailySeisan add column KinYobi1 NUMBER", "2018/02/01定義"),
		new("3.2.180202", "alter table PosHeader add column InboundGender TEXT;alter table PosTorioki add column InboundGender TEXT", "2018/02/02定義"),
		new("3.2.180205", "alter table PosHeader add column CouponNebiki NUMBER;alter table PosTorioki add column CouponNebiki NUMBER", "2018/04/10定義"),
		new("3.2.180409", "alter table PosDailySeisan add column Yobi0 TEXT;alter table PosDailySeisan add column Yobi1 TEXT;alter table PosDailySeisan add column Yobi2 TEXT", "2018/08/30定義"),
		new("3.2.180830", "alter table PosHeader add column YobiCust00 TEXT;alter table PosHeader add column YobiCust01 TEXT;alter table PosHeader add column YobiCust02 TEXT;alter table PosTorioki add column YobiCust00 TEXT;alter table PosTorioki add column YobiCust01 TEXT;;alter table PosTorioki add column YobiCust02 TEXT", "2018/11/10定義"),
		new("3.2.181110", "alter table PosHeader add column TransIdKu TEXT;alter table PosHeader add column TaxSum1 NUMBER;alter table PosHeader add column TaxSum2 NUMBER;alter table PosHeader add column YobiCust03 TEXT;alter table PosHeader add column YobiCust04 TEXT", "2019/06/01定義"),
		new("3.2.181111", "alter table PosTorioki add column TransIdKu TEXT;alter table PosTorioki add column TaxSum1 NUMBER;alter table PosTorioki add column TaxSum2 NUMBER;alter table PosTorioki add column YobiCust03 TEXT;alter table PosTorioki add column YobiCust04 TEXT", "2019/06/01定義"),
		new("3.2.181112", "alter table PosDetail add column TaxRate NUMBER;alter table PosDetail add column Yobi3 TEXT;alter table PosDetail add column Yobi4 TEXT;alter table PosToriokiDetail add column TaxRate NUMBER;alter table PosToriokiDetail add column Yobi3 TEXT;alter table PosToriokiDetail add column Yobi4 TEXT", "2019/06/01定義"),
		new("3.2.190601", "alter table PosDailySeisan add column Yobi3 TEXT;alter table PosDailySeisan add column Yobi4 TEXT", "2020/01/22定義"),
		new("3.2.200122", "alter table PosDailySeisan add column KinYobi2 NUMBER;alter table PosDailySeisan add column KinYobi3 NUMBER", "2020/02/21定義"),
		new("3.2.200221", "alter table PosDetail add column Yobi5 TEXT;alter table PosDetail add column Yobi6 TEXT;alter table PosDetail add column Yobi7 TEXT;alter table PosDetail add column Yobi8 TEXT;alter table PosDetail add column Yobi9 TEXT;alter table PosToriokiDetail add column Yobi5 TEXT;alter table PosToriokiDetail add column Yobi6 TEXT;alter table PosToriokiDetail add column Yobi7 TEXT;alter table PosToriokiDetail add column Yobi8 TEXT;alter table PosToriokiDetail add column Yobi9 TEXT;alter table PosHeader add column YobiCust05 TEXT;alter table PosTorioki add column YobiCust05 TEXT;", "2020/04/23定義"),
		new("3.2.200423", "alter table PosDailySeisan add column KinYobi4 NUMBER;alter table PosDailySeisan add column KinYobi5 NUMBER;alter table PosDailySeisan add column KinYobi6 NUMBER", "2020/05/04定義"),
		new("3.2.200504", "alter table PosHeader add column YobiCust06 TEXT;alter table PosHeader add column YobiCust07 TEXT;alter table PosTorioki add column YobiCust06 TEXT;alter table PosTorioki add column YobiCust07 TEXT;", "2020/05/13定義"),
		new("3.2.200513", "alter table InboundRecord add column Res0 TEXT;alter table InboundRecord add column Res1 TEXT;alter table InboundRecord add column CancelNo NUMBER;Update InboundRecord set Res0='old data',Res1='old data',CancelNo=-99 where CancelNo is null;", "2020/06/22定義"), // 変更する際同時に過去データが送信されないよう目印をつける
		new("3.2.200622", "alter table PosHeader add column InboundYobi0 TEXT;alter table PosTorioki add column InboundYobi0 TEXT", "2021/02/01定義"),
		new("3.2.210201", "alter table PosMoneyInout add column CashChgr NUMBER;alter table PosMoneyInout add column Yobi1 TEXT", "2023/02/01定義"),
		new("3.2.230201", "alter table PosDetail add column Yobi10 TEXT;alter table PosToriokiDetail add column Yobi10 TEXT", "2023/03/28定義"),
		new("3.2.230328", "", "現行バージョン"),
	];

	[ObservableProperty]
	private string _dbVersion = string.Empty;

	[ObservableProperty]
	private DateTime _dateStart;

	[ObservableProperty]
	private DateTime _dateEnd;

	[ObservableProperty]
	private string _newVersion = string.Empty;

	[ObservableProperty]
	private string _doSql = string.Empty;

	[ObservableProperty]
	private string _memo = string.Empty;

	/// <summary>
	/// バージョン情報を書き込む＆バージョンアップされた場合にテーブルの整合性を保つ
	/// </summary>
	static public async Task WriteVersionInfoAsync(IDatabase db, InnerVersion[] verupSql, string dbFile, CancellationToken cancellationToken = default)
	{
		var versions = await db.FetchAsync<VersionTable>(cancellationToken);
		var sortedVersions = versions.OrderByDescending(x => x.DbVersion).ToList();
		var log = NLog.LogManager.GetCurrentClassLogger();

		if (sortedVersions.Count == 0)
		{
			var latestVersion = verupSql[^1];
			var verNow = new VersionTable
			{
				DbVersion = latestVersion.Version,
				DateStart = DateTime.Now,
				Memo = latestVersion.Memo
			};
			await db.InsertAsync(verNow, cancellationToken);
			log.Debug($"DBバージョン新規書込({verNow.DbVersion})");
			return;
		}

		var verStart = sortedVersions[0].DbVersion;
		var verLast = verupSql[^1].Version;
		if (verStart == verLast) return;

		foreach (var record in verupSql)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.Compare(record.Version, verStart) >= 0)
			{
				var item = string.Compare(record.Version, verStart) == 0 ? sortedVersions[0] : null;
				var errorMsg = await SubInsertRecordAsync(db, record, item, verLast, cancellationToken);
				log.Debug($"DBバージョンアップ({record.Version} -> {verLast}) SQL={record.Sql.Replace("\n", " ").Replace("\r", "")} err={errorMsg} db={Path.GetFileName(dbFile)}");
			}
		}
	}

	/// <summary>
	/// 個別のバージョンアップレコードの処理をSQLテーブル
	/// </summary>
	static async Task<string> SubInsertRecordAsync(IDatabase db, InnerVersion verInfo, VersionTable? item, string orgVersion, CancellationToken cancellationToken)
	{
		var errorMsg = "";
		var sqls = verInfo.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (var oneSql in sqls)
		{
			try
			{
				await db.ExecuteAsync(oneSql, cancellationToken);
			}
			catch (Exception ex)
			{
				errorMsg += $"{ex.Message};";
			}
		}

		var isInsert = item is null;
		item ??= new VersionTable();
		if (isInsert)
		{
			item.DbVersion = verInfo.Version;
			item.DateStart = DateTime.Now;
		}
		item.DoSql = verInfo.Sql;
		item.Memo = verInfo.Memo;
		item.NewVersion = orgVersion;
		item.DateEnd = DateTime.Now;

		if (isInsert)
		{
			await db.InsertAsync(item, cancellationToken);
		}
		else
		{
			await db.ExecuteAsync(item.DoSql, cancellationToken);
		}
		return errorMsg;
	}
}
