using CvBase;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払消込 — 仕入先ごとに仕入(Tran03Shiire)と支払(Tran07Shiharai)を並べ、
/// 古い支払から順に(FIFO)充当した結果として未払残と未充当支払を表示する。
/// <para>
/// 【保存はしない】消込結果を永続化する場所がスキーマに無いため、本画面は突合までを行う。
/// 理由と保存方式の選択肢は `.omo/2026-07-31_kesikomi_design.md` に記録済み。
/// `Tran03Shiire.IsPay` は旧システムの「掛計上FLG」の移行値で支払済フラグではないため流用不可。
/// </para>
/// <para>期間は債務側は掛計上日(KakeDay)、支払側は計上日(DenDay)で切る。</para>
/// </summary>
public partial class ShiharaiMatchingViewModel : Helpers.BaseMatchingViewModel<Tran03Shiire, Tran07Shiharai> {
	protected override string QueryTitle => "支払消込";
	protected override string DenTableName => nameof(Tran03Shiire);
	protected override string DenToriIdColumn => nameof(Tran03Shiire.Id_Shiire);
	protected override string KinTableName => nameof(Tran07Shiharai);
	protected override string ToriMasterTableName => nameof(MasterShiire);
	protected override string DenLabel => "仕入";
	protected override string KinLabel => "支払";

	protected override string DenSelectColumns =>
		"h.Id, h.Vdc, h.Vdu, h.DenDay, h.KakeDay, h.Id_Shiire, h.VShiire, h.Id_Soko, h.VSoko, " +
		"h.Id_Shain, h.VShain, h.CalcFlag, h.Kubun, h.IsPay, h.ManualNo, h.RelateNo1, " +
		"h.KingakuTotal, h.Tax, h.Total";

	protected override long GetDenToriId(Tran03Shiire den) => den.Id_Shiire;
	protected override string GetDenKakeDay(Tran03Shiire den) => den.KakeDay;
	protected override long GetDenTotal(Tran03Shiire den) => den.Total;
	protected override long GetDenTax(Tran03Shiire den) => den.Tax;
	protected override string GetDenManualNo(Tran03Shiire den) => den.ManualNo;

	// 仕入の区分は EnumShiire。20-39 は CalcFlag=-1 なので金額はマイナスになる。
	protected override string GetDenKubunText(Tran03Shiire den) => den.EnKubun switch {
		EnumShiire.Shiire => "仕入",
		EnumShiire.Henpin => "仕入返品",
		EnumShiire.Nebiki => "値引",
		EnumShiire.Other => "その他",
		_ => den.Kubun.ToString(System.Globalization.CultureInfo.InvariantCulture),
	};

	protected override string? PickToriCode() => SelectCode<MasterShiire>("");
}
