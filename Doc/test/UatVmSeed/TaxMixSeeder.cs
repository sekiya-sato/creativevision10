using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// C-04 明細別消費税（標準／軽減混在）を「伝票税額再更新」画面から検証するためのデータを投入する。
/// </summary>
/// <remarks>
/// <para>
/// `TranTaxRebuildDb`は「明細Tax合計が0の伝票」だけを対象に、商品ごとの`Id_Tax`で明細税額を
/// 投入する一時処理である。実DBの対象伝票は既に0件（済み）のため、対象データが無いままでは
/// 画面を実行しても何も起きず検証にならない。そこで**未処理状態の伝票を1件だけ新規投入**する。
/// </para>
/// <para>
/// 商品は新規に作らず、既存の実商品をそのまま参照する。標準税率(`Id_Tax=1`)1件と、
/// `Doc/spec/tools/taxmix`で検証済みの軽減税率(`Id_Tax=2`)商品1件を明細に使う。
/// マスタには一切書き込まず、影響は投入した1伝票に閉じる。
/// </para>
/// </remarks>
public static class TaxMixSeeder {
	/// <summary>軽減税率の検証用商品コード（`Doc/spec/tools/taxmix/README.md`に記載、開発DBの実値）。</summary>
	private const string ReducedTaxShohinCode = "20617565001";

	/// <summary>投入した売上伝票の伝票日付。施行日(20191001)以降なので新税率が適用される。</summary>
	public const string DenDay = "20260731";

	/// <summary>標準税率明細の金額（税抜）。</summary>
	public const int StandardKingaku = 10_000;
	/// <summary>軽減税率明細の金額（税抜）。</summary>
	public const int ReducedKingaku = 5_000;
	/// <summary>標準税率(%)。taxmix `inspect`で確認済み。</summary>
	public const int StandardRate = 10;
	/// <summary>軽減税率(%)。taxmix `inspect`で確認済み。</summary>
	public const int ReducedRate = 8;

	/// <summary>投入結果。</summary>
	public sealed record Result(long DenId, long StandardShohinId, long ReducedShohinId, int ExpectedTax, int ExpectedTotal);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };

		var standardShohin = db.Fetch<MasterShohin>("where Id_Tax=1 LIMIT 1").FirstOrDefault()
			?? throw new InvalidOperationException("標準税率(Id_Tax=1)の商品が見つかりません。");
		var reducedShohin = db.Fetch<MasterShohin>("where Code=@0", ReducedTaxShohinCode).FirstOrDefault()
			?? throw new InvalidOperationException($"軽減税率の検証用商品が見つかりません: {ReducedTaxShohinCode}");
		if (reducedShohin.Id_Tax != 2) {
			throw new InvalidOperationException($"検証用商品の税区分が想定と異なります: Code={ReducedTaxShohinCode} Id_Tax={reducedShohin.Id_Tax}");
		}

		var tokui = db.Fetch<MasterTokui>("where Code=@0", ShimeBoundarySeeder.TokuiCode).FirstOrDefault()
			?? throw new InvalidOperationException($"得意先が見つかりません（先にShimeBoundarySeederを実行すること）: {ShimeBoundarySeeder.TokuiCode}");
		var employee = db.Fetch<MasterShain>("order by Id").First();
		var warehouse = db.Fetch<MasterTokui>("where TenType=0 LIMIT 1").FirstOrDefault()
			?? throw new InvalidOperationException("倉庫（TenType=0）が見つかりません。");

		Clean(db, tokui.Id, trace);

		var meisai = new List<Tran99Meisai> {
			new() { No = 1, Id_Shohin = standardShohin.Id, Code_Shohin = standardShohin.Code ?? "", Mei_Shohin = standardShohin.Name ?? "", Su = 1, Tanka = StandardKingaku, Kingaku = StandardKingaku },
			new() { No = 2, Id_Shohin = reducedShohin.Id, Code_Shohin = reducedShohin.Code ?? "", Mei_Shohin = reducedShohin.Name ?? "", Su = 1, Tanka = ReducedKingaku, Kingaku = ReducedKingaku },
		};
		var kingakuTotal = StandardKingaku + ReducedKingaku;

		var den = new Tran00Uriage {
			DenDay = DenDay,
			KakeDay = DenDay,
			Id_Tokui = tokui.Id,
			VTokui = new CodeNameView(tokui.Id, tokui.Code, tokui.Name),
			Id_Soko = warehouse.Id,
			VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			SuTotal = meisai.Sum(m => m.Su),
			KingakuTotal = kingakuTotal,
			// ヘッダ税額・課税対象額・明細税額は未設定(0)のまま投入する。TranTaxRebuildDbが
			// 得意先(UATVM-T20、伝票単位)の現在値から再スナップショットしてTax1/2/3・TaxableAmount1/2/3を
			// 確定させることを検証するため、TaxCalcUnitもここでは設定しない(RebuildGenericが上書きする)。
			Tax1 = 0,
			Tax2 = 0,
			Tax3 = 0,
			Total = kingakuTotal,
			IsPay = 1,
			Jmeisai = meisai,
		};
		den.EnKubun = EnumUri00.Uriage;

		var vdate = Common.GetVdate();
		den.Vdc = vdate;
		den.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(den);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"未処理の売上伝票を投入 Id={den.Id} 標準税率明細={StandardKingaku:N0}円 軽減税率明細={ReducedKingaku:N0}円 ヘッダTax=0(未設定)");

		var expectedTax = (int)Math.Round(StandardKingaku * StandardRate / 100.0) + (int)Math.Round(ReducedKingaku * ReducedRate / 100.0);
		var expectedTotal = kingakuTotal + expectedTax;
		trace($"期待値 ヘッダTax={expectedTax:N0} Total={expectedTotal:N0}"
			+ $"（標準{StandardKingaku:N0}×{StandardRate}%={StandardKingaku * StandardRate / 100:N0} + 軽減{ReducedKingaku:N0}×{ReducedRate}%={ReducedKingaku * ReducedRate / 100:N0}）");

		return new Result(den.Id, standardShohin.Id, reducedShohin.Id, expectedTax, expectedTotal);
	}

	/// <summary>再実行で累積しないよう、対象得意先の当該伝票日の売上だけを消す。</summary>
	private static void Clean(ExDatabaseSqlite db, long tokuiId, Action<string> trace) {
		var deleted = db.Execute("DELETE FROM Tran00Uriage WHERE Id_Tokui=@0 AND DenDay=@1", tokuiId, DenDay);
		trace($"掃除 Tran00Uriage(Id_Tokui={tokuiId}, DenDay={DenDay})={deleted}");
	}
}
