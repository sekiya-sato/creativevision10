using System.IO;
using System.Linq;
using CvBase;
using CvBase.Share;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// L02 連携データ手動送信と L04 の送信バッチ操作（仕様 3.2〜3.3）。
/// </summary>
[TestClass]
public class LogisticsLinkDbSendTests : LogisticsTestBase {
	private const string ToDay = "20261005";
	private long _tenpoId;
	private long _oroshiId;
	private long _otherSokoId;

	[TestInitialize]
	public void Initialize() {
		_tenpoId = InsertTokui("T01", "直営店", 6);
		_oroshiId = InsertTokui("O01", "卸先", 1);
		_otherSokoId = InsertTokui("S99", "対象外倉庫", 0);
		Db.Insert(new MasterShohin {
			Code = "A001",
			Name = "商品A",
			Jcolsiz = [new MasterShohinColSiz { Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M", Jan1 = "4900000000011" }],
		});
	}

	[TestMethod]
	public void QuerySendCandidates_出荷指示は未送信未確定の確定可能区分で対象倉庫と指定日以前だけ() {
		var ok = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		var toOroshi = InsertHaibun(EnumHaibun.Juchu, _oroshiId, nouhinDay: "20261004");
		InsertHaibun(EnumHaibun.Reservation, _tenpoId);
		InsertHaibun(EnumHaibun.Zaiko, _tenpoId, idSoko: _otherSokoId);
		InsertHaibun(EnumHaibun.Zaiko, _tenpoId, nouhinDay: "20261006");
		InsertHaibun(EnumHaibun.Zaiko, _tenpoId, sendFlg: 2);
		InsertHaibun(EnumHaibun.Hatsukai, _tenpoId, arrivedSu: 0);
		var arrived = InsertHaibun(EnumHaibun.Hatsukai, _tenpoId, su: 5, arrivedSu: 2);

		var list = Logistics.QuerySendCandidates(new LogisticsSendQueryParam(LogisticsDataKind.ORDER, ToDay, []));

		CollectionAssert.AreEquivalent(new[] { ok, toOroshi, arrived }, list.Select(c => c.RefId).ToArray());
		Assert.AreEqual("20", list.Single(c => c.RefId == toOroshi).Kubun, "卸先は出荷売上");
		Assert.AreEqual("10", list.Single(c => c.RefId == ok).Kubun, "直営店は移動");
		Assert.AreEqual(2, list.Single(c => c.RefId == arrived).Su, "仕入配分は入荷済み数を送る");
		Assert.AreEqual("A001", list[0].ShohinCode);
		Assert.AreEqual("M", list[0].SizCode);
	}

	[TestMethod]
	public void CreateSendBatch_出荷指示は送信済みにして送信行とファイルを残し二重送信しない() {
		var a = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		var b = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		var c = InsertHaibun(EnumHaibun.Juchu, _oroshiId);

		var result = Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.ORDER, ToDay, [], [a, c], 0), idShain: 3);

		var kind = result.Kinds.Single();
		Assert.AreEqual(2, kind.Count);
		var flags = Db.Fetch<TranHaibun>("").ToDictionary(h => h.Id, h => h.SendFlg);
		Assert.AreEqual(2, flags[a]);
		Assert.AreEqual(0, flags[b], "選ばなかった行は送らない");
		Assert.AreEqual(2, flags[c]);
		var lines = Db.Fetch<TranLogisticsLine>("where Id_Batch=@0 order by LineNo", kind.BatchId);
		CollectionAssert.AreEqual(new[] { a, c }, lines.Select(l => l.RefId).ToArray());
		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.ORDER, ReadSendFile(kind.FileName));
		var headerNo = LogisticsFileFormat.ColumnIndex(LogisticsDataKind.ORDER, "指示伝票番号");
		Assert.AreEqual("1", parsed[0].Fields[headerNo]);
		Assert.AreEqual("2", parsed[1].Fields[headerNo], "出荷先が違えば別の指示伝票");
		Assert.AreEqual(lines[0].RawText, parsed[0].RawText, "送信行はファイルの1行そのもの");

