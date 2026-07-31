using CvBase;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 仕入返品入力 — 仕入先へ返品する伝票(Tran03Shiire の減算区分)を入力する。
/// <para>
/// 【減算になる仕組み】`Tran03Shiire.Kubun` を 20-39（返品/値引）にすると
/// `OnKubunChanged` が `CalcFlag = -1` を立てる。在庫集計(SummaryDb.CreateSummaryStockSql)は
/// `Su * CalcFlag * calcFlag` で積むので、**数量はプラスで入力する**。
/// マイナス入力すると二重に符号が反転して在庫が増えてしまうので注意。
/// </para>
/// <para>
/// 画面・明細操作・印刷SQLは商品仕入入力と完全に同じなので
/// <see cref="ShiireInputViewModel"/> を継承し、区分に関わる箇所だけ上書きする。
/// </para>
/// </summary>
public partial class HenpinInputViewModel : ShiireInputViewModel {
	protected override string DenLabel => "仕入返品";
	protected override EnumShiire DefaultKubun => EnumShiire.Henpin;
	protected override string FormFilePrefix => "HenpinInput";

	// 一覧は減算区分（仕入返品20 / 値引30）だけを出す。通常仕入は商品仕入入力で扱う。
	protected override string? KubunListWhere => "Kubun >= 20 AND Kubun < 40";

	public override IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumShiire.Henpin, "仕入返品"),
		new(EnumShiire.Nebiki, "値引"),
	];
}
