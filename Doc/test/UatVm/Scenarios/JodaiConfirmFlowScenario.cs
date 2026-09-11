using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// 上代一括変更画面（<see cref="MasterJouDaiBulkChangeView"/>）Step 9「承認列の入力と確定フローの統合、
/// 旧UIの撤去」の通しシナリオ（設計書第8章）。
/// </summary>
/// <remarks>
/// Step 5〜8（<see cref="JodaiBulkExtractScenario"/>・<see cref="JodaiScopeScenario"/>・
/// <see cref="JodaiPriceMatrixScenario"/>・<see cref="JodaiConfirmScenario"/>）が各タブ個別の挙動を
/// 担保しているため、ここではStep 9で変わった「確定フローの統合」（設計書2.3・5.6）だけを見る:
/// <list type="bullet">
/// <item>新規作成→抽出→Scope設定→Price Matrix→（④確認タブを事前に開かないまま）確定、を1本で通し、
///   DoFix内部で競合チェック・プレビュー算出が自動的に行われたうえでDerivedJodaiへ展開されること
///   （C1/C2自体は<c>BuildDenpyoAsync</c>が登録時から共通で禁止するため、jodaiscope/jodaiconfirmが
///   既に担保している。ここでは④確認タブを一度も開かなくてもプレビュー・競合状態が確定操作の中で
///   自動的に更新されることを見る）</item>
/// <item>MasterConfig.JodaiNeedApprove=1のときは承認者未入力では確定できず、入力すると確定でき、
///   承認日・承認者がTranJodaiへ記録されること。既定値0では承認者が無くても確定できること（設計書2.10）</item>
/// <item>取消（Status=2）でDerivedJodaiから展開行が消えること</item>
/// <item>丸めの置き換え（Step3持ち越し。<see cref="JodaiPriceRule.ApplyRound"/>）で確定後の実際の価格が
///   1円も変わらないこと（展開結果がJodaiPriceRule.Calculateの算出値と一致することで担保）</item>
/// </list>
/// テストで作った伝票・変更した設定値は必ず元へ戻す。
/// </remarks>
public static class JodaiConfirmFlowScenario {
	public static async Task RunAsync(VmSession session) {
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		var d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		if (!await d.WaitAsync("初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11)) return;

		var codeFrom = "00211161001";
		var codeTo = "00211161010";

		// ============================================================
		// 1) 通しシナリオ: 新規→抽出→Scope→Price Matrix→（④確認は開かない）→確定→DerivedJodai展開
		//    （設計書2.3データフロー・5.6「確定=競合チェック→プレビュー確認→Jshopへ Snapshot→Status=1保存」）
		// ============================================================
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得", vm => vm.GetMeisaiCommand);
		if (!session.Check("明細取得:1件以上", d.Vm.MeisaiRows.Count > 0, new { d.Vm.MeisaiRows.Count })) return;
		var meisaiCount = d.Vm.MeisaiRows.Count;

		d.Vm.ScopeRows[0].Name = "通しSALE";
		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 20m;
		d.Vm.ScopeRows[0].RoundUnit = 2;
		d.Vm.ScopeRows[0].RoundType = 0;
		await d.RunAsync("一括計算", vm => vm.RecalcMatrixCommand);

		await d.RunAsync("対象一覧取得", vm => vm.LoadShopsCommand);
		if (!session.Check("対象一覧取得:1件以上", d.Vm.ShopRows.Count > 0, new { d.Vm.ShopRows.Count })) return;
		d.Run("全てOFF", vm => vm.ShopAllOffCommand);
		var checkCount = Math.Min(2, d.Vm.ShopRows.Count);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		await d.RunAsync("登録(通し)", vm => vm.DoRegisterCommand);
		if (!session.Check("登録:成功(EditId>0)", d.Vm.EditId > 0, new { d.Vm.EditId })) return;
		var flowDenId = d.Vm.EditId;

		// ④確認タブの競合チェックを事前には実行しない（プレビューが未算出であることを確認してから確定する）
		session.Check("④確認:確定前はプレビュー未算出(競合チェック未実行のまま)", d.Vm.PreviewStyleCount == 0, new { d.Vm.PreviewStyleCount });

		await d.RunAsync("確定(競合チェック未実行のまま)", vm => vm.DoFixCommand);
		session.CheckEqual("確定:Status=1", 1, d.Vm.EditStatus);
		session.Check("確定:DoFix内部の競合チェックでプレビューが算出される", d.Vm.PreviewStyleCount > 0, new { d.Vm.PreviewStyleCount });

		var flowExpanded = await session.QueryAsync<DerivedJodai>(
			"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, DayFrom, DayTo, Jodai FROM DerivedJodai WHERE Id_Tran = @0", flowDenId.ToString());
		session.CheckEqual("展開行数:店舗数×明細数と一致", checkCount * meisaiCount, flowExpanded.Count);

		// 丸めの置き換え（Step3持ち越し。JodaiPriceRule.ApplyRound）で確定後の実際の価格が1円も変わらないこと。
		// 展開されたJodaiが、JodaiPriceRule.Calculateの算出値（率20%OFF・百円切捨）と一致することで担保する
		var expectedJodai = d.Vm.MeisaiRows.ToDictionary(m => m.Id_Shohin,
			m => JodaiPriceRule.Calculate(EnumJodaiPriceMethod.RateOff, m.JodaiOld, 0, 20m, 0, 0m, 2, 0, null));
		session.Check("丸め置換:展開結果がJodaiPriceRule.Calculateの算出値と一致(1円も変わらない)",
			flowExpanded.All(x => expectedJodai.TryGetValue(x.Id_Shohin, out var exp) && x.Jodai == exp),
			new { sample = flowExpanded.Take(3).Select(x => new { x.Id_Shohin, x.Jodai, expected = expectedJodai.GetValueOrDefault(x.Id_Shohin) }) });

		// ---- 取消でDerivedJodaiから展開行が消えること ----
		await d.RunAsync("取消(通し)", vm => vm.DoCancelDenCommand);
		session.CheckEqual("取消:Status=2", 2, d.Vm.EditStatus);
		var afterCancel = await session.QueryAsync<DerivedJodai>(
			"SELECT Id, Vdc, Vdu, Id_Tran FROM DerivedJodai WHERE Id_Tran = @0", flowDenId.ToString());
		session.CheckEqual("取消:DerivedJodaiが0件になる", 0, afterCancel.Count);

		// ============================================================
		// 2) MasterConfig.JodaiNeedApprove: 既定0では承認者不要、1のときは必須（設計書2.10・3.8）
		// ============================================================
		var approveConfigRows = await session.QueryAsync<MasterConfig>(
			"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiNeedApprove);
		var originalApproveConfig = approveConfigRows.FirstOrDefault();
		if (originalApproveConfig != null) {
			var originalVal = originalApproveConfig.Val;
			try {
				// ---- 3-a) 既定0: 承認者未入力でも確定できる ----
				originalApproveConfig.Val = "0";
				var savedConfig0 = await session.UpdateAsync(originalApproveConfig);

				d.Run("新規(承認不要用)", vm => vm.DoNewCommand);
				d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
				d.Vm.CondRows[0].CdFrom = codeFrom;
				d.Vm.CondRows[0].CdTo = codeTo;
				await d.RunAsync("明細取得(承認不要用)", vm => vm.GetMeisaiCommand);
				await d.RunAsync("対象一覧取得(承認不要用)", vm => vm.LoadShopsCommand);
				d.Run("全てOFF(承認不要用)", vm => vm.ShopAllOffCommand);
				for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;
				d.Vm.SelectedApproveShain = null;
				await d.RunAsync("登録(承認不要用)", vm => vm.DoRegisterCommand);
				if (session.Check("登録(承認不要用):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) {
					await d.RunAsync("確定(承認不要・未入力)", vm => vm.DoFixCommand);
					session.CheckEqual("承認不要(既定0):承認者未入力でも確定できる(Status=1)", 1, d.Vm.EditStatus);
					await d.RunAsync("取消(承認不要用・後片付け)", vm => vm.DoCancelDenCommand);
					session.CheckEqual("承認不要:後片付け取消後Status=2", 2, d.Vm.EditStatus);
				}

				// ---- 3-b) 1: 承認者未入力では確定できない ----
				var savedConfig1 = savedConfig0;
				savedConfig1.Val = "1";
				savedConfig1 = await session.UpdateAsync(savedConfig1);

				d.Run("新規(承認必須・未入力)", vm => vm.DoNewCommand);
				d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
				d.Vm.CondRows[0].CdFrom = codeFrom;
				d.Vm.CondRows[0].CdTo = codeTo;
				await d.RunAsync("明細取得(承認必須用)", vm => vm.GetMeisaiCommand);
				await d.RunAsync("対象一覧取得(承認必須用)", vm => vm.LoadShopsCommand);
				d.Run("全てOFF(承認必須用)", vm => vm.ShopAllOffCommand);
				for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;
				d.Vm.SelectedApproveShain = null;
				await d.RunAsync("登録(承認必須用)", vm => vm.DoRegisterCommand);
				if (session.Check("登録(承認必須用):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) {
					var needApproveDenId = d.Vm.EditId;

					await d.RunAsync("確定(承認必須・未入力・失敗するはず)", vm => vm.DoFixCommand);
					session.CheckEqual("承認必須(1):未入力では確定できない(Status=0のまま)", 0, d.Vm.EditStatus);

					// ---- 3-c) 承認者を入力すれば確定でき、TranJodaiへ承認日・承認者が記録される ----
					if (session.Check("承認必須用:ShainOptionsが1件以上ある", d.Vm.ShainOptions.Count > 0, new { d.Vm.ShainOptions.Count })) {
						d.Vm.SelectedApproveShain = d.Vm.ShainOptions.First();
						await d.RunAsync("確定(承認必須・入力後)", vm => vm.DoFixCommand);
						session.CheckEqual("承認必須(1):承認者入力後は確定できる(Status=1)", 1, d.Vm.EditStatus);
						session.Check("承認必須:ApproveDayTextが記録される", d.Vm.ApproveDayText != "未承認", new { d.Vm.ApproveDayText });

						var savedRows = await session.QueryAsync<TranJodai>(
							"SELECT Id, Vdc, Vdu, Status, ApproveDay, Id_ApproveShain FROM TranJodai WHERE Id = @0", needApproveDenId.ToString());
						if (session.Check("承認必須:DBで確認できる", savedRows.Count > 0, new { needApproveDenId })) {
							session.Check("承認必須:DBのApproveDayが空でない", !string.IsNullOrEmpty(savedRows[0].ApproveDay), new { savedRows[0].ApproveDay });
							session.CheckEqual("承認必須:DBのId_ApproveShainが選択した社員と一致",
								d.Vm.SelectedApproveShain!.Id, savedRows[0].Id_ApproveShain);
						}

						await d.RunAsync("取消(承認必須用・後片付け)", vm => vm.DoCancelDenCommand);
						session.CheckEqual("承認必須:後片付け取消後Status=2", 2, d.Vm.EditStatus);
					}
				}
			}
			finally {
				var check = await session.QueryAsync<MasterConfig>(
					"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiNeedApprove);
				if (check.FirstOrDefault() is { } current && current.Val != originalVal) {
					current.Val = originalVal;
					await session.UpdateAsync(current);
				}
			}
		}
		else {
			session.Note("承認確認:MasterConfig(JodaiNeedApprove)行が無い環境のため省略", null);
		}

		session.SetDialogResponder(null);
	}
}
