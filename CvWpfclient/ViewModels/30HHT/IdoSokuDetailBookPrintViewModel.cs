using CvBase;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHT 即時移動明細書印刷（旧 SubDlg_08prn_hhtlist06、即時移動 Tran05Ido）。
/// </summary>
public sealed partial class IdoSokuDetailBookPrintViewModel : BaseIdoDetailBookPrintViewModel<Tran05Ido> {
	protected override string ReportTitle => "即時移動明細書印刷";
	protected override string TableName => nameof(Tran05Ido);
	// 「移動明細書」以外のタイトルでは qfm が出庫元バーコードを非表示にする
	protected override string BookTitle => "即時移動明細書";
	protected override string HeaderOrder => "h.DenDay, json_extract(h.VSoko,'$.Cd'), json_extract(h.VIdo,'$.Cd'), h.Id";
}
