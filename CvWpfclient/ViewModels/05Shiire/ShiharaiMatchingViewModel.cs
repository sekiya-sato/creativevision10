using CvBase;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払消込 — 支払先配下の仕入(Tran03Shiire)を掛計上日で一覧し、伝票単位に消込Flgを立てて
/// `Tran03Shiire.EndFlag` へ書き戻す。支払(Tran07Shiharai)は支払日で取得し、明細の区分別集計を並べて
/// 合計金額を比較できるようにする。
/// <para>
/// 消込は伝票単位の目印であり、充当金額・未充当金額・支払伝票との個別対応は保持しない。
/// 買掛残高は伝票金額ベースなので `SummaryKaiKake` の値は消込の有無で変わらない。
/// 仕様は `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.1 を参照する。
/// </para>
/// <para>
/// 期間は債務側・支払側とも掛計上日(KakeDay)で切る。
/// `Tran03Shiire.IsPay` は旧システムの「掛計上FLG」の移行値で消込状態ではないため流用しない。
/// </para>
/// </summary>
public partial class ShiharaiMatchingViewModel : Helpers.BaseMatchingViewModel<Tran03Shiire, Tran07Shiharai> {
	protected override string QueryTitle => "支払消込";
	protected override string DenTableName => nameof(Tran03Shiire);
	protected override string DenToriIdColumn => nameof(Tran03Shiire.Id_Shiire);
	protected override string KinTableName => nameof(Tran07Shiharai);
	protected override string ToriMasterTableName => nameof(MasterShiire);
	protected override string DenLabel => "仕入";
	protected override string KinLabel => "支払";
	protected override string PaysakiLabel => "支払先";
	protected override string ToriLabel => "仕入先";

	// EndFlag は一覧の消込Flg初期値に使うので必ず読む。明細JSONは読まない（軽量化）。
	protected override string DenSelectColumns =>
		"h.Id, h.Vdc, h.Vdu, h.DenDay, h.KakeDay, h.Id_Shiire, h.VShiire, h.Id_Soko, h.VSoko, " +
		"h.Id_Shain, h.VShain, h.CalcFlag, h.Kubun, h.IsPay, h.EndFlag, h.ManualNo, h.RelateNo1, " +
		"h.KingakuTotal, h.Tax1, h.Tax2, h.Tax3, h.Total";

	protected override long GetDenToriId(Tran03Shiire den) => den.Id_Shiire;
	protected override string GetDenKakeDay(Tran03Shiire den) => den.KakeDay;
	protected override long GetDenTotal(Tran03Shiire den) => den.Total;
	protected override long GetDenTax(Tran03Shiire den) => den.Tax1 + den.Tax2 + den.Tax3;
	protected override int GetDenEndFlag(Tran03Shiire den) => den.EndFlag;
	protected override string GetDenManualNo(Tran03Shiire den) => den.ManualNo;

	// 仕入の区分は EnumShiire。20-39 は CalcFlag=-1 なので金額はマイナスになる。
	protected override string GetDenKubunText(Tran03Shiire den) => den.EnKubun switch {
		EnumShiire.Shiire => "仕入",
		EnumShiire.Henpin => "仕入返品",
		EnumShiire.Nebiki => "値引",
		EnumShiire.Other => "その他",
		_ => den.Kubun.ToString(System.Globalization.CultureInfo.InvariantCulture),
	};

	/// <summary>支払先・仕入先はどちらも仕入先マスタから選ぶ。</summary>
	protected override (long Id, string Code, string Name)? PickToriMaster(long startPos) {
		var selected = Helpers.PrintPdfHelper.ShowSelectDialog<MasterShiire>(this, typeof(MasterShiire), "", "Code", startPos);
		return selected == null ? null : (selected.Id, selected.Code, selected.Name);
	}
}
