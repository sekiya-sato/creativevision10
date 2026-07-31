using CvBase;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 入金消込 — 得意先ごとに卸売上(Tran00Uriage)と入金(Tran06Nyukin)を並べ、
/// 古い入金から順に(FIFO)充当した結果として未回収残と未充当入金を表示する。
/// <para>
/// 【保存はしない】消込結果を永続化する場所がスキーマに無いため、本画面は突合までを行う。
/// 理由と保存方式の選択肢は `.omo/2026-07-31_kesikomi_design.md` に記録済み。
/// `Tran00Uriage.IsPay` は旧システムの「掛計上FLG」の移行値で回収済フラグではないため流用不可。
/// </para>
/// <para>
/// 店舗売上(Tran01Tenuri)は含めない。店頭現金売上で売掛が立たないため。
/// 期間は債権側は掛計上日(KakeDay)、入金側は計上日(DenDay)で切る。
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

	protected override string DenSelectColumns =>
		"h.Id, h.Vdc, h.Vdu, h.DenDay, h.KakeDay, h.Id_Tokui, h.VTokui, h.Id_Soko, h.VSoko, " +
		"h.Id_Shain, h.VShain, h.CalcFlag, h.Kubun, h.IsPay, h.ManualNo, h.RelateNo1, " +
		"h.KingakuTotal, h.Tax, h.Total";

	protected override long GetDenToriId(Tran00Uriage den) => den.Id_Tokui;
	protected override string GetDenKakeDay(Tran00Uriage den) => den.KakeDay;
	protected override long GetDenTotal(Tran00Uriage den) => den.Total;
	protected override long GetDenTax(Tran00Uriage den) => den.Tax;
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

	protected override string? PickToriCode() => SelectTokuiCode();
}