		var again = Logistics.QuerySendCandidates(new LogisticsSendQueryParam(LogisticsDataKind.ORDER, ToDay, []));
		CollectionAssert.AreEqual(new[] { b }, again.Select(x => x.RefId).ToArray());
	}

	[TestMethod]
	public void CreateSendBatch_配置失敗なら送信中のまま残し再出力で送信済みにする() {
		var a = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		Directory.CreateDirectory(Folder);
		File.WriteAllText(Path.Combine(Folder, LogisticsSettings.SendDir), "フォルダを作れなくする");

		var result = Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.ORDER, ToDay, [], [], 0), 0);

		var batchId = result.Kinds.Single().BatchId;
		Assert.AreEqual((int)EnumLogisticsSendStatus.PlaceFailed, Batch(batchId).Status);
		Assert.AreEqual(1, Haibun(a).SendFlg, "送信中のまま");
		Assert.AreEqual(1, result.Warnings.Length);
		Assert.AreEqual(0, Logistics.QuerySendCandidates(new LogisticsSendQueryParam(LogisticsDataKind.ORDER, ToDay, [])).Count, "確保した行は再送対象にしない");

		File.Delete(Path.Combine(Folder, LogisticsSettings.SendDir));
		Logistics.ExecuteBatchAction(new LogisticsBatchActionParam(batchId, EnumLogisticsBatchAction.Rewrite, 0), 0);

		Assert.AreEqual((int)EnumLogisticsSendStatus.Placed, Batch(batchId).Status);
		Assert.AreEqual(2, Haibun(a).SendFlg);
		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.ORDER, ReadSendFile(Batch(batchId).FileName));
		Assert.AreEqual(a.ToString(), parsed.Single().Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.ORDER, "指示ID")]);
	}

	[TestMethod]
	public void ExecuteBatchAction_取消は配分を未送信へ戻し確定済みは戻さない() {
		var a = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		var b = InsertHaibun(EnumHaibun.Zaiko, _tenpoId);
		var batchId = Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.ORDER, ToDay, [], [], 0), 0).Kinds.Single().BatchId;
		Db.Execute("update TranHaibun set KakuteiDay='20261005', EndFlag=1 where Id=@0", b);

		var result = Logistics.ExecuteBatchAction(new LogisticsBatchActionParam(batchId, EnumLogisticsBatchAction.Cancel, 0), 0);

		Assert.AreEqual(0, Haibun(a).SendFlg);
		Assert.AreEqual(2, Haibun(b).SendFlg);
		Assert.AreEqual(1, result.Warnings.Length);
		Assert.AreEqual((int)EnumLogisticsSendStatus.Canceled, Batch(batchId).Status);
		Assert.ThrowsExactly<System.ArgumentException>(() =>
			Logistics.ExecuteBatchAction(new LogisticsBatchActionParam(batchId, EnumLogisticsBatchAction.Rewrite, 0), 0));
	}

	[TestMethod]
	public void CreateSendBatch_入荷予定は発注残と未受入の移動を送り取消まで再送しない() {
		var hachu = InsertHachu(su: 10);
		InsertShiire(hachu, su: 4);
		InsertHachu(su: 3, endFlag: 1);
		var ido = InsertIdoOut(su: 6);
		var received = InsertIdoOut(su: 2);
		Db.Insert(new Tran11IdoIn { DenDay = "20261003", Id_Soko = SokoId, Id_Ido = SokoId, RelateNo1 = received });

		var result = Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.STOCK, ToDay, [], [], 0), 0);

		var kind = result.Kinds.Single();
		var lines = Db.Fetch<TranLogisticsLine>("where Id_Batch=@0 order by LineNo", kind.BatchId);
		Assert.AreEqual(2, lines.Count);
		Assert.AreEqual((nameof(Tran13Hachu), hachu, 6), (lines[0].RefTable, lines[0].RefId, lines[0].Su), "発注残 10-4");
		Assert.AreEqual((nameof(Tran10IdoOut), ido, 6), (lines[1].RefTable, lines[1].RefId, lines[1].Su));
		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.STOCK, ReadSendFile(kind.FileName));
		Assert.AreEqual("V01", parsed[0].Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.STOCK, "取引先CD")]);
		Assert.AreEqual(0, Logistics.QuerySendCandidates(new LogisticsSendQueryParam(LogisticsDataKind.STOCK, ToDay, [])).Count, "送信済みは再送しない");

		Db.Execute("update Tran13Hachu set Vdu = Vdu + 1 where Id=@0", hachu);
		var batchRow = Logistics.QueryBatches(new LogisticsBatchQueryParam(1, LogisticsDataKind.STOCK, string.Empty, string.Empty, true)).Single();
		Assert.AreEqual(1, batchRow.ChangedAfterSend, "送信後の発注修正を検出する");

		Logistics.ExecuteBatchAction(new LogisticsBatchActionParam(kind.BatchId, EnumLogisticsBatchAction.Cancel, 0), 0);
		Assert.AreEqual(2, Logistics.QuerySendCandidates(new LogisticsSendQueryParam(LogisticsDataKind.STOCK, ToDay, [])).Count, "取消すれば再送できる");
	}

	[TestMethod]
	public void CreateSendBatch_在庫は有効在庫と積送中を出力し行は残さない() {
		Db.Insert(new SummaryRealStock { Id_Soko = SokoId, Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = 2, Su = 10, ReserveQty = 3 });
		Db.Insert(new SummaryRealStock { Id_Soko = _otherSokoId, Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = 2, Su = 5 });
		Db.Insert(new SummaryStock { SumMonth = "202609", Id_Soko = SokoId, Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = 2, TransitQty = 2 });
		Db.Insert(new SummaryStock { SumMonth = "202610", Id_Soko = SokoId, Id_Shohin = ShohinId, Id_Col = 1, Id_Siz = 2, TransitQty = 1 });

		var result = Logistics.CreateSendBatch(new LogisticsSendParam(LogisticsDataKind.ZAIKO, string.Empty, [], [], 0), 0);

		var parsed = LogisticsFileFormat.Parse(LogisticsDataKind.ZAIKO, ReadSendFile(result.Kinds.Single().FileName)).Single();
		Assert.AreEqual("7", parsed.Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.ZAIKO, "有効在庫")]);
		Assert.AreEqual("3", parsed.Fields[LogisticsFileFormat.ColumnIndex(LogisticsDataKind.ZAIKO, "積送中")]);
		Assert.AreEqual(0, Db.Fetch<TranLogisticsLine>("").Count);
	}

	private long ShohinId => Db.Fetch<MasterShohin>("").Single().Id;

	private long InsertHaibun(EnumHaibun kubun, long idTenpo, string nouhinDay = "", long idSoko = 0, int su = 3, int arrivedSu = 0, int sendFlg = 0) {
		var row = new TranHaibun {
			DenDay = "20261001",
			NouhinDay = nouhinDay,
			Id_Soko = idSoko == 0 ? SokoId : idSoko,
			Id_Tenpo = idTenpo,
			Kubun = (int)kubun,
			Id_Shohin = ShohinId,
			Id_Col = 1,
			Id_Siz = 2,
			Su = su,
			ArrivedSu = arrivedSu,
			SendFlg = sendFlg,
			Tanka = 500,
			Vdu = 100,
		};
		Db.Insert(row);
		return row.Id;
	}

	private long InsertHachu(int su, int endFlag = 0) {
		var row = new Tran13Hachu {
			DenDay = "20261001",
			NouhinDay = "20261010",
			Id_Soko = SokoId,
			Id_Shiire = 1,
			VShiire = new CodeNameView { Sid = 1, Cd = "V01", Mei = "仕入先" },
			Kubun = 10,
			EndFlag = endFlag,
			Jmeisai = [Meisai(su)],
			Vdu = 100,
		};
		Db.Insert(row);
		return row.Id;
	}

	private void InsertShiire(long hachuId, int su) =>
		Db.Insert(new Tran03Shiire { DenDay = "20261002", Id_Soko = SokoId, RelateNo1 = (int)hachuId, Jmeisai = [Meisai(su)] });

	private long InsertIdoOut(int su) {
		var row = new Tran10IdoOut { DenDay = "20261002", Id_Soko = _otherSokoId, Id_Ido = SokoId, Jmeisai = [Meisai(su)], Vdu = 100 };
		Db.Insert(row);
		return row.Id;
	}

	private Tran99Meisai Meisai(int su) => new() {
		No = 1, Id_Shohin = ShohinId, Code_Shohin = "A001", Id_Col = 1, Code_Col = "01", Id_Siz = 2, Code_Siz = "M", JanCode = "4900000000011", Su = su, Tanka = 500,
	};

	private TranLogisticsBatch Batch(long id) => Db.Fetch<TranLogisticsBatch>("where Id=@0", id).Single();
	private TranHaibun Haibun(long id) => Db.Fetch<TranHaibun>("where Id=@0", id).Single();
}
