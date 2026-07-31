using CvBase;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 移動入力(即時) — 倉庫からの出庫と移動先への入庫を同時に計上する。積送中在庫を経由しない。
/// </summary>
public partial class IdoInputSokuViewModel : Helpers.BaseIdoInputViewModel<Tran05Ido> {
	protected override string IdoDisplayName => "移動(即時)";
	protected override string FormFilePrefix => "IdoInputSoku";
}
