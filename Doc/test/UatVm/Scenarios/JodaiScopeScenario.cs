using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// 上代一括変更画面（<see cref="MasterJouDaiBulkChangeView"/>）Step 6「② 適用範囲（Scope）」のUAT。
/// </summary>
/// <remarks>
/// Step 5（<see cref="JodaiBulkExtractScenario"/>）が「① 対象商品」の抽出条件を担保しているため、
/// ここでは Scope（適用範囲）・Jshop/Jmeisai の No_Scope 展開・後方互換（Scope1件＝現行と同一）・
/// 段階値下げ・価格グループScope・競合検出・JodaiMaxCells上限・既存伝票の読込補完を担保する。
/// 実データ件数に依存する固定値は使わず、抽出件数・チェック店舗数から導出した相対値で判定する
/// （本番相当DBの内容に依存させないため）。
/// </remarks>
public static class JodaiScopeScenario {
	public static async Task RunAsync(VmSession session) {
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		var d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		if (!await d.WaitAsync("初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11)) return;

		// ============================================================
		// 1) 新規伝票はScope1件（全店/対象/ヘッダ既定期間・価格ルール）で始まる
		// ============================================================
		d.Run("新規", vm => vm.DoNewCommand);
		session.CheckEqual("初期状態:Scope1件", 1, d.Vm.ScopeRows.Count);
		session.CheckEqual("初期状態:全店Scope", (int)EnumJodaiRangeType.All, d.Vm.ScopeRows[0].RangeType);
		session.CheckEqual("初期状態:対象Scope", (int)EnumJodaiIncExc.Include, d.Vm.ScopeRows[0].IncExc);

		// 対象商品を少数に絞る（商品CDの範囲指定。既存の検索項目・回帰確認と同じ方式）
		var codeFrom = "00211161001";
		var codeTo = "00211161010";
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得", vm => vm.GetMeisaiCommand);
		if (!session.Check("明細取得:1件以上", d.Vm.MeisaiRows.Count > 0, new { d.Vm.MeisaiRows.Count })) return;
		var meisaiCount = d.Vm.MeisaiRows.Count;

		await d.RunAsync("対象一覧取得", vm => vm.LoadShopsCommand);
		if (!session.Check("対象一覧取得:1件以上", d.Vm.ShopRows.Count > 0, new { d.Vm.ShopRows.Count })) return;
		d.Run("全てOFF", vm => vm.ShopAllOffCommand);
		// チェック件数を小さく固定する（3件、または全体がそれ未満ならその件数）
		var checkCount = Math.Min(3, d.Vm.ShopRows.Count);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;
		session.CheckEqual("対象店舗:チェック件数", checkCount, d.Vm.ShopRows.Count(r => r.IsTarget));

		await d.RunAsync("登録", vm => vm.DoRegisterCommand);
		if (!session.Check("登録:成功(EditId>0)", d.Vm.EditId > 0, new { d.Vm.EditId })) return;
		var singleScopeId = d.Vm.EditId;

		await d.RunAsync("確定", vm => vm.DoFixCommand);
		session.CheckEqual("確定:状態1", 1, d.Vm.EditStatus);

		var expanded = await session.QueryAsync<DerivedJodai>(
			"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, DayFrom, DayTo FROM DerivedJodai WHERE Id_Tran = @0", singleScopeId.ToString());
		// Scope1件（全店・対象）なので、展開結果は「チェック店舗数×明細数」の直積と完全に一致する（後方互換。設計3.10）
		session.CheckEqual("展開行数:Scope1件は現行と同一(店舗数×明細数)", checkCount * meisaiCount, expanded.Count);
		session.CheckEqual("展開行数:店舗の異なり数", checkCount, expanded.Select(x => x.Id_Tenpo).Distinct().Count());
		session.CheckEqual("展開行数:商品の異なり数", meisaiCount, expanded.Select(x => x.Id_Shohin).Distinct().Count());

		// 取消して後続テストの外乱にしない
		await d.RunAsync("取消", vm => vm.DoCancelDenCommand);

		// ============================================================
		// 2) Scope追加・削除、「段を追加」で期間がずれた行が複製されること
		// ============================================================
		d.Run("新規2", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得2", vm => vm.GetMeisaiCommand);
		await d.RunAsync("対象一覧取得2", vm => vm.LoadShopsCommand);
		d.Run("全てOFF2", vm => vm.ShopAllOffCommand);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		d.Vm.ScopeRows[0].DayFrom = "20260910";
		d.Vm.ScopeRows[0].DayTo = "20260920"; // 11日間
		d.Vm.SelectedScopeRow = d.Vm.ScopeRows[0];
		d.Run("Scope追加", vm => vm.AddScopeRowCommand);
		session.CheckEqual("Scope追加:2件になる", 2, d.Vm.ScopeRows.Count);

		d.Vm.SelectedScopeRow = d.Vm.ScopeRows[0];
		d.Run("段を追加", vm => vm.AddScopeStageCommand);
		session.CheckEqual("段を追加:3件になる", 3, d.Vm.ScopeRows.Count);
		var stage = d.Vm.ScopeRows[^1];
		session.CheckEqual("段を追加:開始日=元のDayToの翌日", "20260921", stage.DayFrom);
		session.CheckEqual("段を追加:同じ日数(11日)", "20261001", stage.DayTo);

		d.Run("Scope削除", vm => vm.RemoveScopeRowCommand, stage);
		session.CheckEqual("Scope削除:2件に戻る", 2, d.Vm.ScopeRows.Count);

		// ============================================================
		// 3) 段階値下げ（同一範囲・期間が重ならないScope3段）→ DerivedJodaiに3期間分の行
		// ============================================================
		d.Run("新規3", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得3", vm => vm.GetMeisaiCommand);
		await d.RunAsync("対象一覧取得3", vm => vm.LoadShopsCommand);
		d.Run("全てOFF3", vm => vm.ShopAllOffCommand);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		d.Vm.ScopeRows[0].Name = "SALE1";
		d.Vm.ScopeRows[0].DayFrom = "20260910";
		d.Vm.ScopeRows[0].DayTo = "20260920";
		d.Vm.ScopeRows[0].PriceMethod = (int)EnumJodaiPriceMethod.RateOff;
		d.Vm.ScopeRows[0].RateOff = 30m;
		d.Vm.SelectedScopeRow = d.Vm.ScopeRows[0];
		d.Run("段を追加(2段目)", vm => vm.AddScopeStageCommand);
		d.Vm.ScopeRows[^1].Name = "SALE2";
		d.Vm.ScopeRows[^1].RateOff = 40m;
		d.Vm.SelectedScopeRow = d.Vm.ScopeRows[^1];
		d.Run("段を追加(3段目)", vm => vm.AddScopeStageCommand);
		d.Vm.ScopeRows[^1].Name = "SALE3";
		d.Vm.ScopeRows[^1].RateOff = 50m;
		session.CheckEqual("段階値下げ:Scope3件", 3, d.Vm.ScopeRows.Count);

		await d.RunAsync("登録(段階値下げ)", vm => vm.DoRegisterCommand);
		if (!session.Check("登録(段階値下げ):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) return;
		var ladderId = d.Vm.EditId;
		await d.RunAsync("確定(段階値下げ)", vm => vm.DoFixCommand);

		var ladderExpanded = await session.QueryAsync<DerivedJodai>(
			"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, DayFrom, DayTo, Jodai FROM DerivedJodai WHERE Id_Tran = @0", ladderId.ToString());
		// 同一店舗×同一商品で3期間(3段)ぶんの行になる（設計2.6）
		session.CheckEqual("段階値下げ:展開行数(店舗数×明細数×3段)", checkCount * meisaiCount * 3, ladderExpanded.Count);
		session.CheckEqual("段階値下げ:期間の異なり数=3", 3, ladderExpanded.Select(x => x.DayFrom).Distinct().Count());
		await d.RunAsync("取消(段階値下げ)", vm => vm.DoCancelDenCommand);

		// ============================================================
		// 4) 「解決結果を確認」: 複数Scopeに該当した店舗が競合として提示されること（C5 情報）
		// ============================================================
		d.Run("新規4", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得4", vm => vm.GetMeisaiCommand);
		await d.RunAsync("対象一覧取得4", vm => vm.LoadShopsCommand);
		d.Run("全てOFF4", vm => vm.ShopAllOffCommand);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		// 全店Scope(既定) + 個別店舗Scope(先頭のチェック店舗を指名) を同一期間で重ねる → C5
		var firstChecked = d.Vm.ShopRows.First(r => r.IsTarget);
		d.Run("Scope追加(個別店舗用)", vm => vm.AddScopeRowCommand);
		var storeScope = d.Vm.ScopeRows[^1];
		storeScope.RangeType = (int)EnumJodaiRangeType.Store;
		storeScope.SelectedGroupOrStoreOption = d.Vm.ScopeStoreOptions.First(o => o.Id == firstChecked.Id_Tenpo);
		session.CheckEqual("個別店舗Scope:Id_Tenpo設定", firstChecked.Id_Tenpo, storeScope.Id_Tenpo);

		await d.RunAsync("解決結果を確認", vm => vm.ResolveScopeCommand);
		// 全店Scope(既定)と個別店舗Scope(同一店舗・同一期間)が重なるため、C5(情報)が最低1件検出される
		session.Check("解決結果:競合(C5)を検出", d.Vm.Message.Contains("競合", StringComparison.Ordinal)
			&& !d.Vm.Message.Contains("競合 0 件", StringComparison.Ordinal), new { d.Vm.Message });

		// ============================================================
		// 5) C1/C2エラー競合があるときは登録が中止されること
		// ============================================================
		d.Run("新規5", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = codeFrom;
		d.Vm.CondRows[0].CdTo = codeTo;
		await d.RunAsync("明細取得5", vm => vm.GetMeisaiCommand);
		await d.RunAsync("対象一覧取得5", vm => vm.LoadShopsCommand);
		d.Run("全てOFF5", vm => vm.ShopAllOffCommand);
		for (var i = 0; i < checkCount; i++) d.Vm.ShopRows[i].IsTarget = true;

		// 全店Scopeを同一期間でもう1件追加 → 同一範囲(全店)で期間重複 = C2（エラー、確定不可）
		d.Run("Scope追加(重複用)", vm => vm.AddScopeRowCommand);
		var beforeId = d.Vm.EditId;
		await d.RunAsync("登録(C2競合・失敗するはず)", vm => vm.DoRegisterCommand);
		session.CheckEqual("C1/C2競合:登録されない(EditId変化なし)", beforeId, d.Vm.EditId);

		// ============================================================
		// 6) JodaiMaxCellsを超える組み合わせで中止されること
		// ============================================================
		var configRows = await session.QueryAsync<MasterConfig>(
			"SELECT Id, Vdc, Vdu, Category, Name, Val, Example, Memo FROM MasterConfig WHERE Name = @0", MasterConfig.NameJodaiMaxCells);
		var originalConfig = configRows.FirstOrDefault();
		if (originalConfig != null) {
			var originalVal = originalConfig.Val;
			originalConfig.Val = "1";
			var savedConfig = await session.UpdateAsync(originalConfig);

			// cachedJodaiMaxCellsはInit()時に読み込まれるので、新しいViewを開き直して反映させる
			d.View.Close();
			var d2 = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
			await d2.WaitAsync("再初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11);
			d2.Run("新規6", vm => vm.DoNewCommand);
			d2.Vm.CondRows[0].Field = d2.Vm.FieldOptions.First(f => f.Column == "M.Code");
			d2.Vm.CondRows[0].CdFrom = codeFrom;
			d2.Vm.CondRows[0].CdTo = codeTo;
			await d2.RunAsync("明細取得6(上限超過のはず)", vm => vm.GetMeisaiCommand);
			session.Check("JodaiMaxCells超過:明細が取り込まれない", d2.Vm.MeisaiRows.Count == 0, new { d2.Vm.MeisaiRows.Count, meisaiCount });

			// 後続への影響を避けるため元の値へ戻す
			savedConfig.Val = originalVal;
			await session.UpdateAsync(savedConfig);
			d = d2;
		}
		else {
			session.Note("JodaiMaxCells:MasterConfig行が無い環境のため上限テストを省略", null);
		}

		// ============================================================
		// 7) 既存伝票（Jscopeが空）を読み込むと全店Scopeが1件補われること
		// ============================================================
		var shopSample = await session.QueryAsync<MasterTokui>(
			"SELECT Id, Vdc, Vdu, Code, Name, TenType FROM MasterTokui WHERE TenType = 6 ORDER BY Code LIMIT 1");
		var shohinSample = await session.QueryAsync<MasterShohin>(
			"SELECT Id, Vdc, Vdu, Code, Name, TankaJodai, TankaGenka FROM MasterShohin ORDER BY Code LIMIT 1");
		if (shopSample.Count > 0 && shohinSample.Count > 0) {
			var shop = shopSample[0];
			var shohin = shohinSample[0];
			var legacy = new TranJodai {
				DenDay = "20260901",
				Kubun = (int)EnumJodaiKubun.Sale,
				TaishoType = (int)EnumJodaiTaisho.Tenpo,
				Title = "UAT:Scope導入前互換",
				DayFrom = "20260901",
				DayTo = "20260930",
				CalcType = 1,
				CalcRate = 20m,
				RoundUnit = 2,
				RoundType = 0,
				Status = 0,
				ShopCnt = 1,
				MeisaiCnt = 1,
				ScopeCnt = 0,
				Jshop = [new TranJodaiShop { Id_Tenpo = shop.Id, Code_Tenpo = shop.Code, Mei_Tenpo = shop.Name, DayFrom = "20260901", DayTo = "20260930" }],
				Jmeisai = [new TranJodaiMeisai { No = 1, Id_Shohin = shohin.Id, Code_Shohin = shohin.Code, Mei_Shohin = shohin.Name, JodaiOld = shohin.TankaJodai, JodaiNew = shohin.TankaJodai, DayTento = "19010101", DayChange = "20260901" }],
				Jscope = [],
			};
			var savedLegacy = await session.InsertAsync(legacy);
			session.Check("既存伝票投入:Jscope空", savedLegacy.Jscope.Count == 0, new { savedLegacy.Jscope.Count });

			d.Run("検索(既存伝票)", vm => vm.DoSearchCommand);
			await d.WaitAsync("検索完了", vm => !vm.IsBusy);
			d.Vm.SelectedListRow = d.Vm.ListRows.FirstOrDefault(r => r.Id == savedLegacy.Id);
			if (session.Check("既存伝票:一覧に見つかる", d.Vm.SelectedListRow != null, new { savedLegacy.Id })) {
				await d.RunAsync("読込(既存伝票)", vm => vm.GoToEditCommand);
				session.CheckEqual("既存伝票読込:全店Scope1件が補われる", 1, d.Vm.ScopeRows.Count);
				if (d.Vm.ScopeRows.Count == 1) {
					session.CheckEqual("既存伝票読込:RangeType=全店", (int)EnumJodaiRangeType.All, d.Vm.ScopeRows[0].RangeType);
					session.CheckEqual("既存伝票読込:価格方式=値下率(CalcType=1)", (int)EnumJodaiPriceMethod.RateOff, d.Vm.ScopeRows[0].PriceMethod);
					session.CheckEqual("既存伝票読込:DBは書き換わらない(ScopeCnt=0のまま)", 0, savedLegacy.ScopeCnt);
				}
			}
		}
		else {
			session.Note("既存伝票互換テスト:MasterTokui/MasterShohinの候補が無いため省略", null);
		}

		// 6)でJodaiMaxCellsを1へ下げた際に開き直したViewが持つcachedJodaiMaxCellsが1のまま残っている
		// （DB側は6)の最後で30000へ復元済みだが、キャッシュはInit()時にしか読み直さない）。
		// このまま8)で[明細取得]すると誤って上限超過扱いになるため、Viewを開き直してキャッシュを合わせる。
		d.View.Close();
		d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();
		await d.WaitAsync("再初期化8:FieldOptions読込", vm => vm.FieldOptions.Count > 11);

		// ============================================================
		// 8) 価格グループScope(RangeType=1)の配線確認: 画面(ViewModel)→JodaiScopeResolver.Resolveへ
		//    渡す店舗一覧に、Step 1aでMasterTokuiへ追加したId_PriceGroup/Id_PriceArea/Id_PriceChannelが
		//    正しく載っているかを確かめる。JodaiScopeResolverTests（単体）は3軸の絞り込みロジック自体を
		//    網羅済みのため、ここでは「配線」だけを見る（軸の値を持たない店舗を巻き込んでいないか、
		//    GroupAxisの切替が実際に別の列を見に行っているか）。
		//    実店舗のId_PriceGroup/Id_PriceAreaを一時的に書き換えるため、検証後に必ず元へ戻す。
		// ============================================================
		var pgStores = await session.QueryAsync<MasterTokui>(
			"SELECT Id, Vdc, Vdu, Code, Name, TenType, Id_PriceGroup, Id_PriceArea, Id_PriceChannel FROM MasterTokui WHERE TenType = 6 ORDER BY Code LIMIT 4");
		if (session.Check("価格グループ配線:店舗4件以上", pgStores.Count >= 4, new { pgStores.Count })) {
			var storeGroup1 = pgStores[0];
			var storeGroup2 = pgStores[1];
			var storeArea1 = pgStores[2];
			var storeOut = pgStores[3];
			var origGroup1 = storeGroup1.Id_PriceGroup;
			var origGroup2 = storeGroup2.Id_PriceGroup;
			var origArea1 = storeArea1.Id_PriceArea;

			// MasterMeisho(C30=価格グループ区分, C31=地域区分)へグループ実体を1件ずつ登録する。
			// 削除APIがVmSessionに無いため、再実行に備えて既存があれば使い回す（無ければ新規登録）。
			var existingGroup = await session.QueryAsync<MasterMeisho>(
				"SELECT Id, Vdc, Vdu, Kubun, KubunName, Code, Name, Ryaku, Kana, Odr FROM MasterMeisho WHERE Kubun = @0 AND Code = @1", MasterMeisho.KubunPriceGroup, "UATPG01");
			var priceGroup = existingGroup.FirstOrDefault()
				?? await session.InsertAsync(new MasterMeisho { Kubun = MasterMeisho.KubunPriceGroup, Code = "UATPG01", Name = "UAT価格グループ" });
			var existingArea = await session.QueryAsync<MasterMeisho>(
				"SELECT Id, Vdc, Vdu, Kubun, KubunName, Code, Name, Ryaku, Kana, Odr FROM MasterMeisho WHERE Kubun = @0 AND Code = @1", MasterMeisho.KubunPriceArea, "UATPA01");
			var priceArea = existingArea.FirstOrDefault()
				?? await session.InsertAsync(new MasterMeisho { Kubun = MasterMeisho.KubunPriceArea, Code = "UATPA01", Name = "UAT地域" });

			storeGroup1.Id_PriceGroup = priceGroup.Id;
			var savedStoreGroup1 = await session.UpdateAsync(storeGroup1);
			storeGroup2.Id_PriceGroup = priceGroup.Id;
			var savedStoreGroup2 = await session.UpdateAsync(storeGroup2);
			storeArea1.Id_PriceArea = priceArea.Id;
			var savedStoreArea1 = await session.UpdateAsync(storeArea1);

			try {
				d.Run("新規8", vm => vm.DoNewCommand);
				d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
				d.Vm.CondRows[0].CdFrom = codeFrom;
				d.Vm.CondRows[0].CdTo = codeTo;
				await d.RunAsync("明細取得8", vm => vm.GetMeisaiCommand);
				await d.RunAsync("対象一覧取得8", vm => vm.LoadShopsCommand);
				d.Run("全てOFF8", vm => vm.ShopAllOffCommand);
				foreach (var id in new[] { storeGroup1.Id, storeGroup2.Id, storeArea1.Id, storeOut.Id }) {
					var row = d.Vm.ShopRows.FirstOrDefault(r => r.Id_Tenpo == id);
					if (row != null) row.IsTarget = true;
				}
				session.CheckEqual("価格グループ配線:対象4店舗", 4, d.Vm.ShopRows.Count(r => r.IsTarget));

				// Scope1(全店・既定)の値下率をまず決めておく（後でDerivedJodaiの価格差で配線を確認するため）
				d.Vm.ScopeRows[0].RateOff = 10m;

				// Scope2: 価格グループ軸(GroupAxis=0)でpriceGroupを指定
				d.Run("Scope追加(価格グループ軸)", vm => vm.AddScopeRowCommand);
				var pgScope = d.Vm.ScopeRows[^1];
				pgScope.Name = "PG";
				pgScope.RangeType = (int)EnumJodaiRangeType.PriceGroup;
				pgScope.GroupAxis = (int)EnumJodaiGroupAxis.PriceGroup;
				pgScope.Id_Group = priceGroup.Id;
				pgScope.RateOff = 30m;

				// Scope3: 地域軸(GroupAxis=1)でpriceAreaを指定（残り2軸のうち1つで配線を確認）
				d.Run("Scope追加(地域軸)", vm => vm.AddScopeRowCommand);
				var paScope = d.Vm.ScopeRows[^1];
				paScope.Name = "PA";
				paScope.RangeType = (int)EnumJodaiRangeType.PriceGroup;
				paScope.GroupAxis = (int)EnumJodaiGroupAxis.PriceArea;
				paScope.Id_Group = priceArea.Id;
				paScope.RateOff = 50m;

				session.ClearDialogs();
				await d.RunAsync("解決結果を確認(価格グループ配線)", vm => vm.ResolveScopeCommand);
				var resolveDialog = session.Dialogs.LastOrDefault();
				if (session.Check("価格グループ配線:解決ダイアログが出る", resolveDialog != null, null)) {
					var body = resolveDialog!.Request.Message;
					session.Check("価格グループ配線:PGスコープへグループ店舗2件のみ該当",
						body.Contains("「PG」: 2 店舗", StringComparison.Ordinal), new { body });
					session.Check("価格グループ配線:PAスコープへ地域店舗1件のみ該当",
						body.Contains("「PA」: 1 店舗", StringComparison.Ordinal), new { body });
					session.Check("価格グループ配線:軸を持たない店舗は全店Scopeへ",
						body.Contains("「全店」: 1 店舗", StringComparison.Ordinal), new { body });
				}

				await d.RunAsync("登録(価格グループ配線)", vm => vm.DoRegisterCommand);
				if (session.Check("登録(価格グループ配線):成功", d.Vm.EditId > 0, new { d.Vm.EditId })) {
					var pgTranId = d.Vm.EditId;
					await d.RunAsync("確定(価格グループ配線)", vm => vm.DoFixCommand);

					var pgExpanded = await session.QueryAsync<DerivedJodai>(
						"SELECT Id, Vdc, Vdu, Id_Tran, Id_Tenpo, Id_Shohin, DayFrom, DayTo, Jodai FROM DerivedJodai WHERE Id_Tran = @0", pgTranId.ToString());
					var jodaiByStore = pgExpanded.GroupBy(x => x.Id_Tenpo).ToDictionary(g => g.Key, g => g.First().Jodai);
					var hasAll4 = new[] { storeGroup1.Id, storeGroup2.Id, storeArea1.Id, storeOut.Id }.All(jodaiByStore.ContainsKey);
					if (session.Check("展開結果:対象4店舗すべてに行がある", hasAll4, new { jodaiByStore.Keys })) {
						session.CheckEqual("展開結果:価格グループ店舗2件は同じ価格(PGスコープが効いている)",
							jodaiByStore[storeGroup1.Id], jodaiByStore[storeGroup2.Id]);
						session.Check("展開結果:価格グループ店舗は全店Scopeの店舗と価格が異なる(値下率10%→30%)",
							jodaiByStore[storeGroup1.Id] != jodaiByStore[storeOut.Id],
							new { group = jodaiByStore[storeGroup1.Id], all = jodaiByStore[storeOut.Id] });
						session.Check("展開結果:地域店舗はPGスコープ・全店Scopeのどちらとも価格が異なる(値下率50%)",
							jodaiByStore[storeArea1.Id] != jodaiByStore[storeGroup1.Id] && jodaiByStore[storeArea1.Id] != jodaiByStore[storeOut.Id],
							new { area = jodaiByStore[storeArea1.Id], group = jodaiByStore[storeGroup1.Id], all = jodaiByStore[storeOut.Id] });
					}
				}

				// 取消して後続への外乱にしない（設計2.6の登録済み伝票と同じ扱い。1)・3)と同じ手法）
				await d.RunAsync("取消(価格グループ配線)", vm => vm.DoCancelDenCommand);
			}
			finally {
				// 実店舗のId_PriceGroup/Id_PriceAreaを元へ戻す（他のシナリオ・実データを汚さない）
				savedStoreGroup1.Id_PriceGroup = origGroup1;
				await session.UpdateAsync(savedStoreGroup1);
				savedStoreGroup2.Id_PriceGroup = origGroup2;
				await session.UpdateAsync(savedStoreGroup2);
				savedStoreArea1.Id_PriceArea = origArea1;
				await session.UpdateAsync(savedStoreArea1);
			}
		}

		session.SetDialogResponder(null);
	}
}
