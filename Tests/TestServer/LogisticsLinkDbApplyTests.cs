using System.Linq;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// L03 反映（仕様 4.4）と L04 の行操作（仕様 4.5）。
/// </summary>
[TestClass]
public class LogisticsLinkDbApplyTests : LogisticsReceiveTestBase {
	[TestMethod]
	public void ApplyReceiveBatch_出荷確定は配分確定で移動伝票を作り残りを欠品で完了する() {
		Stock(siz: 2, su: 10);
		var a = SendHaibun(su: 3);
		var batchId = Upload("WMS_ORDERFIX_1.csv", LogisticsDataKind.ORDERFIX, OrderFix(a, "2", "1")).Kinds.Single().BatchId;

		var result = Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), idShain: 5);

		Assert.AreEqual(1, result.Kinds.Single().Count, result.Message);
		var h = Db.Fetch<TranHaibun>("where Id=@0", a).Single();
		Assert.AreEqual((1, 2, 1, "20261005"), (h.EndFlag, h.JitsuSu, h.ShortSu, h.KakuteiDay));
		var line = Lines(batchId).Single();
		Assert.AreEqual((int)EnumLogisticsLineStatus.Applied, line.Status);
		Assert.AreEqual(nameof(Tran10IdoOut), line.TargetTable);
		var slip = Db.Fetch<Tran10IdoOut>("where Id=@0", line.TargetId).Single();
		Assert.AreEqual(2, slip.SuTotal);
		Assert.AreEqual((int)EnumLogisticsReceiveStatus.Applied, Batch(batchId).Status);

		var again = Upload("WMS_ORDERFIX_2.csv", LogisticsDataKind.ORDERFIX, OrderFix(a, "2", "1", jan: "x"));
		Assert.AreEqual("E11", Lines(again.Kinds.Single().BatchId).Single().ErrorCode, "反映済みの指示を別ファイルで再受信しても二重に確定しない");
	}

	[TestMethod]
	public void ApplyReceiveBatch_欠品は伝票を作らず完了だけ立てる() {
		var a = SendHaibun(su: 3);
		var batchId = Upload("WMS_LACK_1.csv", LogisticsDataKind.LACK, OrderFix(a, "", "3", kind: LogisticsDataKind.LACK)).Kinds.Single().BatchId;

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);

		var h = Db.Fetch<TranHaibun>("where Id=@0", a).Single();
		Assert.AreEqual((1, 0, 3), (h.EndFlag, h.JitsuSu, h.ShortSu));
		Assert.AreEqual(0, Db.Fetch<Tran10IdoOut>("").Count);
		Assert.AreEqual(string.Empty, Lines(batchId).Single().TargetTable);
	}

	[TestMethod]
	public void ApplyReceiveBatch_在庫割れの伝票だけエラーにし他の伝票は反映し在庫を直せば再反映できる() {
		Stock(siz: 2, su: 10);
		var ok = SendHaibun(su: 3);
		var ng = SendHaibun(su: 3, siz: 3, tenpo: InsertTokui("T02", "店2", 6));
		var batchId = Upload("WMS_ORDERFIX_1.csv", LogisticsDataKind.ORDERFIX,
			OrderFix(ok, "3", "0"), OrderFix(ng, "3", "0", siz: "L")).Kinds.Single().BatchId;

		var first = Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);

		var lines = Lines(batchId);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Applied, lines[0].Status);
		Assert.AreEqual("E40", lines[1].ErrorCode, first.Message);
		Assert.AreEqual(0, Db.Fetch<TranHaibun>("where Id=@0", ng).Single().EndFlag, "失敗した伝票は何も書かない");
		Assert.AreEqual((int)EnumLogisticsReceiveStatus.PartialError, Batch(batchId).Status);

		Stock(siz: 3, su: 5);
		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Applied, Lines(batchId)[1].Status);
		Assert.AreEqual((int)EnumLogisticsReceiveStatus.Applied, Batch(batchId).Status);
		Assert.AreEqual(2, Db.Fetch<Tran10IdoOut>("").Count);
	}

	[TestMethod]
	public void ApplyReceiveBatch_入荷確定は発注に紐付く仕入と移動受を作り在庫と発注完了を更新する() {
		var hachu = InsertHachu(su: 4);
		var otherSoko = InsertTokui("S99", "出庫元", 0);
		var idoOut = new Tran10IdoOut {
			DenDay = "20261001", Id_Soko = otherSoko, Id_Ido = SokoId,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = ShohinId, Code_Shohin = "A001", Id_Col = 1, Code_Col = "01", Id_Siz = 3, Code_Siz = "L", Su = 2, Tanka = 300 }],
		};
		Db.Insert(idoOut);
		Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.STOCK, "20261005", [], [], 0), 0);
		var batchId = Upload("WMS_STOCKFIX_1.csv", LogisticsDataKind.STOCKFIX,
			StockFix("20", hachu, "S01", "M", "4"),
			StockFix("10", idoOut.Id, "S01", "L", "2")).Kinds.Single().BatchId;

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);

		var lines = Lines(batchId);
		Assert.AreEqual(string.Empty, string.Join("/", lines.Where(l => l.Status != (int)EnumLogisticsLineStatus.Applied).Select(l => l.ErrorCode + l.ErrorMsg)));
		var stockBatch = Logistics.QueryBatches(new LogisticsBatchQueryParam(1, LogisticsDataKind.STOCK, string.Empty, string.Empty, false)).Single();
		Assert.AreEqual(0, stockBatch.ChangedAfterSend, "入荷確定で発注が完了・移動が受入済みになっても送信後変更とはみなさない");
		var shiire = Db.Fetch<Tran03Shiire>("").Single();
		Assert.AreEqual(((int)hachu, 4, 400, "20261006"), (shiire.RelateNo1, shiire.SuTotal, shiire.Jmeisai![0].Tanka, shiire.DenDay));
		Assert.AreEqual(1, Db.Fetch<Tran13Hachu>("where Id=@0", hachu).Single().EndFlag, "発注残が無くなれば完了");
		var idoIn = Db.Fetch<Tran11IdoIn>("").Single();
		Assert.AreEqual((idoOut.Id, otherSoko, SokoId, 2), (idoIn.RelateNo1, idoIn.Id_Soko, idoIn.Id_Ido, idoIn.SuTotal));
		var stock = Db.Fetch<SummaryRealStock>("where Id_Soko=@0", SokoId).ToDictionary(x => x.Id_Siz, x => x.Su);
		Assert.AreEqual(4, stock[2], "仕入で在庫が増える");
		Assert.AreEqual(2, stock[3], "移動受で在庫が増える");
	}

	[TestMethod]
	public void ApplyReceiveBatch_棚卸は倉庫と棚番ごとに棚卸データを作る() {
		var batchId = Upload("WMS_INVENTORY_1.csv", LogisticsDataKind.INVENTORY,
			["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "M", "", "3", ""],
			["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "L", "", "1", ""],
			["INVENTORY", "20261005", "S01", "B-1", "A001", "01", "M", "", "2", ""]).Kinds.Single().BatchId;

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);

		var tana = Db.Fetch<Tran60Tana>("order by TanaNo");
		Assert.AreEqual(2, tana.Count);
		Assert.AreEqual(("A-1", 4, SokoId, "20261005"), (tana[0].TanaNo, tana[0].SuTotal, tana[0].Id_Soko, tana[0].DenDay));
		Assert.AreEqual(0, Db.Fetch<SummaryRealStock>("").Count, "棚卸データは在庫を動かさない");
	}

	[TestMethod]
	public void ExecuteLineAction_除外と訂正版で原文を変えずに再検査する() {
		Stock(siz: 2, su: 10);
		var a = SendHaibun(su: 3);
		var b = SendHaibun(su: 3, tenpo: InsertTokui("T02", "店2", 6));
		var batchId = Upload("WMS_ORDERFIX_1.csv", LogisticsDataKind.ORDERFIX,
			OrderFix(a, "9", "0"), OrderFix(b, "9", "0")).Kinds.Single().BatchId;
		var lines = Lines(batchId);
		Assert.IsTrue(lines.All(l => l.ErrorCode == "E12"));

		var fields = LogisticsFileFormat.Parse(LogisticsDataKind.ORDERFIX, lines[0].RawText).Single().Fields.ToArray();
		fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.ORDERFIX, "確定数")] = "3";
		Logistics.ExecuteLineAction(new LogisticsLineActionParam(lines[0].Id, EnumLogisticsLineAction.Correct, fields, "数量誤り", 0), 0);
		Logistics.ExecuteLineAction(new LogisticsLineActionParam(lines[1].Id, EnumLogisticsLineAction.Exclude, [], "連携先で取消", 0), 0);

		lines = Lines(batchId);
		Assert.AreEqual(3, lines.Count);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Corrected, lines[0].Status);
		Assert.IsTrue(lines[0].RawText.Contains("\"9\""), "原文は変えない");
		var corrected = lines.Single(l => l.Id_LineOrg == lines[0].Id);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, corrected.Status, corrected.ErrorMsg);
		Assert.AreEqual(1, lines.Count(l => l.Status == (int)EnumLogisticsLineStatus.Excluded));

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);
		Assert.AreEqual(3, Db.Fetch<TranHaibun>("where Id=@0", a).Single().JitsuSu);
		Assert.AreEqual(0, Db.Fetch<TranHaibun>("where Id=@0", b).Single().EndFlag, "除外した行は反映しない");
		Assert.AreEqual((int)EnumLogisticsReceiveStatus.Applied, Batch(batchId).Status);
	}

	[TestMethod]
	public void 重複受信_仕入入荷と棚卸は内容の違う再送でも二重に計上しない() {
		var hachu = InsertHachu(su: 10);
		Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.STOCK, "20261005", [], [hachu], 0), 0);
		var first = Upload("WMS_STOCKFIX_1.csv", LogisticsDataKind.STOCKFIX, StockFix("20", hachu, "S01", "M", "4")).Kinds.Single().BatchId;
		var pendingOther = Upload("WMS_STOCKFIX_2.csv", LogisticsDataKind.STOCKFIX, StockFix("20", hachu, "S01", "M", "3", day: "20261007")).Kinds.Single().BatchId;
		Assert.AreEqual("E13", Lines(pendingOther).Single().ErrorCode, "同じ発注の未反映の受信が他にある");

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(first, 0), 0);
		var resend = Upload("WMS_STOCKFIX_3.csv", LogisticsDataKind.STOCKFIX, StockFix("20", hachu, "S01", "M", "4"), StockFix("20", hachu, "S01", "M", "0")).Kinds.Single().BatchId;
		Assert.AreEqual("E14", Lines(resend)[0].ErrorCode, "同じ発注・入荷日・SKU・数量が反映済み");
		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(resend, 0), 0);
		Assert.AreEqual(1, Db.Fetch<Tran03Shiire>("").Count);

		Logistics.RecheckBatch(new LogisticsRecheckParam(pendingOther, 0), 0);
		Assert.AreEqual((int)EnumLogisticsLineStatus.Pending, Lines(pendingOther).Single().Status, "先の受信を反映すれば分割入荷として受けられる");

		var inv = Upload("WMS_INVENTORY_1.csv", LogisticsDataKind.INVENTORY, ["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "M", "", "3", ""]).Kinds.Single().BatchId;
		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(inv, 0), 0);
		var invAgain = Upload("WMS_INVENTORY_2.csv", LogisticsDataKind.INVENTORY, ["INVENTORY", "20261005", "S01", "A-1", "A001", "01", "M", "", "4", ""]).Kinds.Single().BatchId;
		Assert.AreEqual("E31", Lines(invAgain).Single().ErrorCode);
	}

	[TestMethod]
	public void ApplyReceiveBatch_卸先への出荷確定は出荷売上を作り売掛を集計する() {
		Stock(siz: 2, su: 10);
		var oroshi = InsertTokui("O01", "卸先", 1);
		var a = SendHaibun(su: 3, tenpo: oroshi);
		var batchId = Upload("WMS_ORDERFIX_1.csv", LogisticsDataKind.ORDERFIX, OrderFix(a, "3", "0")).Kinds.Single().BatchId;

		Logistics.ApplyReceiveBatch(new LogisticsApplyParam(batchId, 0), 0);

		var line = Lines(batchId).Single();
		Assert.AreEqual(nameof(Tran00Uriage), line.TargetTable, line.ErrorMsg);
		Assert.AreEqual(1, Db.Fetch<SummaryUriKake>("where Id_Tokui=@0", oroshi).Count, "売掛を集計し直す");
	}

	[TestMethod]
	public void ExecuteBatchAction_作成中のまま残った送信バッチも取消できる() {
		var a = SendHaibun(su: 3);
		var batchId = Db.Fetch<TranLogisticsBatch>("where Direction=1").Single().Id;
		Db.Execute("update TranLogisticsBatch set Status=0 where Id=@0", batchId);
		Db.Execute("update TranHaibun set SendFlg=1 where Id=@0", a);

		Logistics.ExecuteBatchAction(new LogisticsBatchActionParam(batchId, EnumLogisticsBatchAction.Cancel, 0), 0);

		Assert.AreEqual(0, Db.Fetch<TranHaibun>("where Id=@0", a).Single().SendFlg);
	}

	private void Stock(long siz, int su) {
		var existing = Db.Fetch<SummaryRealStock>("where Id_Soko=@0 and Id_Siz=@1", SokoId, siz).FirstOrDefault();
		if (existing != null) {
			Db.Execute("update SummaryRealStock set Su=@0 where Id=@1", su, existing.Id);
			return;
		}
		Db.Insert(new SummaryRealStock { Id_Soko = SokoId, Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = siz, Su = su });
	}

	private TranLogisticsBatch Batch(long id) => Db.Fetch<TranLogisticsBatch>("where Id=@0", id).Single();
}
