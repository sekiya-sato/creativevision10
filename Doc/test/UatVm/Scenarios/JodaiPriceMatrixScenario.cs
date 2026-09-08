using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// 上代一括変更画面（<see cref="MasterJouDaiBulkChangeView"/>）Step 7「③ 価格（Price Matrix、設計書5.4）」のUAT。
/// </summary>
/// <remarks>
/// Step 5（<see cref="JodaiBulkExtractScenario"/>）・Step 6（<see cref="JodaiScopeScenario"/>）が
/// 抽出条件・Scope本体（Jshop展開・競合検出・後方互換）を担保しているため、ここでは Price Matrix 固有の
/// 挙動だけを見る:
/// <list type="bullet">
/// <item>対象取得直後、セルがScope数ぶん複製され、値がJodaiPriceRule.Calculateの算出値と一致すること</item>
/// <item>Scopeの増減にMatrixの列（<see cref="JodaiMeisaiRow.Cells"/>）が追従し、既存セルの値が温存されること</item>
/// <item>セル一括操作の結果がJodaiPriceRule.Calculateと一致し、手動編集フラグが立つこと</item>
/// <item>「一括計算」は手動編集セルも含めて上書きすること（一括操作との違い）</item>
/// <item>C7（原価割れ）・C8（最低販売価格違反）の判定が<c>JodaiPriceRule.IsBelowCost</c>/
///   <c>IsBelowMinPrice</c>（<c>JodaiConflictChecker</c>と共有する述語）と一致すること。
///   背景色そのものはUatVmから見えないため、色を決めるViewModelプロパティ（IsCostViolation/IsMinPriceViolation）
///   で判定結果を確認する</item>
/// <item>Scope1件・手動編集無しのときの展開結果が現行（Scope導入前）と同一であること（回帰）</item>
/// <item>手で直したセルの値がそのまま保存され、確定後のDerivedJodai.Jodaiに反映されること
///   （Matrixで例外価格を入れられることの担保）</item>
/// <item>既存伝票（商品×Scopeのセル単位のJmeisai）を読み込むと、Matrixが商品1行×Scope数ぶんのセルへ
///   正しく復元されること</item>
/// </list>
/// 実データ件数に依存する固定値は使わず、抽出件数・原価から導出した相対値で判定する。
/// </remarks>
public static class JodaiPriceMatrixScenario {
	public static async Task RunAsync(VmSession session) {
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		var d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		if (!await d.WaitAsync("初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11)) return;

		var codeFrom = "00211161001";
		var codeTo = "00211161010";

		// ============================================================
		// 1) 対象取得直後: Cellsが Scope数(1件)ぶん複製され、値がJodaiPriceRuleの算出値と一致する
		// ============================================================
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得", vm => vm.GetMeisaiCommand);
		if (!session.Check("明細取得:1件以上", d.Vm.MeisaiRows.Count > 1, new { d.Vm.MeisaiRows.Count })) return;

		session.Check("Matrix初期化:全行がScope数(1)ぶんのCellsを持つ",
			d.Vm.MeisaiRows.All(r => r.Cells.Count == 1), new { d.Vm.ScopeRows.Count });

		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 30m;
		d.Vm.ScopeRows[0].RoundUnit = 2;
		d.Vm.ScopeRows[0].RoundType = 0;
		await d.RunAsync("一括計算(初回)", vm => vm.RecalcMatrixCommand);

		int ExpectedByScope(JodaiScopeRow scope, JodaiMeisaiRow row) => JodaiPriceRule.Calculate(
			(EnumJodaiPriceMethod)scope.PriceMethod, row.JodaiOld, scope.FixedPrice, scope.RateOff,
			scope.Amount, scope.RateOn, scope.RoundUnit, scope.RoundType, []);

		var expectedRateOff30 = d.Vm.MeisaiRows.ToDictionary(r => r.Id_Shohin, r => ExpectedByScope(d.Vm.ScopeRows[0], r));
		session.Check("一括計算:全セルがJodaiPriceRule.Calculateの算出値と一致",
			d.Vm.MeisaiRows.All(r => r.Cells[0].JodaiNew == expectedRateOff30[r.Id_Shohin]), null);

		// ============================================================
		// 2) Scopeを増やすとMatrixの列(Cells)が追従する。既存セルの値は温存され、減らすと元へ戻る
		// ============================================================
		d.Run("Scope追加", vm => vm.AddScopeRowCommand);
		session.CheckEqual("Scope追加:全行がCells2件になる", 2, d.Vm.MeisaiRows.Min(r => r.Cells.Count));
		session.Check("Scope追加:既存セル(Scope1)の値は温存される",
			d.Vm.MeisaiRows.All(r => r.Cells[0].JodaiNew == expectedRateOff30[r.Id_Shohin]), null);

		var addedScope = d.Vm.ScopeRows[^1];
		d.Run("Scope削除", vm => vm.RemoveScopeRowCommand, addedScope);
		session.CheckEqual("Scope削除:全行がCells1件に戻る", 1, d.Vm.MeisaiRows.Max(r => r.Cells.Count));

		// ============================================================
		// 3) セル一括操作: 選択セルへ方式・値を適用した結果がJodaiPriceRuleの算出値と一致し、手動編集フラグが立つ
		// ============================================================
		var targetRow = d.Vm.MeisaiRows[0];
		d.Vm.SetSelectedMatrixCells([(targetRow, d.Vm.ScopeRows[0].No)]);
		session.CheckEqual("セル選択:1件", 1, d.Vm.SelectedMatrixCellCount);

		d.Vm.BulkMethod = (int)EnumJodaiPriceMethod.Amount;
		d.Vm.BulkValueText = "1000";
		d.Run("選択セルへ一括操作(値引額1000円)", vm => vm.ApplyBulkOperationCommand);
		var expectedAmount = JodaiPriceRule.Calculate(EnumJodaiPriceMethod.Amount, targetRow.JodaiOld, 0, 0m, 1000, 0m,
			d.Vm.ScopeRows[0].RoundUnit, d.Vm.ScopeRows[0].RoundType, []);
		session.CheckEqual("セル一括操作:算出値がJodaiPriceRuleと一致", expectedAmount, targetRow.Cells[0].JodaiNew);
		session.Check("セル一括操作:手動編集フラグが立つ", targetRow.Cells[0].IsManuallyEdited, null);

		var untouchedRow = d.Vm.MeisaiRows[1];
		session.Check("セル一括操作:未選択行は影響を受けない(Scopeの値のまま)",
			untouchedRow.Cells[0].JodaiNew == expectedRateOff30[untouchedRow.Id_Shohin] && !untouchedRow.Cells[0].IsManuallyEdited, null);

		// 「一括計算」(RecalcMatrix)は明示操作なので手動編集セルも含めて全セルを上書きする（一括操作との違い）
		await d.RunAsync("一括計算(手動編集セルの上書き確認)", vm => vm.RecalcMatrixCommand);
		session.CheckEqual("一括計算:手動編集セルも上書きされる", expectedRateOff30[targetRow.Id_Shohin], targetRow.Cells[0].JodaiNew);
		session.Check("一括計算:手動編集フラグは解除される", !targetRow.Cells[0].IsManuallyEdited, null);

		// ============================================================
		// 4) C7(原価割れ)・C8(最低販売価格違反)の判定がJodaiPriceRuleの共有述語と一致する。
		//    CvWpfclient/UatVmはCvDomainLogicを参照できないため、JodaiConflictChecker.CheckBelowCost/
		//    CheckBelowMinPriceが呼ぶのと同じ関数(JodaiPriceRule.IsBelowCost/IsBelowMinPrice)との
		//    一致を確認することで、画面とサーバ側の基準が同一であることを担保する。
		// ============================================================
		var costRow = d.Vm.MeisaiRows.First(r => r.TankaGenka > 0);
		var belowCost = Math.Max(costRow.TankaGenka - 1, 0);
		d.Vm.SetSelectedMatrixCells([(costRow, d.Vm.ScopeRows[0].No)]);
		d.Vm.BulkMethod = (int)EnumJodaiPriceMethod.FixedPrice;
		d.Vm.BulkValueText = belowCost.ToString();
		d.Run("原価割れセルを作る", vm => vm.ApplyBulkOperationCommand);
		session.CheckEqual("C7:算出値=原価-1", belowCost, costRow.Cells[0].JodaiNew);
		session.CheckEqual("C7:IsCostViolationがJodaiPriceRule.IsBelowCostと一致",
			JodaiPriceRule.IsBelowCost(costRow.Cells[0].JodaiNew, costRow.TankaGenka), costRow.Cells[0].IsCostViolation);
		session.Check("C7:原価未満なのでtrue", costRow.Cells[0].IsCostViolation, new { costRow.TankaGenka, belowCost });

		// 原価以上へ戻すとfalseになることも確認する（判定が値に追従することの確認）
		d.Vm.BulkValueText = (costRow.TankaGenka + 1).ToString();
		d.Run("原価割れを解消", vm => vm.ApplyBulkOperationCommand);
		session.Check("C7:原価以上ならfalse", !costRow.Cells[0].IsCostViolation, new { costRow.TankaGenka });

		var configRows = await session.QueryAsync<MasterConfig>(
			"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiMinPrice);
		var originalMinPrice = configRows.FirstOrDefault();
		if (originalMinPrice != null) {
			var originalVal = originalMinPrice.Val;
			try {
				// JodaiMinPriceを高い値に設定し、Viewを開き直してcachedJodaiMinPriceへ反映させる（Init()時にのみ読む）
				originalMinPrice.Val = "999999";
				var savedMinPrice = await session.UpdateAsync(originalMinPrice);

				d.View.Close();
				d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
				await d.WaitAsync("再初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11);
				d.Run("新規(C8確認)", vm => vm.DoNewCommand);
				d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
				d.Vm.CondRows[0].CdFrom = codeFrom;
				d.Vm.CondRows[0].CdTo = codeTo;
				await d.RunAsync("明細取得(C8確認)", vm => vm.GetMeisaiCommand);

				var minPriceRow = d.Vm.MeisaiRows[0];
				session.CheckEqual("C8:MinSellingPriceがキャッシュ値(999999)に更新されている", 999999, minPriceRow.MinSellingPrice);
				session.CheckEqual("C8:IsMinPriceViolationがJodaiPriceRule.IsBelowMinPriceと一致(必ず違反)",
					JodaiPriceRule.IsBelowMinPrice(minPriceRow.Cells[0].JodaiNew, minPriceRow.MinSellingPrice),
					minPriceRow.Cells[0].IsMinPriceViolation);
				session.Check("C8:999999円設定なので違反=true", minPriceRow.Cells[0].IsMinPriceViolation, null);
			}
			finally {
				// 後続シナリオ・実データを汚さないよう必ず元へ戻す（既存シナリオがJodaiMaxCellsを復元するのと同じ扱い）
				var check = await session.QueryAsync<MasterConfig>(
					"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiMinPrice);
				if (check.FirstOrDefault() is { } current && current.Val != originalVal) {
					current.Val = originalVal;
					await session.UpdateAsync(current);
				}
			}

			// cachedJodaiMinPriceが999999のまま残っているため、Viewを開き直してキャッシュを元(0)へ合わせる
			d.View.Close();
			d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
			await d.WaitAsync("再初期化2:FieldOptions読込", vm => vm.FieldOptions.Count > 11);
		}
		else {
			session.Note("C8:MasterConfig(JodaiMinPrice)行が無い環境のため省略", null);
		}

		// ============================================================
		// 5) Scope1件・手動編集無しのとき、展開結果が現行（Scope導入前）と同一であること（回帰）
		// ============================================================
		d.Run("新規(回帰用)", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得(回帰用)", vm => vm.GetMeisaiCommand);
		var meisaiCount = d.Vm.MeisaiRows.Count;
		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 20m;
		d.Vm.ScopeRows[0].RoundUnit = 0;
		d.Vm.ScopeRows[0].RoundType = 0;
		var expectedRegression = d.Vm.MeisaiRows.ToDictionary(r => r.Id_Shohin, r => ExpectedByScope(d.Vm.ScopeRows[0], r));

		await d.RunAsync("対象一覧取得(回帰用)", vm => vm.LoadShopsCommand);
		if (!session.Check("対象一覧取得(回帰用):1件以上", d.Vm.ShopRows.Count > 0, new { d.Vm.ShopRows.Count })) return;
		d.Run("全てOFF(回帰用)", vm => vm.ShopAllOffCommand);
		d.Vm.ShopRows[0].IsTarget = true;
		var regressionShopId = d.Vm.ShopRows[0].Id_Tenpo;

		await d.RunAsync("登録(回帰用・セル未編集)", vm => vm.DoRegisterCommand);
		if (session.Check("登録(回帰用):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) {
			var regressionId = d.Vm.EditId;
			await d.RunAsync("確定(回帰用)", vm => vm.DoFixCommand);
			var regressionExpanded = await session.QueryAsync<DerivedJodai>(
				"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, Jodai FROM DerivedJodai WHERE Id_Tran = @0", regressionId.ToString());
			session.CheckEqual("回帰:展開行数=明細数(店舗1件)", meisaiCount, regressionExpanded.Count);
			session.Check("回帰:全行の適用上代がScopeの値下率20%計算と一致(手動編集を挟まない限り現行と同一)",
				regressionExpanded.All(x => expectedRegression.TryGetValue(x.Id_Shohin, out var expected) && x.Jodai == expected),
				new { regressionExpanded.Count });
			await d.RunAsync("取消(回帰用)", vm => vm.DoCancelDenCommand);
		}

		// ============================================================
		// 6) 手で直したセルの値がそのまま保存され、確定後のDerivedJodai.Jodaiに反映されること
		//    （Matrixで例外価格を入れられることの担保。設計書「主入力手段ではなく確認・例外編集用」）
		// ============================================================
		d.Run("新規(例外価格用)", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得(例外価格用)", vm => vm.GetMeisaiCommand);
		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 20m;

		await d.RunAsync("対象一覧取得(例外価格用)", vm => vm.LoadShopsCommand);
		d.Run("全てOFF(例外価格用)", vm => vm.ShopAllOffCommand);
		d.Vm.ShopRows[0].IsTarget = true;

		var exceptionRow = d.Vm.MeisaiRows[0];
		var normalRow = d.Vm.MeisaiRows[1];
		var exceptionPrice = Math.Max(exceptionRow.JodaiOld - 777, 1); // Scopeの値下率計算(20%OFF)とは異なる値
		d.Vm.SetSelectedMatrixCells([(exceptionRow, d.Vm.ScopeRows[0].No)]);
		d.Vm.BulkMethod = (int)EnumJodaiPriceMethod.FixedPrice;
		d.Vm.BulkValueText = exceptionPrice.ToString();
		d.Run("例外価格をセルへ手動設定", vm => vm.ApplyBulkOperationCommand);
		session.CheckEqual("例外価格:セルへ反映", exceptionPrice, exceptionRow.Cells[0].JodaiNew);
		session.Check("例外価格:手動編集フラグが立つ", exceptionRow.Cells[0].IsManuallyEdited, null);
		var expectedNormal = JodaiPriceRule.Calculate((EnumJodaiPriceMethod)d.Vm.ScopeRows[0].PriceMethod, normalRow.JodaiOld,
			d.Vm.ScopeRows[0].FixedPrice, d.Vm.ScopeRows[0].RateOff, d.Vm.ScopeRows[0].Amount, d.Vm.ScopeRows[0].RateOn,
			d.Vm.ScopeRows[0].RoundUnit, d.Vm.ScopeRows[0].RoundType, []);

		await d.RunAsync("登録(例外価格)", vm => vm.DoRegisterCommand);
		if (session.Check("登録(例外価格):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) {
			var exceptionId = d.Vm.EditId;
			await d.RunAsync("確定(例外価格)", vm => vm.DoFixCommand);
			var exceptionExpanded = await session.QueryAsync<DerivedJodai>(
				"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, Jodai FROM DerivedJodai WHERE Id_Tran = @0", exceptionId.ToString());
			var exceptionExpandedRow = exceptionExpanded.FirstOrDefault(x => x.Id_Shohin == exceptionRow.Id_Shohin);
			var normalExpandedRow = exceptionExpanded.FirstOrDefault(x => x.Id_Shohin == normalRow.Id_Shohin);
			session.Check("例外価格:展開結果に手動セルの行がある", exceptionExpandedRow != null, null);
			if (exceptionExpandedRow != null) {
				session.CheckEqual("例外価格:DerivedJodai.Jodaiが手動編集値と一致(Scope計算値ではない)", exceptionPrice, exceptionExpandedRow.Jodai);
			}
			session.Check("例外価格:未編集行の展開結果はある", normalExpandedRow != null, null);
			if (normalExpandedRow != null) {
				session.CheckEqual("例外価格:未編集行はScope計算値のまま", expectedNormal, normalExpandedRow.Jodai);
			}
			await d.RunAsync("取消(例外価格)", vm => vm.DoCancelDenCommand);
		}

		// ============================================================
		// 7) 既存伝票（商品×Scopeのセル単位のJmeisai）を読み込むと、Matrixが正しく復元されること
		// ============================================================
		var shopSample = await session.QueryAsync<MasterTokui>(
			"SELECT Id, Vdc, Vdu, Code, Name, TenType FROM MasterTokui WHERE TenType = 6 ORDER BY Code LIMIT 1");
		var shohinSample = await session.QueryAsync<MasterShohin>(
			"SELECT Id, Vdc, Vdu, Code, Name, TankaJodai, TankaGenka FROM MasterShohin ORDER BY Code LIMIT 1");
		if (shopSample.Count > 0 && shohinSample.Count > 0) {
			var shop = shopSample[0];
			var shohin = shohinSample[0];
			var scope1Price = shohin.TankaJodai > 0 ? shohin.TankaJodai - 100 : 100;
			var scope2Price = shohin.TankaJodai > 0 ? shohin.TankaJodai - 200 : 200;
			var restore = new TranJodai {
				DenDay = "20260901",
				Kubun = (int)EnumJodaiKubun.Sale,
				TaishoType = (int)EnumJodaiTaisho.Tenpo,
				Title = "UAT:PriceMatrix復元",
				DayFrom = "20260901",
				DayTo = "20260930",
				CalcType = 1,
				CalcRate = 20m,
				RoundUnit = 2,
				RoundType = 0,
				Status = 0,
				ShopCnt = 1,
				MeisaiCnt = 2,
				ScopeCnt = 2,
				Jshop = [new TranJodaiShop { Id_Tenpo = shop.Id, Code_Tenpo = shop.Code, Mei_Tenpo = shop.Name, DayFrom = "20260901", DayTo = "20260930", No_Scope = 1 }],
				Jscope = [
					new TranJodaiScope { No = 1, Name = "全店1", RangeType = (int)EnumJodaiRangeType.All, IncExc = (int)EnumJodaiIncExc.Include, DayFrom = "20260901", DayTo = "20260930", PriceMethod = (int)EnumJodaiPriceMethod.FixedPrice, FixedPrice = scope1Price },
					new TranJodaiScope { No = 2, Name = "全店2", RangeType = (int)EnumJodaiRangeType.All, IncExc = (int)EnumJodaiIncExc.Include, DayFrom = "20260901", DayTo = "20260930", PriceMethod = (int)EnumJodaiPriceMethod.FixedPrice, FixedPrice = scope2Price },
				],
				Jmeisai = [
					new TranJodaiMeisai { No = 1, Id_Shohin = shohin.Id, Code_Shohin = shohin.Code, Mei_Shohin = shohin.Name, JodaiOld = shohin.TankaJodai, JodaiNew = scope1Price, DayTento = "19010101", DayChange = "20260901", No_Scope = 1 },
					new TranJodaiMeisai { No = 1, Id_Shohin = shohin.Id, Code_Shohin = shohin.Code, Mei_Shohin = shohin.Name, JodaiOld = shohin.TankaJodai, JodaiNew = scope2Price, DayTento = "19010101", DayChange = "20260901", No_Scope = 2 },
				],
			};
			var savedRestore = await session.InsertAsync(restore);

			d.Run("検索(復元確認)", vm => vm.DoSearchCommand);
			await d.WaitAsync("検索完了(復元確認)", vm => !vm.IsBusy);
			d.Vm.SelectedListRow = d.Vm.ListRows.FirstOrDefault(r => r.Id == savedRestore.Id);
			if (session.Check("復元確認:一覧に見つかる", d.Vm.SelectedListRow != null, new { savedRestore.Id })) {
				await d.RunAsync("読込(復元確認)", vm => vm.GoToEditCommand);
				session.CheckEqual("復元確認:商品1行に集約される", 1, d.Vm.MeisaiRows.Count);
				session.CheckEqual("復元確認:Scope2件が復元される", 2, d.Vm.ScopeRows.Count);
				if (d.Vm.MeisaiRows.Count == 1) {
					var restoredRow = d.Vm.MeisaiRows[0];
					session.CheckEqual("復元確認:CellsがScope数(2)ぶん復元される", 2, restoredRow.Cells.Count);
					var cellScope1 = restoredRow.Cells.FirstOrDefault(c => c.No_Scope == 1);
					var cellScope2 = restoredRow.Cells.FirstOrDefault(c => c.No_Scope == 2);
					session.Check("復元確認:Scope1のセルが見つかる", cellScope1 != null, null);
					session.Check("復元確認:Scope2のセルが見つかる", cellScope2 != null, null);
					if (cellScope1 != null) session.CheckEqual("復元確認:Scope1のJodaiNewが一致", scope1Price, cellScope1.JodaiNew);
					if (cellScope2 != null) session.CheckEqual("復元確認:Scope2のJodaiNewが一致", scope2Price, cellScope2.JodaiNew);
					session.Check("復元確認:復元直後は手動編集扱いにしない(再保存時にScope計算へ戻せる後方互換)",
						restoredRow.Cells.All(c => !c.IsManuallyEdited), null);
				}
			}
		}
		else {
			session.Note("復元確認:MasterTokui/MasterShohinの候補が無いため省略", null);
		}

		session.SetDialogResponder(null);
	}
}
