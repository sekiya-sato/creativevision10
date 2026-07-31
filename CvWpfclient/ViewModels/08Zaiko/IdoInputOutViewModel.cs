using CvBase;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 移動入力(積送) — 倉庫から出庫し、移動先へは「積送中(入庫予定)」として計上する。
/// 実入庫は移動受入力(<see cref="IdoInputUkeViewModel"/>)で Tran11IdoIn を登録した時点で立つ。
/// </summary>
public partial class IdoInputOutViewModel : Helpers.BaseIdoInputViewModel<Tran10IdoOut> {
	protected override string IdoDisplayName => "移動(積送)";
	protected override string FormFilePrefix => "IdoInputOut";
}
