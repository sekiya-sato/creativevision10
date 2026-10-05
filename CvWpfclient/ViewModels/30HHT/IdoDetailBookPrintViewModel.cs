using CvBase;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHT 移動明細書印刷（旧 SubDlg_08prn_hhtlist05、積送移動 Tran10IdoOut）。
/// </summary>
public sealed partial class IdoDetailBookPrintViewModel : BaseIdoDetailBookPrintViewModel<Tran10IdoOut> {
	protected override string ReportTitle => "移動明細書印刷";
	protected override string TableName => nameof(Tran10IdoOut);
	// qfm のスクリプトがこの文字列で入庫先バーコードを非表示にする
	protected override string BookTitle => "移動明細書";
	protected override string HeaderOrder => "h.Id";
}
