using CvBase;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 入金消込 — 請求先配下の卸売上(Tran00Uriage)を掛計上日で一覧し、伝票単位に消込Flgを立てて
/// `Tran00Uriage.EndFlag` へ書き戻す。入金(Tran06Nyukin)は支払日で取得し、明細の区分別集計を並べて
/// 合計金額を比較できるようにする。
/// <para>
/// 消込は伝票単位の目印であり、充当金額・未充当金額・入金伝票との個別対応は保持しない。
/// 売掛残高は伝票金額ベースなので `SummaryUriKake` の値は消込の有無で変わらない。
/// 仕様は `Doc/spec/archive/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.1 を参照する。
/// </para>
/// <para>
/// 店舗売上(Tran01Tenuri)は含めない。店頭現金売上で売掛が立たないため。
/// 期間は債権側・入金側とも掛計上日(KakeDay)で切る。
/// `Tran00Uriage.IsPay` は旧システムの「掛計上FLG」の移行値で消込状態ではないため流用しない。
/// </para>
/// </summary>
public partial class NyukinMatchingViewModel : Helpers.BaseMatchingViewModel<Tran00Uriage, Tran06Nyukin> {
	protected override string QueryTitle => "入金消込";
	protected override string DenTableName => nameof(Tran00Uriage);
	protected override string DenToriIdColumn => nameof(Tran00Uriage.Id_Tokui);
	protected override string KinTableName => nameof(Tran06Nyukin);
	protected override string ToriMasterTableName => nameof(MasterTokui);
	protected override string ToriMasterWhereFor(string alias) => $"{alias}.TenType = 1";
	protected override string DenLabel => "売上";
	protected override string KinLabel => "入金";
	protected override string PaysakiLabel => "請求先";
	protected override string ToriLabel => "得意先";

	// EndFlag は一覧の消込Flg初期値に使うので必ず読む。明細JSONは読まない（軽量化）。
	protected override string DenSelectColumns =>
		"h.Id, h.Vdc, h.Vdu, h.DenDay, h.KakeDay, h.Id_Tokui, h.VTokui, h.Id_Soko, h.VSoko, " +
		"h.Id_Shain, h.VShain, h.CalcFlag, h.Kubun, h.IsPay, h.EndFlag, h.ManualNo, h.RelateNo1, " +
		"h.KingakuTotal, h.Tax1, h.Tax2, h.Tax3, h.Total";

	protected override long GetDenToriId(Tran00Uriage den) => den.Id_Tokui;
	protected override string GetDenKakeDay(Tran00Uriage den) => den.KakeDay;
	protected override long GetDenTotal(Tran00Uriage den) => den.Total;
	protected override long GetDenTax(Tran00Uriage den) => den.Tax1 + den.Tax2 + den.Tax3;
	protected override int GetDenEndFlag(Tran00Uriage den) => den.EndFlag;
	protected override string GetDenManualNo(Tran00Uriage den) => den.ManualNo;

	// 卸売上の区分は EnumUri00。20-39 は OnKubunChanged が CalcFlag=-1 を立てるので金額はマイナスになる。
	protected override string GetDenKubunText(Tran00Uriage den) => den.EnKubun switch {
		EnumUri00.Uriage => "売上",
		EnumUri00.UriSale => "売上(ｾｰﾙ)",
		EnumUri00.Henpin => "返品",
		EnumUri00.HenSale => "返品(ｾｰﾙ)",
		EnumUri00.Nebiki => "値引",
		EnumUri00.Other => "その他",
		_ => den.Kubun.ToString(System.Globalization.CultureInfo.InvariantCulture),
	};

	/// <summary>請求先・得意先はどちらも得意先マスタ(卸先)から選ぶ。</summary>
	protected override (long Id, string Code, string Name)? PickToriMaster(long startPos) {
		var selected = Helpers.PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "TenType=1", "Code", startPos);
		return selected == null ? null : (selected.Id, selected.Code, selected.Name);
	}
}
