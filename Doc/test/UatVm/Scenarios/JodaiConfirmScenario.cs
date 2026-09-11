using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// 上代一括変更画面（<see cref="MasterJouDaiBulkChangeView"/>）Step 8「④ 確認（プレビュー・競合・Timeline、
/// 設計書2.8・2.9・5.5）」のUAT。
/// </summary>
/// <remarks>
/// Step 5〜7（<see cref="JodaiBulkExtractScenario"/>・<see cref="JodaiScopeScenario"/>・
/// <see cref="JodaiPriceMatrixScenario"/>）が対象商品・Scope・Price Matrixを担保しているため、
/// ここでは④確認タブ固有の挙動だけを見る:
/// <list type="bullet">
/// <item>プレビュー集計（Style数・SKU数・店舗数・平均上代・平均値下率・展開見込行数）がDBを直接引いた
///   実データと一致すること</item>
/// <item>展開見込行数がMasterConfig.JodaiExpandWarnRowsを超えると警告フラグが立ち、
///   元の設定値に戻すと収まること</item>
/// <item>C1（伝票内・スコープ重複）があると確定ボタン（DoFixCommand）が無効になり、
///   無ければ有効であること</item>
/// <item>C4（他伝票との競合）が、別伝票を確定させた状態で検出されること</item>
/// <item>C7（原価割れ）・C8（最低販売価格違反）の件数が、DBを直接引いた実データ（原価・MasterConfig）と
///   一致すること（CvWpfclientはCvDomainLogicを参照できずJodaiConflictCheckerを直接呼べないため、
///   共有された判定関数・SQL（JodaiPriceRule/JodaiConflictSql）が正しく動くことを実データ照合で担保する）</item>
/// </list>
/// テストで作った伝票・変更した設定値は必ず元へ戻す。
/// </remarks>
public static class JodaiConfirmScenario {
	public static async Task RunAsync(VmSession session) {
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		var d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		if (!await d.WaitAsync("初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11)) return;

		var codeFrom = "00211161001";
		var codeTo = "00211161010";

		// ============================================================
		// 1) プレビュー集計がDBを直接引いた実データと一致する（設計書2.9）
		// ============================================================
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得", vm => vm.GetMeisaiCommand);
		if (!session.Check("明細取得:1件以上", d.Vm.MeisaiRows.Count > 1, new { d.Vm.MeisaiRows.Count })) return;

		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 25m;
		d.Vm.ScopeRows[0].RoundUnit = 2;
		d.Vm.ScopeRows[0].RoundType = 0;
		await d.RunAsync("一括計算", vm => vm.RecalcMatrixCommand);

		await d.RunAsync("対象一覧取得", vm => vm.LoadShopsCommand);
		if (!session.Check("対象一覧取得:1件以上", d.Vm.ShopRows.Count > 0, new { d.Vm.ShopRows.Count })) return;
		d.Run("全てOFF", vm => vm.ShopAllOffCommand);
		var checkCount = Math.Min(2, d.Vm.ShopRows.Count);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		await d.RunAsync("競合チェック(初回)", vm => vm.CheckConflictsCommand);

		var expectedStyleCount = d.Vm.MeisaiRows.Select(r => r.Id_Shohin).Distinct().Count();
		session.CheckEqual("プレビュー:対象Style数", expectedStyleCount, d.Vm.PreviewStyleCount);

		var shohinIds = d.Vm.MeisaiRows.Select(r => r.Id_Shohin).ToList();
		var skuRows = await session.QueryAsync<ScalarCountRow>(
			$"SELECT COUNT(*) AS Cnt FROM DerivedShohinColSiz WHERE Id_Shohin IN ({string.Join(",", shohinIds)})");
		session.CheckEqual("プレビュー:対象SKU数(DerivedShohinColSizの実件数と一致)", skuRows[0].Cnt, d.Vm.PreviewSkuCount);

		var expectedShopCount = d.Vm.ShopRows.Count(x => x.IsTarget);
		session.CheckEqual("プレビュー:対象店舗数", expectedShopCount, d.Vm.PreviewShopCount);

		var expectedAvgOld = (int)Math.Round(d.Vm.MeisaiRows.Average(r => (double)r.JodaiOld), MidpointRounding.AwayFromZero);
		session.CheckEqual("プレビュー:現在平均上代=avg(JodaiOld)", expectedAvgOld, d.Vm.PreviewAvgJodaiOld);

		var expectedAvgNew = (int)Math.Round(d.Vm.MeisaiRows.Average(r => (double)r.Cells[0].JodaiNew), MidpointRounding.AwayFromZero);
		session.CheckEqual("プレビュー:変更後平均上代=avg(JodaiNew)", expectedAvgNew, d.Vm.PreviewAvgJodaiNew);

		var sumOld = d.Vm.MeisaiRows.Sum(r => (long)r.JodaiOld);
		var sumNew = d.Vm.MeisaiRows.Sum(r => (long)r.Cells[0].JodaiNew);
		var expectedRateOff = sumOld > 0 ? Math.Round((1m - (decimal)sumNew / sumOld) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
		session.CheckEqual("プレビュー:平均値下率=1-sum(JodaiNew)/sum(JodaiOld)", expectedRateOff, d.Vm.PreviewAvgRateOffPercent);

		// Scope1件のみなので「店舗数×商品数」の単純積と一致する（設計書2.9「Jshop件数×Scope内商品件数の合計」）
		var expectedExpandRows = (long)expectedShopCount * d.Vm.MeisaiRows.Count;
		session.CheckEqual("プレビュー:展開見込行数=Jshop件数×Scope内商品件数", expectedExpandRows, d.Vm.PreviewExpandRows);

		// ============================================================
		// 2) 展開見込行数がMasterConfig.JodaiExpandWarnRowsを超えると警告が出る（設計書2.9）
		// ============================================================
		var expandConfigRows = await session.QueryAsync<MasterConfig>(
			"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiExpandWarnRows);
		var originalExpandConfig = expandConfigRows.FirstOrDefault();
		if (originalExpandConfig != null) {
			var originalVal = originalExpandConfig.Val;
			try {
				// 展開見込行数(expectedExpandRows)より確実に小さい値へ下げて警告を発生させる
				originalExpandConfig.Val = "1";
				await session.UpdateAsync(originalExpandConfig);
				await d.RunAsync("競合チェック(警告確認)", vm => vm.CheckConflictsCommand);
				session.Check("展開見込警告:閾値超過でtrue", d.Vm.PreviewExpandRowsWarning, new { d.Vm.PreviewExpandRows });
			}
			finally {
				var check = await session.QueryAsync<MasterConfig>(
					"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiExpandWarnRows);
				if (check.FirstOrDefault() is { } current && current.Val != originalVal) {
					current.Val = originalVal;
					await session.UpdateAsync(current);
				}
			}
			await d.RunAsync("競合チェック(閾値復元確認)", vm => vm.CheckConflictsCommand);
			session.Check("展開見込警告:閾値を戻すとfalse", !d.Vm.PreviewExpandRowsWarning, null);
		}
		else {
			session.Note("展開見込警告:MasterConfig(JodaiExpandWarnRows)行が無い環境のため省略", null);
		}

		// ============================================================
		// 3) C1（伝票内・スコープ重複）があると確定ボタンが無効、無ければ有効（設計書5.5）
		// ============================================================
		await d.RunAsync("登録(競合確認用)", vm => vm.DoRegisterCommand);
		if (!session.Check("登録:成功(EditId>0)", d.Vm.EditId > 0, new { d.Vm.EditId })) return;
		var mainDenId = d.Vm.EditId;

		await d.RunAsync("競合チェック(C1無し)", vm => vm.CheckConflictsCommand);
		session.Check("確定ボタン:C1/C2無しなら有効", d.Vm.DoFixCommand.CanExecute(null), null);
		session.Check("HasBlockingConflicts:C1/C2無しならfalse", !d.Vm.HasBlockingConflicts, null);

		// 既存Scope(全店・対象・ヘッダ期間)と全く同じ範囲・重なる期間のScopeをもう1件追加してC1を作る
		d.Run("Scope追加(C1用)", vm => vm.AddScopeRowCommand);
		await d.RunAsync("競合チェック(C1あり)", vm => vm.CheckConflictsCommand);
		session.Check("C1検出:HasBlockingConflicts=true", d.Vm.HasBlockingConflicts, null);
		session.Check("競合一覧:C1(ScopeOverlapSameRange)行が含まれる",
			d.Vm.ConflictRows.Any(r => r.Kind == EnumJodaiConflictKind.ScopeOverlapSameRange && r.Severity == EnumJodaiConflictSeverity.Error), null);
		session.Check("確定ボタン:C1ありなら無効", !d.Vm.DoFixCommand.CanExecute(null), null);

		var dupScope = d.Vm.ScopeRows[^1];
		d.Run("Scope削除(C1解消)", vm => vm.RemoveScopeRowCommand, dupScope);
		await d.RunAsync("競合チェック(C1解消後)", vm => vm.CheckConflictsCommand);
		session.Check("確定ボタン:C1解消後は再び有効", d.Vm.DoFixCommand.CanExecute(null), null);
		session.Check("HasBlockingConflicts:C1解消後はfalse", !d.Vm.HasBlockingConflicts, null);

		// ============================================================
		// 4) C4（他伝票との競合）が、別伝票を確定させた状態で検出される（設計書2.8）
		// ============================================================
		var mainShohinId = d.Vm.MeisaiRows[0].Id_Shohin;
		var mainShopId = d.Vm.ShopRows.First(x => x.IsTarget).Id_Tenpo;

		var d2 = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		await d2.WaitAsync("初期化(他伝票):FieldOptions読込", vm => vm.FieldOptions.Count > 11);
		d2.Run("新規(他伝票)", vm => vm.DoNewCommand);
		d2.Vm.CondRows[0].Field = d2.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d2.Vm.CondRows[0].CdFrom = codeFrom;
		d2.Vm.CondRows[0].CdTo = codeFrom; // 主伝票の1商品目だけに絞る（同一商品×同一店舗×期間重複を作るため）
		await d2.RunAsync("明細取得(他伝票)", vm => vm.GetMeisaiCommand);
		var otherHasTarget = d2.Vm.MeisaiRows.Any(r => r.Id_Shohin == mainShohinId);
		if (session.Check("他伝票:主伝票と同一商品を含む", otherHasTarget, new { mainShohinId })) {
			await d2.RunAsync("対象一覧取得(他伝票)", vm => vm.LoadShopsCommand);
			d2.Run("全てOFF(他伝票)", vm => vm.ShopAllOffCommand);
			var otherShopRow = d2.Vm.ShopRows.FirstOrDefault(x => x.Id_Tenpo == mainShopId);
			if (otherShopRow != null) otherShopRow.IsTarget = true;
			session.Check("他伝票:主伝票と同一店舗を選べた", otherShopRow != null, new { mainShopId });

			await d2.RunAsync("登録(他伝票)", vm => vm.DoRegisterCommand);
			if (session.Check("他伝票:登録成功", d2.Vm.EditId > 0, new { d2.Vm.EditId })) {
				var otherDenId = d2.Vm.EditId;
				await d2.RunAsync("確定(他伝票)", vm => vm.DoFixCommand);
				session.CheckEqual("他伝票:確定済み(Status=1)", 1, d2.Vm.EditStatus);

				await d.RunAsync("競合チェック(C4確認)", vm => vm.CheckConflictsCommand);
				var c4Row = d.Vm.ConflictRows.FirstOrDefault(r => r.Kind == EnumJodaiConflictKind.OtherSlipConflict);
				session.Check("C4検出:他伝票の確定済みDerivedJodaiと重複する", c4Row != null, new { c4Row?.Count });
				if (c4Row != null) session.Check("C4:件数>0", c4Row.Count > 0, new { c4Row.Count });

				await d2.RunAsync("取消(他伝票)", vm => vm.DoCancelDenCommand);
				session.CheckEqual("他伝票:取消後Status=2", 2, d2.Vm.EditStatus);

				await d.RunAsync("競合チェック(C4解消確認)", vm => vm.CheckConflictsCommand);
				session.Check("C4解消:他伝票取消後は検出されない",
					!d.Vm.ConflictRows.Any(r => r.Kind == EnumJodaiConflictKind.OtherSlipConflict), null);
			}
		}
		d2.View.Close();

		// ============================================================
		// 5) C7（原価割れ）・C8（最低販売価格違反）の件数がDBを直接引いた実データと一致する（設計書2.8）
		// ============================================================
		// 全セルへ「1円」を強制適用し、原価がある商品は必ず原価割れになるようにする
		var allCells = d.Vm.MeisaiRows.Select(r => (r, d.Vm.ScopeRows[0].No)).ToList();
		d.Vm.SetSelectedMatrixCells(allCells);
		d.Vm.BulkMethod = (int)EnumJodaiPriceMethod.FixedPrice;
		d.Vm.BulkValueText = "1";
		d.Run("全セルへ1円を適用(C7/C8確認用)", vm => vm.ApplyBulkOperationCommand);

		var minPriceRows = await session.QueryAsync<MasterConfig>(
			"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiMinPrice);
		var minPrice = minPriceRows.FirstOrDefault() is { } mp && int.TryParse(mp.Val, out var v) && v >= 0 ? v : 0;

		await d.RunAsync("競合チェック(C7/C8確認)", vm => vm.CheckConflictsCommand);

		var expectedBelowCost = d.Vm.MeisaiRows.Count(r => JodaiPriceRule.IsBelowCost(1, r.TankaGenka));
		session.CheckEqual("C7:原価割れ件数がJodaiPriceRule.IsBelowCostの実データと一致", expectedBelowCost, d.Vm.PreviewBelowCostCount);

		var expectedBelowMinPrice = minPrice > 0 ? d.Vm.MeisaiRows.Count(r => JodaiPriceRule.IsBelowMinPrice(1, minPrice)) : 0;
		session.CheckEqual("C8:最低販売価格違反件数がJodaiPriceRule.IsBelowMinPriceの実データと一致", expectedBelowMinPrice, d.Vm.PreviewBelowMinPriceCount);

		// ============================================================
		// 6) Timeline: Scope期間の区間・期間外は通常上代へ戻る区間・他伝票由来の重なりを確認する（設計書5.5）
		// ============================================================
		d.Vm.SelectedTimelineShohin = d.Vm.TimelineShohinOptions.FirstOrDefault(o => o.Id == mainShohinId);
		d.Vm.SelectedTimelineTenpo = d.Vm.TimelineTenpoOptions.FirstOrDefault(o => o.Id == mainShopId);
		if (session.Check("Timeline:商品/店舗の選択肢が見つかる", d.Vm.SelectedTimelineShohin != null && d.Vm.SelectedTimelineTenpo != null, null)) {
			await d.RunAsync("Timeline作成", vm => vm.BuildTimelineCommand);
			session.Check("Timeline:区間データが1件以上ある", d.Vm.TimelineSegments.Count > 0, new { d.Vm.TimelineSegments.Count });
			session.Check("Timeline:Scope期間内の区間が1件以上ある(通常上代へ戻る区間ではない)",
				d.Vm.TimelineSegments.Any(s => !s.IsFallback && !s.IsOtherSlip), null);
			session.Check("Timeline:期間外に通常上代へ戻る区間がある(IsFallback=true)",
				d.Vm.TimelineSegments.Any(s => s.IsFallback), null);
			session.Check("Timeline:区間は開始日の昇順に並ぶ",
				d.Vm.TimelineSegments.Zip(d.Vm.TimelineSegments.Skip(1), (a, b) => string.CompareOrdinal(a.DayFrom, b.DayFrom) <= 0).All(x => x), null);
		}

		// 後片付け: 主伝票はまだStatus=0(登録のみ)。確定→取消の経路を通して展開済みDerivedJodaiが
		// 残らない状態にする（登録のみで残すより、Fix/Cancel一連の後始末で確実に元へ戻す）。
		if (d.Vm.DoFixCommand.CanExecute(null)) {
			await d.RunAsync("確定(後片付け用)", vm => vm.DoFixCommand);
			session.CheckEqual("後片付け:主伝票確定Status=1", 1, d.Vm.EditStatus);
			await d.RunAsync("取消(主伝票)", vm => vm.DoCancelDenCommand);
			session.CheckEqual("後片付け:主伝票取消後Status=2", 2, d.Vm.EditStatus);
		}
		session.Note("後片付け:主伝票Id/他伝票Idは取消済み(展開済みDerivedJodaiは削除済み)", new { mainDenId });

		session.SetDialogResponder(null);
	}
}
