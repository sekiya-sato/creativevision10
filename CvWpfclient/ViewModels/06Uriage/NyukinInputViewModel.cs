using CommunityToolkit.Mvvm.Input;
using CvBase;

namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// 入金入力 — 得意先からの入金(Tran06Nyukin)を入力する。売掛の減算。
/// <para>
/// 支払入力(Tran07Shiharai)と構造が同一なので <see cref="Helpers.BaseKinInputViewModel{TDen}"/> を共有する。
/// 違いは取引先が `MasterTokui`（支払は `MasterShiire`）である点だけ。
/// `TranKinHeader.Id_Torisaki` のテーブルコメント「入金であればMasterTokui 支払であればMasterShiire」に従う。
/// </para>
/// <para>
/// 入金額は明細(TranKinMeisai)の金額を積み上げて `KingakuTotal` になる。
/// 金種(現金/振込/手数料など)は明細の区分 `MasterMeisho Kubun='KIN'` で表す。
/// この伝票を売掛残高へ反映するのは請求計算・締め処理(Phase 15 の月次更新)側の仕事で、
/// 本画面は伝票を作るところまでを担当する。
/// </para>
/// </summary>
public partial class NyukinInputViewModel : Helpers.BaseKinInputViewModel<Tran06Nyukin> {
	protected override string KinDisplayName => "入金";
	protected override string ToriLabel => "得意先Id";

	[RelayCommand]
	void DoSelectTokui() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "", "Code", startPos: CurrentEdit.Id_Torisaki);
		if (tokui == null) return;
		ApplyTorisaki(tokui.Id, tokui.Code, tokui.Name);
	}
}
