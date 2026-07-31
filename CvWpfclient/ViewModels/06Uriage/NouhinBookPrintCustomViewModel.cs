namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 納品書印刷（専用伝票）。抽出条件・SQL・発行済み更新は納品書印刷と全く同じで、
/// 使用する印刷フォームだけが異なる（プレプリント伝票へ位置合わせして印字するため、
/// 罫線と見出しを持たない qfm を使う）。
///
/// SQL を二重に持つと片方だけ直して食い違うので、NouhinBookPrintViewModel を継承して
/// FormFileName のみ差し替える。
/// </summary>
public partial class NouhinBookPrintCustomViewModel : NouhinBookPrintViewModel {
	protected override string ReportTitle => "納品書印刷(専用伝票)";
	protected override string FormFileName => "NouhinBookPrintCustom.qfm";
}
