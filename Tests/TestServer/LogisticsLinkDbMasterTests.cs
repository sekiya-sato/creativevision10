using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 物流連携テストの共通基盤。共有メモリのSQLiteと一時の連携フォルダを用意し、設定（対象倉庫 S01）を入れる。
/// </summary>
public abstract class LogisticsTestBase {
	private SqliteConnection? _anchorConnection;
	protected ExDatabaseSqlite Db { get; private set; } = null!;
	protected string Folder { get; private set; } = string.Empty;
	/// <summary>対象倉庫 S01 の Id</summary>
	protected long SokoId { get; private set; }

	[TestInitialize]
	public void InitializeBase() {
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = $"Logistics-{System.Guid.NewGuid():N}",
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		Db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
		foreach (var type in TableTypes) {
			Db.CreateTable(type, true, false);
		}
		Db.Insert(new MasterSysman { ShimeBi = 99 });
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Folder = Path.Combine(Path.GetTempPath(), "cv10-logistics-test-" + System.Guid.NewGuid().ToString("N"));
		MasterConfig.CreateDefaultData(Db);
		SetConfig(MasterConfig.NameLogisticsBaseFolder, Folder);
		SetConfig(MasterConfig.NameLogisticsTargetSoko, "S01");
		SokoId = InsertTokui("S01", "対象倉庫", 0);
	}

