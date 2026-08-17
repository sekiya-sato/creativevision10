using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._03Hatchu;

/// <summary>
/// 発注残完了設定。残っていてもこれ以上入荷しないと決めた発注をまとめて完了にする。
/// <para>
/// 発注は仕入が明細単位で全SKU充足した時点で自動的に完了になる。この画面はその例外処理で、
/// 完了(強制)と、誤って完了にしたものの解除を行う。仕様は
/// `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 4.2 / 4.3 を参照する。
/// </para>
/// <para>
/// 残の突合は仕入の <c>RelateNo1</c> に発注Idを入れる規約による（旧CV.netの「関連伝票NO1」と同じ）。
/// 仕入返品は <c>CalcFlag=-1</c> で符号付きに数えるため、返品を入れると残が戻る。
/// </para>
/// </summary>
public partial class HachuZanCompletionSettingViewModel : BaseZanCompletionViewModel<Tran13Hachu> {
	protected override string QueryTitle => "発注残完了設定";
	protected override string DenTableName => nameof(Tran13Hachu);
	protected override string ActualTableName => nameof(Tran03Shiire);
	protected override string DenToriIdColumn => nameof(Tran13Hachu.Id_Shiire);
	protected override string ToriMasterTableName => nameof(MasterShiire);
	protected override string DenDayLabel => "発注日";
	protected override string ToriLabel => "仕入先";

	protected override (long Id, string Code, string Name)? PickToriMaster(long startPos) {
		var picked = PrintPdfHelper.ShowSelectDialog<MasterShiire>(this, typeof(MasterShiire), "", "Code", startPos);
		return picked == null ? null : (picked.Id, picked.Code ?? string.Empty, picked.Name ?? string.Empty);
	}
}
