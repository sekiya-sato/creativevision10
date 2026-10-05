using System.IO;
using System.Linq;
using System.Text;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// L03 連携データ手動受信の取込・検査（仕様 4.1〜4.3）。反映は別テスト。
/// </summary>
[TestClass]
public class LogisticsLinkDbReceiveTests : LogisticsReceiveTestBase {
	[TestMethod]
	public void ImportReceiveFiles_出荷確定を取り込み検査して原文を退避し同じ内容は二重に取り込まない() {
		var a = SendHaibun(su: 3);
		var b = SendHaibun(su: 3, siz: 3);
		var file = PutReceiveFile("WMS_ORDERFIX_1.csv", LogisticsDataKind.ORDERFIX,
			OrderFix(a, "2", "1"),
			OrderFix(b, "3", "", jan: "4900000000028", code: ""));

		var result = Logistics.ImportReceiveFiles(new LogisticsReceiveImportParam([], [], 0), 0);

		var batchId = result.Kinds.Single().BatchId;
		var lines = Lines(batchId);
		Assert.AreEqual(2, lines.Count);
		Assert.IsTrue(lines.All(l => l.Status == (int)EnumLogisticsLineStatus.Pending), string.Join("/", lines.Select(l => l.ErrorMsg)));
		Assert.AreEqual((a, 2, 1, "20261005"), (lines[0].RefId, lines[0].Su, lines[0].Su2, lines[0].WorkDay));
		Assert.AreEqual((b, 3, 0), (lines[1].RefId, lines[1].Su, lines[1].Su2), "欠品数が空なら指示数−確定数。JANだけでもSKUを解決する");
		Assert.IsFalse(File.Exists(file), "取込後は受信フォルダから移す");
		Assert.IsTrue(File.Exists(Path.Combine(Folder, LogisticsSettings.ReceiveBackupDir, "WMS_ORDERFIX_1.csv")));

		File.Copy(Path.Combine(Folder, LogisticsSettings.ReceiveBackupDir, "WMS_ORDERFIX_1.csv"), Path.Combine(Folder, LogisticsSettings.ReceiveDir, "WMS_ORDERFIX_again.csv"));
		var again = Logistics.ImportReceiveFiles(new LogisticsReceiveImportParam([], [], 0), 0);
		Assert.AreEqual(0, again.Kinds.Single().BatchId);
		Assert.IsTrue(again.Warnings.Single().Contains("取込済み"));
		Assert.AreEqual(1, Db.Fetch<TranLogisticsBatch>("where Direction=2").Count);
	}

	[TestMethod]
	public void ImportReceiveFiles_出荷確定の検査エラーと同じ伝票の巻き添え() {
		var ok = SendHaibun(su: 3);
		var sameSlip = SendHaibun(su: 3, siz: 3);
		var notSent = InsertHaibunRow(su: 3, sendFlg: 0, tenpo: InsertTokui("T02", "店2", 6));
		var done = SendHaibun(su: 3, tenpo: InsertTokui("T03", "店3", 6));
		Db.Execute("update TranHaibun set EndFlag=1, KakuteiDay='20261001' where Id=@0", done);
		var twice = SendHaibun(su: 3, tenpo: InsertTokui("T04", "店4", 6));
		var mismatch = SendHaibun(su: 3, tenpo: InsertTokui("T05", "店5", 6));

		var result = Upload("WMS_ORDERFIX_2.csv", LogisticsDataKind.ORDERFIX,
			OrderFix(ok, "4", "0"),
			OrderFix(sameSlip, "3", "0", siz: "L"),
			OrderFix(notSent, "3", "0"),
			OrderFix(done, "3", "0"),
			OrderFix(twice, "2", "0"),
			OrderFix(twice, "1", "0"),
			OrderFix(999, "1", "0"),
			OrderFix(mismatch, "1", "0", siz: "L"));

		var lines = Lines(result.Kinds.Single().BatchId);
		Assert.AreEqual("E12", lines[0].ErrorCode, "確定数が指示数を超える");
		Assert.AreEqual("E19", lines[1].ErrorCode, "同じ伝票の行も反映しない");
		Assert.AreEqual("E11", lines[2].ErrorCode, "未送信");
		Assert.AreEqual("E11", lines[3].ErrorCode, "完了済み（重複受信）");
		Assert.AreEqual("E13", lines[4].ErrorCode, "同じ指示IDが2行");
		Assert.AreEqual("E13", lines[5].ErrorCode);
		Assert.AreEqual("E10", lines[6].ErrorCode, "配分が無い");
		Assert.AreEqual("E10", lines[7].ErrorCode, "SKUが指示と違う");
		Assert.AreEqual(8, Db.Fetch<TranLogisticsBatch>("where Id=@0", result.Kinds.Single().BatchId).Single().ErrorCount);
	}