	[TestCleanup]
	public void CleanupBase() {
		Db.Close();
		(Db.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
		if (Directory.Exists(Folder)) {
			Directory.Delete(Folder, true);
		}
	}

	/// <summary>テストで作るテーブル</summary>
	protected virtual IEnumerable<System.Type> TableTypes => [
		typeof(MasterConfig), typeof(MasterSysman), typeof(MasterShohin), typeof(MasterTokui), typeof(MasterShiire),
		typeof(TranLogisticsBatch), typeof(TranLogisticsLine), typeof(SysSequence), typeof(SysHistAutoexec),
		typeof(TranHaibun), typeof(Tran00Uriage), typeof(Tran03Shiire), typeof(Tran10IdoOut), typeof(Tran11IdoIn), typeof(Tran13Hachu),
		typeof(Tran60Tana), typeof(SummaryStock), typeof(SummaryRealStock), typeof(DerivedShohinColSiz),
	];

	protected void SetConfig(string name, string val) =>
		Db.Execute($"update {nameof(MasterConfig)} set Val=@0 where Name=@1", val, name);

	protected long InsertTokui(string code, string name, int tenType) {
		var tokui = new MasterTokui { Code = code, Name = name, TenType = tenType, PostalCode = "100-0001", Tel = "03-1111-2222" };
		Db.Insert(tokui);
		return tokui.Id;
	}

	protected LogisticsLinkDb Logistics => new(Db);

	/// <summary>送信フォルダのファイルを設定の文字コード(shift_jis)で読む</summary>
	protected string ReadSendFile(string fileName) {
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		return File.ReadAllText(Path.Combine(Folder, LogisticsSettings.SendDir, fileName), Encoding.GetEncoding("shift_jis"));
	}
}

/// <summary>
/// L01 マスタデータ作成（仕様 3.1・5.2）。
/// </summary>
[TestClass]
public class LogisticsLinkDbMasterTests : LogisticsTestBase {
	[TestMethod]
	public void CreateMasterFiles_商品SKUと場所を出力し控えと実行履歴を残す() {
		InsertShohin("A001", ("01", "M", "4900000000011"), ("01", "L", "4900000000028"));
		InsertTokui("T001", "卸先\"A\"", 1);
		Db.Insert(new MasterShiire { Code = "V001", Name = "仕入先" });

		var result = Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD, LogisticsDataKind.BSY], true, 0, false, 0), idShain: 7);

		var pd = result.Kinds.Single(k => k.Kind == LogisticsDataKind.PD);
		var bsy = result.Kinds.Single(k => k.Kind == LogisticsDataKind.BSY);
		Assert.AreEqual(2, pd.Count);
		Assert.AreEqual(3, bsy.Count, "対象倉庫・卸先・仕入先");
		var pdText = ReadSendFile(pd.FileName);
		var pdLines = LogisticsFileFormat.Parse(LogisticsDataKind.PD, pdText);
		Assert.AreEqual("4900000000028", pdLines[1].Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.PD, "JAN1")]);
		Assert.IsTrue(File.Exists(Path.Combine(Folder, LogisticsSettings.SendBackupDir, pd.FileName)), "控えを残す");
		Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(Folder, LogisticsSettings.WorkDir)).Any(), "作成中ファイルを残さない");

		var bsyLines = LogisticsFileFormat.Parse(LogisticsDataKind.BSY, ReadSendFile(bsy.FileName));
		var kubunIndex = LogisticsFileFormat.ColumnIndex(LogisticsDataKind.BSY, "場所区分");
		CollectionAssert.AreEquivalent(new[] { "20", "50", "10" }, bsyLines.Select(l => l.Fields[kubunIndex]).ToArray());
		Assert.AreEqual("卸先\"A\"", bsyLines.Single(l => l.Fields[kubunIndex] == "50").Fields[3], "引用符を含む名称も往復できる");
		Assert.AreEqual("1000001", bsyLines[0].Fields[6], "郵便番号のハイフンを除く");

		var batch = Db.Fetch<TranLogisticsBatch>("where Id=@0", pd.BatchId).Single();
		Assert.AreEqual((int)EnumLogisticsSendStatus.Placed, batch.Status);
		Assert.AreEqual(7, batch.Id_Shain);
		Assert.AreEqual(0, Db.Fetch<TranLogisticsLine>("").Count, "マスタは行明細を残さない");
		var hist = Db.Fetch<SysHistAutoexec>("").Single();
		Assert.AreEqual(LogisticsLinkDb.LockProcessName, hist.TaskName);
		Assert.AreEqual(5, hist.Count);
		Assert.AreEqual(0, Db.Fetch<SysSequence>("").Count, "排他を開放する");
	}

	[TestMethod]
	public void CreateMasterFiles_プレビューはファイルもバッチも作らずJAN重複を警告する() {
		InsertShohin("A001", ("01", "M", "4900000000011"));
		InsertShohin("A002", ("01", "M", "4900000000011"));
		InsertShohin("A003");

		var result = Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], true, 0, true, 0), 0);

		Assert.AreEqual(2, result.Kinds.Single().Count);
		Assert.IsTrue(result.Warnings.Any(w => w.Contains("同じJAN")), string.Join("/", result.Warnings));
		Assert.IsTrue(result.Warnings.Any(w => w.Contains("A003")), "色・サイズの無い商品を警告");
		Assert.AreEqual(0, Db.Fetch<TranLogisticsBatch>("").Count);
		Assert.IsFalse(Directory.Exists(Path.Combine(Folder, LogisticsSettings.SendDir)));
	}

	[TestMethod]
	public void CreateMasterFiles_差分は前回成功した同種別バッチ以降に更新した商品だけ() {
		InsertShohin("A001", ("01", "M", "4900000000011"));
		var first = Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], false, 0, false, 0), 0);
		Assert.AreEqual(1, first.Kinds.Single().Count, "初回は前回が無いので全件");

		Db.Execute("update MasterShohin set Vdu = 1");
		InsertShohin("A002", ("01", "M", "4900000000035"));
		var second = Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], false, 0, false, 0), 0);

		Assert.AreEqual(1, second.Kinds.Single().Count);
		Assert.IsTrue(ReadSendFile(second.Kinds.Single().FileName).Contains("\"A002\""));
	}

	[TestMethod]
	public void CreateMasterFiles_対象0件ならファイルを作らない() {
		var result = Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], true, 0, false, 0), 0);
		Assert.AreEqual(0, result.Kinds.Single().Count);
		Assert.AreEqual(string.Empty, result.Kinds.Single().FileName);
		Assert.AreEqual(0, Db.Fetch<TranLogisticsBatch>("").Count);
	}

	[TestMethod]
	public void CreateMasterFiles_設定不備や他処理の実行中は実行しない() {
		SetConfig(MasterConfig.NameLogisticsBaseFolder, string.Empty);
		Assert.ThrowsExactly<System.ArgumentException>(() =>
			Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], true, 0, false, 0), 0));

		SetConfig(MasterConfig.NameLogisticsBaseFolder, Folder);
		var other = new ManualLockDb(Db).TryBegin("原価更新", "step", 60);
		Assert.IsTrue(other.IsAcquired);
		Assert.ThrowsExactly<System.InvalidOperationException>(() =>
			Logistics.CreateMasterFiles(new LogisticsMasterParam([LogisticsDataKind.PD], true, 0, false, 0), 0));
	}

	[TestMethod]
	public void QuerySettings_対象倉庫コードが倉庫マスタに無ければ使用不可() {
		Assert.AreEqual(string.Empty, Logistics.QuerySettings().UnusableReason);
		SetConfig(MasterConfig.NameLogisticsTargetSoko, "ZZZ");
		var info = Logistics.QuerySettings();
		Assert.AreNotEqual(string.Empty, info.UnusableReason);
		Assert.AreEqual(0, info.Soko.Length);
	}

	private void InsertShohin(string code, params (string Col, string Siz, string Jan)[] skus) {
		Db.Insert(new MasterShohin {
			Code = code,
			Name = "商品" + code,
			TankaJodai = 1000,
			Jcolsiz = [.. skus.Select((s, i) => new MasterShohinColSiz { Id_Col = i + 1, Code_Col = s.Col, Id_Siz = i + 1, Code_Siz = s.Siz, Jan1 = s.Jan })],
			Vdu = CvAsset.Common.GetVdate(),
		});
	}
}
