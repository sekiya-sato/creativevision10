using System.Linq;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 物流連携（WMS）の共通契約・ファイル形式 cv10-v1・履歴テーブルのテスト。
/// cv10-v1の設定・送受信種別・バッチ／行状態・CSVレイアウトの共通契約を固定する。
/// </summary>
[TestClass]
public class LogisticsContractsTests {
	[TestMethod]
	public void DetectReceiveKind_長い種別名を優先して判定する() {
		Assert.AreEqual(LogisticsDataKind.ORDERFIX, LogisticsDataKind.DetectReceiveKind("WMS_ORDERFIX_20261005.csv"));
		Assert.AreEqual(LogisticsDataKind.STOCKFIX, LogisticsDataKind.DetectReceiveKind("121stockfix2017081501.TXT"));
		Assert.AreEqual(LogisticsDataKind.LACK, LogisticsDataKind.DetectReceiveKind("WMS_LACK_1.csv"));
		Assert.AreEqual(LogisticsDataKind.INVENTORY, LogisticsDataKind.DetectReceiveKind("INVENTORY.csv"));
		Assert.AreEqual(string.Empty, LogisticsDataKind.DetectReceiveKind("WMS_ORDER_1.csv"), "送信種別は受信として判定しない");
	}

	[TestMethod]
	public void BuildFile_全項目を引用符で囲み見出し行とCRLFを付ける() {
		var (text, lines) = LogisticsFileFormat.BuildFile(LogisticsDataKind.ZAIKO, [
			["ZAIKO", "20261005120000", "0001", "A\"1", "01", "M", "", "5", "-2"],
		]);
		var expectedLine = "\"ZAIKO\",\"20261005120000\",\"0001\",\"A\"\"1\",\"01\",\"M\",\"\",\"5\",\"-2\"";
		Assert.AreEqual(1, lines.Count);
		Assert.AreEqual(expectedLine, lines[0]);
		Assert.IsTrue(text.StartsWith("\"データ区分\",\"基準日時\","), "1行目は見出し");
		Assert.IsTrue(text.EndsWith(expectedLine + "\r\n"), "CRLFで終わる");
	}

	[TestMethod]
	public void BuildFile_列数が定義と違えば例外() {
		Assert.ThrowsExactly<System.ArgumentException>(() => LogisticsFileFormat.BuildFile(LogisticsDataKind.ZAIKO, [["ZAIKO"]]));
	}

	[TestMethod]
	public void Parse_見出し行と空行を読み飛ばし原文と行番号を返す() {
		var (text, _) = LogisticsFileFormat.BuildFile(LogisticsDataKind.INVENTORY, [
			["INVENTORY", "20261005", "0001", "A-1", "S001", "01", "M", "4900000000001", "3", "x,y"],
			["INVENTORY", "20261005", "0001", "A-1", "S002", "01", "L", "", "0", ""],
		]);
		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.INVENTORY, "\uFEFF" + text + "\r\n");
		Assert.AreEqual(2, parsed.Count);
		Assert.AreEqual(1, parsed[0].LineNo);
		Assert.AreEqual("x,y", parsed[0].Fields[9], "引用符内のカンマは項目の一部");
		Assert.IsTrue(parsed[1].RawText.Contains("\"S002\""), "原文は物理行そのまま");
	}

	[TestMethod]
	public void Parse_見出し無しのファイルも受ける() {
		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.LACK, "LACK,10,123,20261005,0001,0002,S001,01,M,,0,2,,\r\n");
		Assert.AreEqual(1, parsed.Count);
		Assert.AreEqual("123", parsed[0].Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.LACK, "指示ID")]);
	}

	[TestMethod]
	public void Settings_未設定なら使用不可の理由を返し対象倉庫を分割する() {
		var empty = LogisticsSettings.From([]);
		Assert.AreEqual("WMS", empty.LinkCode);
		Assert.AreEqual(LogisticsFileFormat.Cv10V1, empty.FileFormat);
		Assert.AreNotEqual(string.Empty, empty.UnusableReason);

		var settings = LogisticsSettings.From([
			Config(MasterConfig.NameLogisticsBaseFolder, @"C:\wms"),
			Config(MasterConfig.NameLogisticsTargetSoko, " 0001, 0002,0001 "),
		]);
		CollectionAssert.AreEqual(new[] { "0001", "0002" }, settings.TargetSokoCodes);
		Assert.AreEqual(string.Empty, settings.UnusableReason);
	}

	[TestMethod]
	public void CreateDefaultData_物流連携の設定行を追加する() {
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using var db = new ExDatabaseSqlite(connection);
		Assert.IsTrue(db.CreateTable(typeof(MasterConfig), true, false));

		MasterConfig.CreateDefaultData(db);

		var rows = db.Fetch<MasterConfig>($"where Category = @0", MasterConfig.CategoryLogistics);
		CollectionAssert.AreEquivalent(new[] {
			MasterConfig.NameLogisticsLinkCode, MasterConfig.NameLogisticsBaseFolder, MasterConfig.NameLogisticsTargetSoko,
			MasterConfig.NameLogisticsEncoding, MasterConfig.NameLogisticsFileFormat,
		}, rows.Select(r => r.Name).ToArray());
		var fromRows = LogisticsSettings.From(rows);
		var fromNone = LogisticsSettings.From([]);
		Assert.AreEqual((fromNone.LinkCode, fromNone.BaseFolder, fromNone.EncodingName, fromNone.FileFormat),
			(fromRows.LinkCode, fromRows.BaseFolder, fromRows.EncodingName, fromRows.FileFormat), "初期データと未設定時の既定値が一致する");
	}

	[TestMethod]
	public void DefineDataTable_履歴2表をSQLiteへ作成できる() {
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		using var db = new ExDatabaseSqlite(connection);
		Assert.IsTrue(DefineDataTable.TableTypes.Contains(typeof(TranLogisticsBatch)));
		Assert.IsTrue(DefineDataTable.TableTypes.Contains(typeof(TranLogisticsLine)));
		Assert.IsTrue(db.CreateTable(typeof(TranLogisticsBatch), true, false));
		Assert.IsTrue(db.CreateTable(typeof(TranLogisticsLine), true, false));

		var batch = new TranLogisticsBatch { LinkCode = "WMS", Direction = (int)EnumLogisticsDirection.Send, DataKind = LogisticsDataKind.ORDER, FileName = "a.csv" };
		db.Insert(batch);
		db.Insert(new TranLogisticsLine { Id_Batch = batch.Id, LineNo = 1, RawText = "\"ORDER\"", RefTable = nameof(TranHaibun), RefId = 10, Su = 3 });
		var line = db.Fetch<TranLogisticsLine>("where Id_Batch=@0", batch.Id).Single();
		Assert.AreEqual(10, line.RefId);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, line.Status);
	}

	private static MasterConfig Config(string name, string val) =>
		new() { Category = MasterConfig.CategoryLogistics, Name = name, Val = val };
}
