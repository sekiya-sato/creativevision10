using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._04Juchu;

/// <summary>
/// 受注残完了設定。残っていてもこれ以上出荷しないと決めた受注をまとめて完了にする。
/// <para>
/// 受注は出荷が明細単位で全SKU充足した時点で自動的に完了になる。この画面はその例外処理で、
/// 完了(強制)と、誤って完了にしたものの解除を行う。仕様は
/// `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 4.2 / 4.3 / G4 を参照する。
/// </para>
/// <para>
/// 残の突合は出荷売上の <c>RelateNo1</c> に受注Idを入れる規約による。
/// 対象とする出荷は<b>出荷先の店種区分が卸先(1)・売仕店(3)</b>のものだけで、
/// 倉庫(0)・直営店(6)への移動は受注残を消化しない（決定 G4 / I4）。
/// </para>
/// </summary>
public partial class JuchuZanCompletionSettingViewModel : BaseZanCompletionViewModel<Tran12Jyuchu> {
	protected override string QueryTitle => "受注残完了設定";
	protected override string DenTableName => nameof(Tran12Jyuchu);
	protected override string ActualTableName => nameof(Tran00Uriage);
	protected override string DenToriIdColumn => nameof(Tran12Jyuchu.Id_Tokui);
	protected override string ToriMasterTableName => nameof(MasterTokui);
	protected override string DenDayLabel => "受注日";
	protected override string ToriLabel => "得意先";

	/// <summary>
	/// 出荷売上のうち卸先・売仕店へのものだけを残の消化として数える。
	/// 区分値はサーバー側の判定(<c>CompletionDb</c>)と同じ <see cref="TranCalcBase.ShukkaTenTypes"/> を使い、
	/// 画面とサーバーで食い違わないようにする。
	/// </summary>
	protected override string ActualExtraJoin =>
		$"INNER JOIN {nameof(MasterTokui)} at ON at.Id = a.Id_Tokui "
		+ $"AND at.TenType IN ({TranCalcBase.ShukkaTenTypes})";

	protected override (long Id, string Code, string Name)? PickToriMaster(long startPos) {
		var picked = PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "", "Code", startPos);
		return picked == null ? null : (picked.Id, picked.Code ?? string.Empty, picked.Name ?? string.Empty);
	}
}
