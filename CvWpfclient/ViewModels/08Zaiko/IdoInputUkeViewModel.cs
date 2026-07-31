using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 移動受入力 — 積送中在庫(Tran10IdoOut で出庫済み)を移動先へ実入庫する Tran11IdoIn を登録する。
/// <para>
/// 【出庫伝票との紐付け】`Tran11IdoIn.RelateNo1` に積送出庫伝票(Tran10IdoOut)の Id をセットする。
/// これは 移動未受リスト(<see cref="IdoUnreceivedListViewModel"/>) が未受判定に使っている前提であり、
/// テーブルコメントの「関連伝票NO」規約とも一致する。**この規約を崩すと未受リストが機能しなくなる。**
/// </para>
/// <para>
/// 在庫計算(TranCalcBase.GetCalcIdosaki)では Tran11IdoIn が移動先の在庫+1・積送中-1 を立てる。
/// 出庫元倉庫(Id_Soko)側は Tran10IdoOut の時点で減算済みなので、Tran11IdoIn の GetCalcSoko は全て0。
/// つまり本画面の Id_Soko は「どこから来たか」の記録であり、二重に在庫を減らすことはない。
/// </para>
/// </summary>
public partial class IdoInputUkeViewModel : Helpers.BaseIdoInputViewModel<Tran11IdoIn> {
	protected override string IdoDisplayName => "移動受";
	protected override string FormFilePrefix => "IdoInputUke";
	protected override string DenLabel => "移動受";

	/// <summary>
	/// 積送出庫伝票(Tran10IdoOut)を選んでヘッダ・明細を取込む。
	/// 入庫数は出庫数を初期値とし、一部入庫の場合は取込後に明細の数量を減らす運用。
	/// 選択候補は未受（Tran11IdoIn.RelateNo1 に自分のIdが登録されていない）出庫伝票に絞る。
	/// </summary>
	[RelayCommand]
	void DoImportFromIdoOut() {
		const string unreceivedWhere = "Id NOT IN (SELECT RelateNo1 FROM Tran11IdoIn WHERE RelateNo1 > 0)";
		var idoOut = ShowSelectDialog<Tran10IdoOut>(typeof(Tran10IdoOut), unreceivedWhere, "DenDay desc, Id desc");
		if (idoOut == null) return;

		if (idoOut.Jmeisai == null || idoOut.Jmeisai.Count == 0) {
			MessageEx.ShowWarningDialog($"積送出庫 No.{idoOut.Id} に明細がありません", owner: ActiveWindow);
			return;
		}

		var uke = new Tran11IdoIn {
			DenDay = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			Id_Soko = idoOut.Id_Soko,
			VSoko = idoOut.VSoko,
			Id_Ido = idoOut.Id_Ido,
			VIdo = idoOut.VIdo,
			Id_Shain = idoOut.Id_Shain,
			VShain = idoOut.VShain,
			// 出庫伝票との紐付け。移動未受リストがこの列で消し込む。
			RelateNo1 = (int)idoOut.Id,
			ManualNo = idoOut.ManualNo,
			Memo = idoOut.Memo,
			Jmeisai = [.. idoOut.Jmeisai.Select(Common.CloneObject)],
		};

		Current = uke;
		SelectedTabIndex = 1;
		Message = $"積送出庫 No.{idoOut.Id} から {uke.Jmeisai.Count} 明細を取込みました";
	}
}