	[TestMethod]
	public void ImportReceiveFiles_欠品は確定数0として扱い一致しない数は警告() {
		var a = SendHaibun(su: 5);
		var result = Upload("x_LACK.csv", LogisticsDataKind.LACK, OrderFix(a, "", "3", kind: LogisticsDataKind.LACK));

		var line = Lines(result.Kinds.Single().BatchId).Single();
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, line.Status);
		Assert.AreEqual((0, 3), (line.Su, line.Su2));
		Assert.AreEqual("W12", line.ErrorCode);
	}

	[TestMethod]
	public void ImportReceiveFiles_種別を判定できないファイルは取込失敗にして受信フォルダに残す() {
		var file = PutReceiveFile("unknown.csv", LogisticsDataKind.ORDERFIX, OrderFix(1, "1", "0"));

		var result = Logistics.ImportReceiveFiles(new LogisticsReceiveImportParam([], [], 0), 0);

		var batch = Db.Fetch<TranLogisticsBatch>("where Id=@0", result.Kinds.Single().BatchId).Single();
		Assert.AreEqual((int)EnumLogisticsReceiveStatus.ImportFailed, batch.Status);
		Assert.IsTrue(File.Exists(file));
		Assert.AreEqual(0, Db.Fetch<TranLogisticsLine>("").Count);
	}

	[TestMethod]
	public void ImportReceiveFiles_入荷確定は送信した元伝票と入荷倉庫を検査する() {
		var hachu = InsertHachu();
		var notSent = InsertHachu();
		Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.STOCK, "20261005", [], [hachu], 0), 0);

		var result = Upload("WMS_STOCKFIX_1.csv", LogisticsDataKind.STOCKFIX,
			StockFix("20", hachu, "S01", "M", "4"),
			StockFix("20", notSent, "S01", "M", "4"),
			StockFix("20", hachu, "S01", "L", "1", day: "20261007"),
			StockFix("20", hachu, "T01", "M", "1", day: "20261008"),
			StockFix("10", 999, "S01", "M", "1", day: "20261009"));

		var lines = Lines(result.Kinds.Single().BatchId);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, lines[0].Status, lines[0].ErrorMsg);
		Assert.AreEqual((nameof(Tran13Hachu), hachu, SokoId, 4), (lines[0].RefTable, lines[0].RefId, lines[0].Id_Soko, lines[0].Su));
		Assert.AreEqual("E20", lines[1].ErrorCode, "送信していない発注");
		Assert.AreEqual("W20", lines[2].ErrorCode, "元伝票に無いSKUは警告");
		Assert.AreEqual("E03", lines[3].ErrorCode, "入荷倉庫が違う");
		Assert.AreEqual("E20", lines[4].ErrorCode, "移動が無い");
	}

	[TestMethod]
	public void ImportReceiveFiles_棚卸は対象倉庫とSKUと重複を検査し再検査でマスタ修正を反映する() {
		var result = Upload("WMS_INVENTORY_1.csv", LogisticsDataKind.INVENTORY,
			["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "M", "", "3", ""],
			["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "M", "", "1", ""],
			["INVENTORY", "20261005", "S01", "B-1", "A002", "01", "M", "", "1", ""],
			["INVENTORY", "20261005", "S01", "C-1", "A001", "01", "L", "", "2", ""]);

		var batchId = result.Kinds.Single().BatchId;
		var lines = Lines(batchId);
		Assert.AreEqual("E30", lines[0].ErrorCode);
		Assert.AreEqual("E30", lines[1].ErrorCode);
		Assert.AreEqual("E02", lines[2].ErrorCode);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, lines[3].Status);

		Db.Insert(new DerivedShohinColSiz { Id_Shohin = 77, Code = "A002", Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M" });
		Logistics.RecheckBatch(new LogisticsRecheckParam(batchId, 0), 0);
		lines = Lines(batchId);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, lines[2].Status, "マスタ追加後の再検査で解消");
		Assert.AreEqual(77, lines[2].Id_Shohin);
		Assert.AreEqual("E30", lines[0].ErrorCode);
	}
}

/// <summary>受信系テストの共通準備（店舗・商品SKU）と取込ヘルパ</summary>
public abstract class LogisticsReceiveTestBase : LogisticsTestBase {
	protected long TenpoId;
	protected long ShohinId;

	[TestInitialize]
	public void InitializeReceive() {
		TenpoId = InsertTokui("T01", "直営店", 6);
		var shohin = new MasterShohin {
			Code = "A001",
			Name = "商品A",
			Jcolsiz = [
				new MasterShohinColSiz { Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M", Jan1 = "4900000000011" },
				new MasterShohinColSiz { Id_Col = 1, Code_Col = "01", Id_Siz = 3, Code_Siz = "L", Jan1 = "4900000000028" },
			],
		};
		Db.Insert(shohin);
		ShohinId = shohin.Id;
		Db.Insert(new DerivedShohinColSiz { Id_Shohin = ShohinId, Code = "A001", Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M", Jan1 = "4900000000011" });
		Db.Insert(new DerivedShohinColSiz { Id_Shohin = ShohinId, Code = "A001", Id_Col = 1, Code_Col = "01", Id_Siz = 3, Code_Siz = "L", Jan1 = "4900000000028" });
	}

	// ---- helpers ----

	protected long InsertHaibunRow(int su, int sendFlg, long tenpo = 0, long siz = 2) {
		var row = new TranHaibun {
			DenDay = "20261001", Id_Soko = SokoId, Id_Tenpo = tenpo == 0 ? TenpoId : tenpo, Kubun = (int)EnumHaibun.Zaiko,
			Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = siz, Su = su, SendFlg = sendFlg, Tanka = 500,
		};
		Db.Insert(row);
		return row.Id;
	}

	/// <summary>配分を1行作って出荷指示として送信する</summary>
	protected long SendHaibun(int su, long siz = 2, long tenpo = 0) {
		var id = InsertHaibunRow(su, 0, tenpo, siz);
		Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.ORDER, "20261005", [], [id], 0), 0);
		return id;
	}

	protected long InsertHachu(int su = 10) {
		var row = new Tran13Hachu {
			DenDay = "20261001", Id_Soko = SokoId, Id_Shiire = 1, VShiire = new CodeNameView { Sid = 1, Cd = "V01", Mei = "仕入先" }, Kubun = 10,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = ShohinId, Code_Shohin = "A001", Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M", Su = su, Tanka = 400, Jodai = 1000 }],
		};
		Db.Insert(row);
		return row.Id;
	}

	protected static string?[] OrderFix(long id, string kakutei, string ketsu, string siz = "M", string jan = "", string code = "A001", string kind = LogisticsDataKind.ORDERFIX) =>
		[kind, "10", id.ToString(), "20261005", "S01", "T01", code, code.Length > 0 ? "01" : "", code.Length > 0 ? siz : "", jan, kakutei, ketsu, "INV-1", ""];

	protected static string?[] StockFix(string kubun, long id, string soko, string siz, string su, string day = "20261006") =>
		["STOCKFIX", kubun, id.ToString(), "1", day, soko, "V01", "A001", "01", siz, "", su, ""];

	protected string PutReceiveFile(string fileName, string kind, params string?[][] rows) {
		var dir = Path.Combine(Folder, LogisticsSettings.ReceiveDir);
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, fileName);
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		File.WriteAllText(path, LogisticsFileFormat.BuildFile(kind, rows).Text, Encoding.GetEncoding("shift_jis"));
		return path;
	}

	/// <summary>画面で選んだファイルとして取り込む</summary>
	protected LogisticsRunResult Upload(string fileName, string kind, params string?[][] rows) {
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var bytes = Encoding.GetEncoding("shift_jis").GetBytes(LogisticsFileFormat.BuildFile(kind, rows).Text);
		return Logistics.ImportReceiveFiles(new LogisticsReceiveImportParam([], [new LogisticsUploadFile(fileName, bytes)], 0), 0);
	}

	protected System.Collections.Generic.List<TranLogisticsLine> Lines(long batchId) =>
		Db.Fetch<TranLogisticsLine>("where Id_Batch=@0 order by LineNo, Id", batchId);
}
