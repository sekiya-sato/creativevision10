using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 配分確定。配分に確定数を入れて確定し、出荷売上／移動伝票を作る（旧CV.netの「出荷指示確定」＋「出荷処理」）。
/// 処理内容は <see cref="Helpers.BaseShippingConfirmViewModel"/> にある。
/// <para>
/// 旧 配分確定(商品)／配分確定(得意先) は並び順だけが違う同じ画面だったので、並び順の切替を持つ1画面にまとめた
/// （画面統合の判断は `Doc/spec/2026-09-28_設計判断記録.md` 2.11）。
/// </para>
/// </summary>
public sealed partial class HaibunCommitViewModel : Helpers.BaseShippingConfirmViewModel {
	/// <summary>商品×色サイズを主軸にまとめて確定する並び（既定）</summary>
	public const string SortShohin = "商品順";
	/// <summary>出荷先ごとに確認して確定する並び</summary>
	public const string SortTenpo = "出荷先順";

	public IReadOnlyList<string> SortKinds { get; } = [SortShohin, SortTenpo];

	[ObservableProperty]
	public partial string SortKind { get; set; } = SortShohin;

	/// <summary>並びを変えたら、検索済みの一覧は再検索で並べ直す（確定数の入力が消えるので自動では読み直さない）</summary>
	partial void OnSortKindChanged(string value) {
		if (Rows.Count > 0) {
			Message = $"並び順を「{value}」にしました。［検索実行］で並べ直します（入力中の確定数とチェックは消えます）。";
		}
	}

	protected override string QueryTitle => "配分確定";
	protected override string SortOrderSql => SortKind == SortTenpo
		? "h.Id_Tenpo, h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id"
		: "h.Id_Shohin, h.Id_Col, h.Id_Siz, h.Id_Soko, h.Id_Tenpo, h.Id";
}
