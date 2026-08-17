using System.Collections.Generic;
using System.Linq;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// テーブル更新に伴う副作用の起動順序(<see cref="WriteEffectRunner"/>)を検証する。
/// <para>
/// 個々の計算(<see cref="SummaryDb"/> / <see cref="MasterCascadeDb"/> / <see cref="DerivedDb"/>)は
/// SummaryDbTests / MasterCascadeDbTests が担保しているので、ここでは
/// 「どの操作でどの副作用が何回走るか」と「在庫の反転→更新→再計算の順序」だけを固定する。
/// </para>
/// </summary>
[TestClass]
public class WriteEffectRunnerTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"WriteEffectRunnerTests-{System.Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
		_db.KeepConnectionAlive = true;
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	// ===== 引当(ITranReserve) =====

	/// <summary>追加で引当が加算され、削除で戻ることを確認する</summary>
	[TestMethod]
	public void After_InsertAndDelete_TranHaibun_UpdatesReserveQty() {
		PrepareStockTables();
		var runner = new WriteEffectRunner(Db);

		var haibun = CreateHaibun("20260815", 1, 7);
		Db.Insert(haibun);
		var inserted = runner.After(WriteOp.Insert, typeof(TranHaibun), haibun, null, 1);

		Assert.AreEqual(7, MonthReserve("202608", 1), "追加で引当が加算される");
		Assert.AreEqual(7, RealReserve(1), "実在庫側の引当も加算される");
		Assert.IsTrue(inserted.Reserve > 0, "引当の更新行数が返る");
		Assert.AreEqual(0, inserted.Stock, "配分は在庫を動かさない");
		Assert.AreEqual(0, inserted.Derived, "配分は派生テーブルを持たない");
		Assert.AreEqual(0, inserted.Cascade, "追加ではV*列伝播しない");

		Db.Delete(haibun);
		runner.After(WriteOp.Delete, typeof(TranHaibun), haibun, haibun, 0);

		Assert.AreEqual(0, MonthReserve("202608", 1), "削除で引当が戻る");
		Assert.AreEqual(0, RealReserve(1), "実在庫側の引当も戻る");
	}

	/// <summary>倉庫が変わる修正で、旧キーと新キーの両方が引き直されることを確認する</summary>
	[TestMethod]
	public void After_Update_TranHaibun_RecalculatesBothOldAndNewKey() {
		PrepareStockTables();
		var runner = new WriteEffectRunner(Db);

		var haibun = CreateHaibun("20260815", 1, 7);
		Db.Insert(haibun);
		runner.After(WriteOp.Insert, typeof(TranHaibun), haibun, null, 1);
		Assert.AreEqual(7, RealReserve(1));

		// 修正前の姿を取っておき、倉庫を1→2へ動かす
		var org = CreateHaibun("20260815", 1, 7);
		org.Id = haibun.Id;
		haibun.Id_Soko = 2;
		Db.Update(haibun);
		runner.After(WriteOp.Update, typeof(TranHaibun), haibun, org, 2);

		Assert.AreEqual(0, RealReserve(1), "旧倉庫の引当が消える");
		Assert.AreEqual(7, RealReserve(2), "新倉庫へ引当が移る");
	}

	/// <summary>
	/// 一括登録では引当キーを貯めるだけで、FlushReserve を呼ぶまで再計算されないことを確認する。
	/// 在庫と違い引当はキー単位の引き直しなので、まとめて1回で正しい値になる。
	/// </summary>
	[TestMethod]
	public void After_WithReserveKeys_DefersRecalculationUntilFlush() {
		PrepareStockTables();
		var runner = new WriteEffectRunner(Db);
		var keys = new HashSet<ReserveKey>();

		foreach (var su in new[] { 3, 4, 5 }) {
			var row = CreateHaibun("20260815", 1, su);
			Db.Insert(row);
			var result = runner.After(WriteOp.Insert, typeof(TranHaibun), row, null, 1, keys);
			Assert.AreEqual(0, result.Reserve, "貯めている間は引当を再計算しない");
		}
		Assert.AreEqual(0, RealReserve(1), "Flushまでは引当が更新されない");
		Assert.AreEqual(1, keys.Count, "同じキーの3行は1キーにまとまる");

		var flushed = runner.FlushReserve(keys);

		Assert.IsTrue(flushed > 0, "Flushで更新行数が返る");
		Assert.AreEqual(12, RealReserve(1), "3行の合計が一度に反映される");
		Assert.AreEqual(12, MonthReserve("202608", 1));
	}

	/// <summary>キーが空の Flush は何もしない</summary>
	[TestMethod]
	public void FlushReserve_WithNoKeys_DoesNothing() {
		PrepareStockTables();
		Assert.AreEqual(0, new WriteEffectRunner(Db).FlushReserve([]));
	}

	// ===== 在庫(ITranSoko / ITranIdo) =====

	/// <summary>
	/// 更新は「Beforeで反転 → DB更新 → Afterで再計算」の順序でなければ正しい在庫にならない。
	/// 移動伝票(ITranIdo)なので倉庫軸と移動先軸の両方が動く。
	/// </summary>
	[TestMethod]
	public void BeforeAndAfter_Update_Tran05Ido_InvertsOldValueThenAppliesNew() {
		PrepareStockTables();
		Db.CreateTable(typeof(Tran05Ido), true, false);
		var runner = new WriteEffectRunner(Db);

		var tran = CreateTransfer("20260815", 1, 2, 7);
		Db.Insert(tran);
		var inserted = runner.After(WriteOp.Insert, typeof(Tran05Ido), tran, null, 1);

		Assert.AreEqual(-7, RealStock(1), "出庫元が減る");
		Assert.AreEqual(7, RealStock(2), "移動先が増える");
		Assert.IsTrue(inserted.Stock > 0, "在庫の更新行数が返る");

		// 数量を 7 → 3 へ修正する
		var org = Db.Fetch(typeof(Tran05Ido), "where Id=@0", tran.Id).OfType<Tran05Ido>().First();
		runner.Before(WriteOp.Update, typeof(Tran05Ido), org);

		Assert.AreEqual(0, RealStock(1), "Beforeの反転で旧値が打ち消される");
		Assert.AreEqual(0, RealStock(2));

		tran.Jmeisai![0].Su = 3;
		Db.Update(tran);
		runner.After(WriteOp.Update, typeof(Tran05Ido), tran, org, 2);

		Assert.AreEqual(-3, RealStock(1), "新しい数量で計算し直される");
		Assert.AreEqual(3, RealStock(2));
	}

	/// <summary>削除は Before の反転だけで完結し、After は在庫を触らない</summary>
	[TestMethod]
	public void BeforeAndAfter_Delete_Tran05Ido_OnlyInverts() {
		PrepareStockTables();
		Db.CreateTable(typeof(Tran05Ido), true, false);
		var runner = new WriteEffectRunner(Db);

		var tran = CreateTransfer("20260815", 1, 2, 7);
		Db.Insert(tran);
		runner.After(WriteOp.Insert, typeof(Tran05Ido), tran, null, 1);
		Assert.AreEqual(-7, RealStock(1));

		runner.Before(WriteOp.Delete, typeof(Tran05Ido), tran);
		Db.Delete(tran);
		var result = runner.After(WriteOp.Delete, typeof(Tran05Ido), tran, tran, 0);

		Assert.AreEqual(0, RealStock(1), "反転だけで在庫が戻る");
		Assert.AreEqual(0, RealStock(2));
		Assert.AreEqual(0, result.Stock, "削除の後処理では在庫を計算しない");
	}

	/// <summary>追加には打ち消す旧値が無いので Before は何もしない</summary>
	[TestMethod]
	public void Before_Insert_DoesNothing() {
		PrepareStockTables();
		Db.CreateTable(typeof(Tran05Ido), true, false);
		var tran = CreateTransfer("20260815", 1, 2, 7);
		Db.Insert(tran);

		Assert.AreEqual(0, new WriteEffectRunner(Db).Before(WriteOp.Insert, typeof(Tran05Ido), tran));
		Assert.AreEqual(0, RealStock(1), "在庫が動かない");
	}

	// ===== 派生テーブル(IDerivedOrigin) =====

	/// <summary>商品マスタの追加・更新・削除に追随して派生テーブルが展開・解除される</summary>
	[TestMethod]
	public void After_MasterShohin_ExpandsAndRemovesDerivedRows() {
		Db.CreateTable(typeof(MasterShohin), true, false);
		Db.CreateTable(typeof(DerivedShohinColSiz), true, false);
		var runner = new WriteEffectRunner(Db);

		var shohin = new MasterShohin {
			Code = "0001",
			Name = "サンプル商品",
			Jcolsiz = [
				new MasterShohinColSiz { Id_Col = 100, Code_Col = "01", Mei_Col = "赤", Id_Siz = 1000, Code_Siz = "01", Mei_Siz = "M" },
				new MasterShohinColSiz { Id_Col = 101, Code_Col = "02", Mei_Col = "青", Id_Siz = 1000, Code_Siz = "01", Mei_Siz = "M" },
			],
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);
		var inserted = runner.After(WriteOp.Insert, typeof(MasterShohin), shohin, null, 1);

		Assert.AreEqual(2, DerivedCount(shohin.Id), "色サイズが展開される");
		Assert.AreEqual(2, inserted.Derived, "展開行数が返る");
		Assert.AreEqual(0, inserted.Stock, "商品マスタは在庫を動かさない");

		// 色サイズを1件に減らして更新すると、削除→再展開で追随する
		shohin.Jcolsiz = [shohin.Jcolsiz[0]];
		Db.Update(shohin);
		runner.After(WriteOp.Update, typeof(MasterShohin), shohin, shohin, 2);

		Assert.AreEqual(1, DerivedCount(shohin.Id), "再展開で1件になる");

		Db.Delete(shohin);
		runner.After(WriteOp.Delete, typeof(MasterShohin), shohin, shohin, 0);

		Assert.AreEqual(0, DerivedCount(shohin.Id), "削除で展開が解除される");
	}

	// ===== V*列伝播(IBaseCodeName) =====

	/// <summary>マスタの名称変更は更新時だけ伝播し、追加では伝播しない</summary>
	[TestMethod]
	public void After_Update_MasterMeisho_CascadesVColumn() {
		CreateCascadeTables();
		var runner = new WriteEffectRunner(Db);

		var brand = new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		var org = new MasterMeisho { Id = brand.Id, Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(new MasterShohin {
			Code = "0001",
			Id_Brand = brand.Id,
			VBrand = new CodeNameView { Sid = brand.Id, Cd = "01", Mei = "旧ブランド" },
			Vdc = 1,
			Vdu = 1,
		});

		// 追加では伝播しない
		Assert.AreEqual(0, runner.After(WriteOp.Insert, typeof(MasterMeisho), brand, null, 1).Cascade);

		brand.Name = "新ブランド";
		Db.Update(brand);
		var updated = runner.After(WriteOp.Update, typeof(MasterMeisho), brand, org, 20260816123000);

		Assert.IsTrue(updated.Cascade > 0, "伝播行数が返る");
		Assert.AreEqual("新ブランド", Db.First<MasterShohin>("where Code='0001'").VBrand.Mei, "参照側のV*列が現行名称になる");
	}

	/// <summary>Code/Name が変わっていなければ伝播しない(無駄なUPDATEを流さない)</summary>
	[TestMethod]
	public void After_Update_WithoutNameChange_DoesNotCascade() {
		CreateCascadeTables();
		var brand = new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		var org = new MasterMeisho { Id = brand.Id, Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "ブランド", Vdc = 1, Vdu = 1 };

		Assert.AreEqual(0, new WriteEffectRunner(Db).After(WriteOp.Update, typeof(MasterMeisho), brand, org, 2).Cascade);
	}

	// ===== 部分更新 =====

	/// <summary>EndFlag を含む部分更新だけが引当を引き直す</summary>
	[TestMethod]
	public void AfterPartialUpdate_OnlyRecalculatesWhenEndFlagIsUpdated() {
		PrepareStockTables();
		var runner = new WriteEffectRunner(Db);

		var haibun = CreateHaibun("20260815", 1, 7);
		Db.Insert(haibun);
		runner.After(WriteOp.Insert, typeof(TranHaibun), haibun, null, 1);
		Assert.AreEqual(7, RealReserve(1));

		// EndFlag=1(入庫済) にすると引当が解除される
		Db.Execute("update TranHaibun set EndFlag = 1 where Id = @0", haibun.Id);
		var updated = runner.AfterPartialUpdate(typeof(TranHaibun), [nameof(TranHaibun.EndFlag)], [haibun.Id]);

		Assert.IsTrue(updated > 0);
		Assert.AreEqual(0, RealReserve(1), "入庫済になった行は引当から外れる");

		// EndFlag を含まない部分更新では何もしない
		Assert.AreEqual(0, runner.AfterPartialUpdate(typeof(TranHaibun), [nameof(TranHaibun.Memo)], [haibun.Id]));
		// 引当を持たない型でも何もしない
		Assert.AreEqual(0, runner.AfterPartialUpdate(typeof(MasterShohin), [nameof(TranHaibun.EndFlag)], [haibun.Id]));
		// 対象行が無ければ何もしない
		Assert.AreEqual(0, runner.AfterPartialUpdate(typeof(TranHaibun), [nameof(TranHaibun.EndFlag)], []));
	}

	/// <summary>
	/// 部分更新の禁止列に、副作用が読む列が漏れなく入っていることを確認する(定義の腐り検出)。
	/// </summary>
	[TestMethod]
	public void PartialUpdateDeniedColumns_CoverAuditAndSideEffectColumns() {
		var denied = WriteEffectRunner.PartialUpdateDeniedColumns;

		foreach (var column in new[] {
			nameof(BaseDbClass.Id), nameof(BaseDbClass.Vdc), nameof(BaseDbClass.Vdu),
			nameof(ITranSoko.Id_Soko), nameof(ITranIdo.Id_Ido),
			nameof(ITranReserve.Id_Shohin), nameof(ITranReserve.Id_Col), nameof(ITranReserve.Id_Siz),
			nameof(ITranReserve.Su), nameof(ITranReserve.DenDay),
		}) {
			Assert.IsTrue(denied.Contains(column, System.StringComparer.OrdinalIgnoreCase),
				$"副作用が読む列 {column} は部分更新で変更できてはいけない");
		}
		// EndFlag は AfterPartialUpdate が引き直すので、部分更新できなければならない
		Assert.IsFalse(denied.Contains(nameof(ITranReserve.EndFlag), System.StringComparer.OrdinalIgnoreCase));
	}

	// ===== 発注残・受注残の自動完了(CompletionDb) =====

	/// <summary>
	/// 発注は「明細単位で全SKUが充足」した時点で自動完了する(決定 G0-c)。
	/// 伝票合計では判定しないので、1SKUでも不足なら未完了のままになる。
	/// </summary>
	[TestMethod]
	public void After_Shiire_CompletesHachuOnlyWhenEverySkuIsFilled() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		// 発注: SKU-A 10枚 / SKU-B 5枚
		var hachu = CreateHachu([(10, 100, 1000, 10), (10, 100, 1001, 5)]);
		Db.Insert(hachu);

		// 合計15枚だがSKU-Aへ寄せた仕入。合計では足りるが SKU-B が不足なので完了しない
		var shiire1 = CreateShiire(hachu.Id, [(10, 100, 1000, 15)]);
		Db.Insert(shiire1);
		var r1 = runner.After(WriteOp.Insert, typeof(Tran03Shiire), shiire1, null, 1);

		Assert.AreEqual(0, r1.Completion);
		Assert.AreEqual(0, GetEndFlag(nameof(Tran13Hachu), hachu.Id), "SKU-Bが未充足なら完了しない");

		// SKU-B を満たすと完了する
		var shiire2 = CreateShiire(hachu.Id, [(10, 100, 1001, 5)]);
		Db.Insert(shiire2);
		var r2 = runner.After(WriteOp.Insert, typeof(Tran03Shiire), shiire2, null, 1);

		Assert.AreEqual(1, r2.Completion);
		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id));
	}

	/// <summary>いったん完了したものは仕入を削除しても自動では戻らない(決定 4.3.1)</summary>
	[TestMethod]
	public void After_DeleteShiire_DoesNotReopenCompletedHachu() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var hachu = CreateHachu([(10, 100, 1000, 10)]);
		Db.Insert(hachu);
		var shiire = CreateShiire(hachu.Id, [(10, 100, 1000, 10)]);
		Db.Insert(shiire);
		runner.After(WriteOp.Insert, typeof(Tran03Shiire), shiire, null, 1);
		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id));

		Db.Delete(shiire);
		runner.After(WriteOp.Delete, typeof(Tran03Shiire), shiire, shiire, 2);

		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id), "完了は自動で戻さない。戻すのは残完了設定からの手動操作だけ");
		CollectionAssert.AreEqual(new[] { hachu.Id },
			new CompletionDb(Db).FindCompleted(typeof(Tran13Hachu), [hachu.Id]).ToArray(),
			"編集時ワーニング用に完了済みとして検出できる");
	}

	/// <summary>仕入返品(CalcFlag=-1)は符号付きで数えるため、充足が取り消される</summary>
	[TestMethod]
	public void After_Henpin_KeepsHachuIncompleteBySignedQuantity() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var hachu = CreateHachu([(10, 100, 1000, 10)]);
		Db.Insert(hachu);
		var shiire = CreateShiire(hachu.Id, [(10, 100, 1000, 10)]);
		var henpin = CreateShiire(hachu.Id, [(10, 100, 1000, 4)], EnumShiire.Henpin);
		Db.Insert(shiire);
		Db.Insert(henpin);

		// 10 - 4 = 6 で発注10に足りない
		Assert.AreEqual(0, runner.After(WriteOp.Insert, typeof(Tran03Shiire), henpin, null, 1).Completion);
		Assert.AreEqual(0, GetEndFlag(nameof(Tran13Hachu), hachu.Id));
	}

	/// <summary>
	/// 受注は「受注伝票Idで紐付いた出荷売上」で消化する。
	/// 対象は出荷先の店種区分が卸先(1)か売仕店(3)のものだけで、直営店(6)は移動扱いなので数えない(決定 G4 / I4)。
	/// </summary>
	[TestMethod]
	public void After_Uriage_CompletesJuchuOnlyForOroshiAndUrishiTenType() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var oroshiId = InsertTokui("T001", "卸先", tenType: 1);
		var chokueiId = InsertTokui("T006", "直営店", tenType: 6);
		var juchu = CreateJuchu([(10, 100, 1000, 10)]);
		Db.Insert(juchu);

		// 直営店への出荷は受注残を消化しない
		var chokuei = CreateUriage(juchu.Id, chokueiId, [(10, 100, 1000, 10)]);
		Db.Insert(chokuei);
		Assert.AreEqual(0, runner.After(WriteOp.Insert, typeof(Tran00Uriage), chokuei, null, 1).Completion);
		Assert.AreEqual(0, GetEndFlag(nameof(Tran12Jyuchu), juchu.Id));

		// 卸先への出荷で完了する
		var oroshi = CreateUriage(juchu.Id, oroshiId, [(10, 100, 1000, 10)]);
		Db.Insert(oroshi);
		Assert.AreEqual(1, runner.After(WriteOp.Insert, typeof(Tran00Uriage), oroshi, null, 1).Completion);
		Assert.AreEqual(1, GetEndFlag(nameof(Tran12Jyuchu), juchu.Id));
	}

	/// <summary>紐付け(RelateNo1)の無い出荷は受注残を消化しない</summary>
	[TestMethod]
	public void After_UriageWithoutRelateNo_DoesNotCompleteJuchu() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var oroshiId = InsertTokui("T001", "卸先", tenType: 1);
		var juchu = CreateJuchu([(10, 100, 1000, 10)]);
		Db.Insert(juchu);
		var uriage = CreateUriage(0, oroshiId, [(10, 100, 1000, 10)]);
		Db.Insert(uriage);

		Assert.AreEqual(0, runner.After(WriteOp.Insert, typeof(Tran00Uriage), uriage, null, 1).Completion);
		Assert.AreEqual(0, GetEndFlag(nameof(Tran12Jyuchu), juchu.Id));
	}

	/// <summary>
	/// 残完了設定画面が使う `EndFlag` の部分更新は、在庫にも引当にも副作用を起こさない。
	/// 消込と同じく `PartialUpdateParam` で書き戻すため、副作用の起動対象外であることを固定する。
	/// </summary>
	[TestMethod]
	public void AfterPartialUpdate_HachuEndFlag_HasNoSideEffect() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var hachu = CreateHachu([(10, 100, 1000, 10)]);
		Db.Insert(hachu);

		// 残完了設定画面と同じ更新（EndFlag だけを立てる）
		Db.Execute("update Tran13Hachu set EndFlag = 1 where Id = @0", hachu.Id);
		var effect = runner.AfterPartialUpdate(typeof(Tran13Hachu), [nameof(Tran13Hachu.EndFlag)], [hachu.Id]);

		Assert.AreEqual(0, effect, "発注はITranReserveではないので引当再計算は走らない");
		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id));
		Assert.AreEqual(0, Db.Fetch<SummaryRealStock>("").Count, "在庫集計も動かない");
	}

	/// <summary>
	/// 残完了設定で手動解除したあと、実績が充足していれば次の書き込みで再び自動完了になる。
	/// 完了は「未完了かつ全SKU充足」でのみ立つ片方向の判定なので、解除しただけでは戻らない。
	/// </summary>
	[TestMethod]
	public void After_ShiireWrite_RecompletesManuallyClearedHachu() {
		PrepareCompletionTables();
		var runner = new WriteEffectRunner(Db);
		var hachu = CreateHachu([(10, 100, 1000, 10)]);
		Db.Insert(hachu);
		var shiire = CreateShiire(hachu.Id, [(10, 100, 1000, 10)]);
		Db.Insert(shiire);
		runner.After(WriteOp.Insert, typeof(Tran03Shiire), shiire, null, 1);
		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id));

		// 残完了設定から手動で解除する
		Db.Execute("update Tran13Hachu set EndFlag = 0 where Id = @0", hachu.Id);
		Assert.AreEqual(0, GetEndFlag(nameof(Tran13Hachu), hachu.Id));

		// 仕入をもう一度保存すると、充足しているので自動完了へ戻る
		var effect = runner.After(WriteOp.Update, typeof(Tran03Shiire), shiire, shiire, 2);

		Assert.AreEqual(1, effect.Completion);
		Assert.AreEqual(1, GetEndFlag(nameof(Tran13Hachu), hachu.Id));
	}

	// ===== ヘルパ =====

	private void PrepareCompletionTables() {
		Db.CreateTable(typeof(Tran13Hachu), true, false);
		Db.CreateTable(typeof(Tran12Jyuchu), true, false);
		Db.CreateTable(typeof(Tran03Shiire), true, false);
		Db.CreateTable(typeof(Tran00Uriage), true, false);
		Db.CreateTable(typeof(MasterTokui), true, false);
		// Tran03Shiire / Tran00Uriage は ITranSoko なので在庫集計も走る
		PrepareStockTables();
	}

	/// <summary>得意先を登録して採番されたIdを返す。Idは自動採番なので明示指定しても反映されない</summary>
	private long InsertTokui(string code, string name, int tenType) {
		var tokui = new MasterTokui { Code = code, Name = name, TenType = tenType };
		Db.Insert(tokui);
		return tokui.Id;
	}

	private int GetEndFlag(string table, long id) =>
		Db.FirstOrDefault<int>($"SELECT EndFlag FROM {table} WHERE Id = @0", id);

	private static List<Tran99Meisai> Meisai(IEnumerable<(long shohin, long col, long siz, int su)> rows) =>
		[.. rows.Select((r, i) => new Tran99Meisai {
			No = i + 1, Id_Shohin = r.shohin, Id_Col = r.col, Id_Siz = r.siz, Su = r.su,
		})];

	private static Tran13Hachu CreateHachu((long shohin, long col, long siz, int su)[] rows) => new() {
		DenDay = "20260801", Id_Soko = 1, Id_Shiire = 1, Jmeisai = Meisai(rows),
	};

	private static Tran12Jyuchu CreateJuchu((long shohin, long col, long siz, int su)[] rows) => new() {
		DenDay = "20260801", Id_Soko = 1, Id_Tokui = 1, Jmeisai = Meisai(rows),
	};

	private static Tran03Shiire CreateShiire(long relateNo1, (long shohin, long col, long siz, int su)[] rows,
		EnumShiire kubun = EnumShiire.Shiire) => new() {
		DenDay = "20260810", Id_Soko = 1, Id_Shiire = 1, RelateNo1 = (int)relateNo1,
		EnKubun = kubun, Jmeisai = Meisai(rows),
	};

	private static Tran00Uriage CreateUriage(long relateNo1, long idTokui,
		(long shohin, long col, long siz, int su)[] rows) => new() {
		DenDay = "20260812", Id_Soko = 1, Id_Tokui = idTokui, RelateNo1 = (int)relateNo1,
		Jmeisai = Meisai(rows),
	};

	private void PrepareStockTables() {
		Db.CreateTable(typeof(SummaryStock), true, false);
		Db.CreateTable(typeof(SummaryRealStock), true, false);
		Db.CreateTable(typeof(TranHaibun), true, false);
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
	}

	/// <summary>伝播定義に登場する全テーブルを作成する</summary>
	private void CreateCascadeTables() {
		var types = MasterCascadeDb.VRules
			.Select(r => r.Target)
			.Concat(MasterCascadeDb.VRules.Select(r => r.Source))
			.Distinct();
		foreach (var t in types) {
			Db.CreateTable(t, true, false);
		}
	}

	/// <summary>引当テスト用の配分行。既定は在庫配分(引当対象)・未確定とする</summary>
	private static TranHaibun CreateHaibun(string denDay, long idSoko, int su, int endFlag = 0,
		EnumHaibun kubun = EnumHaibun.Zaiko) => new() {
		DenDay = denDay,
		Id_Soko = idSoko,
		Id_Shohin = 10,
		Id_Col = 100,
		Id_Siz = 1000,
		Su = su,
		EndFlag = endFlag,
		Kubun = (int)kubun,
	};

	private static Tran05Ido CreateTransfer(string denDay, long idSoko, long idIdo, int su) => new() {
		DenDay = denDay,
		Id_Soko = idSoko,
		Id_Ido = idIdo,
		Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = 10, Id_Col = 100, Id_Siz = 1000, Su = su }],
	};

	private int MonthReserve(string sumMonth, long idSoko) =>
		Db.Fetch<SummaryStock>("where SumMonth=@0 and Id_Soko=@1 and Id_Shohin=10 and Id_Col=100 and Id_Siz=1000", sumMonth, idSoko)
			.Sum(x => x.ReserveQty);

	private int RealReserve(long idSoko) =>
		Db.Fetch<SummaryRealStock>("where Id_Soko=@0 and Id_Shohin=10 and Id_Col=100 and Id_Siz=1000", idSoko)
			.Sum(x => x.ReserveQty);

	private int RealStock(long idSoko) =>
		Db.Fetch<SummaryRealStock>("where Id_Soko=@0 and Id_Shohin=10 and Id_Col=100 and Id_Siz=1000", idSoko)
			.Sum(x => x.Su);

	private int DerivedCount(long idShohin) =>
		Db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0", idShohin).Count;
}
