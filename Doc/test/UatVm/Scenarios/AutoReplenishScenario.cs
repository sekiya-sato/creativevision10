using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.ViewModels._05Shiire;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._01Master;
using CvWpfclient.Views._05Shiire;
using CvWpfclient.Views._07Haibun;
using CvWpfclient.Views._31Monthly;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>専用DBで設定→補充→発注→仕入→次回補充を実View/実gRPCで確認する。</summary>
public static class AutoReplenishScenario {
	static AutoReplenishSeeder.Result? seeded;
	public static void Seeder(string dbPath) => seeded = AutoReplenishSeeder.Seed(dbPath, Console.WriteLine);

	public static async Task RunAsync(VmSession session) {
		var data = seeded ?? throw new InvalidOperationException("専用DBのシードがありません。");
		var screens = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", "autoreplenish-" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			var stock = session.OpenView<ZaikoAutoHojunMenteView, ZaikoAutoHojunMenteViewModel>();
			await stock.WaitAsync("基準画面初期化", vm => !vm.IsBusy && vm.InitCommand.ExecutionTask is { IsCompleted: true });
			stock.Input("基準10・優先0・有効", vm => {
				Fill(vm, data); vm.Tenpo = data.Store; vm.TargetSuText = "10"; vm.PriorityText = "0"; vm.FlagValue = true;
			});
			await stock.RunAsync("基準設定保存", vm => vm.SaveCommand);
			if (!session.Check("基準設定がDBに保存され一覧へ戻る", stock.Vm.Rows.Count == 1 && stock.Vm.Rows.Single() is { TargetSu: 10, FlagValue: true }, stock.Vm.Message)) return;
			await ReadStockAsync(stock);
			session.Check("基準設定一覧選択で編集値復元", stock.Vm.Tenpo?.Id == data.Store.Id && stock.Vm.SelectedSku?.Id_Col == data.Sku.Id_Col && stock.Vm.SelectedSku.Id_Siz == data.Sku.Id_Siz && stock.Vm.TargetSuText == "10");
			await HaibunScreenScenario.CaptureAsync(session, stock.View, screens, "01_StockSetting");
			await CheckButtonReachabilityAsync(session, stock.View, screens, "StockSetting");

			var exclude = session.OpenView<AutoHachuHojunExcludeSettingView, AutoHachuHojunExcludeSettingViewModel>();
			await exclude.WaitAsync("除外画面初期化", vm => !vm.IsBusy && vm.InitCommand.ExecutionTask is { IsCompleted: true });
			exclude.Input("SKU除外", vm => { Fill(vm, data); vm.FlagValue = true; });
			await exclude.RunAsync("除外設定保存", vm => vm.SaveCommand);
			if (!session.Check("除外設定保存", exclude.Vm.Rows.Count == 1 && exclude.Vm.Rows.Single().FlagValue, exclude.Vm.Message)) return;
			await HaibunScreenScenario.CaptureAsync(session, exclude.View, screens, "02_ExcludeSetting");
			await CheckButtonReachabilityAsync(session, exclude.View, screens, "ExcludeSetting");

			var monthly = session.OpenView<AutoOrderReplenishExecuteView, AutoOrderReplenishExecuteViewModel>();
			monthly.Input("対象倉庫", vm => { vm.WarehouseId = data.Warehouse.Id; vm.WarehouseText = data.Warehouse.Code + " " + data.Warehouse.Name; });
			await monthly.RunAsync("除外中プレビュー", vm => vm.PreviewCommand);
			session.Check("除外SKUは発注・配分候補なし", monthly.Vm.Rows.Count > 0 && monthly.Vm.Rows.All(x => x.Row.DemandSu == 0 && x.Row.TransferSu == 0 && x.Row.Su == 0) && monthly.Vm.Rows.Any(x => x.Reason.Contains("除外")) && !monthly.Vm.SaveCommand.CanExecute(null), monthly.Vm.StatusMessage);
			exclude.Input("除外一覧選択", vm => vm.SelectedRow = vm.Rows.Single());
			await exclude.RunAsync("除外設定読込", vm => vm.LoadSelectedCommand);
			exclude.Input("除外解除", vm => vm.FlagValue = false);
			await exclude.RunAsync("除外解除保存", vm => vm.SaveCommand);
			session.Check("除外解除は物理削除せず保持", exclude.Vm.Rows.Count == 1 && !exclude.Vm.Rows.Single().FlagValue, exclude.Vm.Message);

			stock.Input("店舗設定無効化", vm => vm.FlagValue = false);
			await stock.RunAsync("無効化保存", vm => vm.SaveCommand);
			await monthly.RunAsync("無効設定プレビュー", vm => vm.PreviewCommand);
			session.Check("無効店舗は補充対象外", monthly.Vm.Rows.All(x => x.Row.Su == 0 && x.Row.TransferSu == 0), monthly.Vm.StatusMessage);
			await ReadStockAsync(stock);
			stock.Input("店舗設定再有効化", vm => vm.FlagValue = true);
			await stock.RunAsync("再有効化保存", vm => vm.SaveCommand);

			await monthly.RunAsync("基準10プレビュー", vm => vm.PreviewCommand);
			if (!CheckPlan(session, monthly.Vm, 8, 2, "初回")) return;
			await CaptureMonthlyAsync(session, monthly.View, screens);
			await CheckButtonReachabilityAsync(session, monthly.View, screens, "Monthly");
			var beforeOrders = (await Orders(session, data)).Count;
			await monthly.RunAsync("補充保存", vm => vm.SaveCommand);
			if (!session.Check("補充保存は未確定で発注なし", monthly.Vm.CurrentBatch?.Status == 0 && (await Orders(session, data)).Count == beforeOrders, monthly.Vm.StatusMessage)) return;
			var staleId = monthly.Vm.CurrentBatch!.Id;
			await monthly.RunAsync("履歴取得", vm => vm.LoadHistoryCommand);
			monthly.Input("履歴選択", vm => vm.SelectedHistory = vm.HistoryRows.Single(x => x.Id == staleId));
			await monthly.RunAsync("履歴から補充読込", vm => vm.LoadCommand);
			session.Check("履歴から内訳と未確定状態を復元", monthly.Vm.CurrentBatch?.Id == staleId && monthly.Vm.Rows.Sum(x => x.Row.TransferSu) == 8 && monthly.Vm.Rows.Sum(x => x.Row.Su) == 2);

			await ReadStockAsync(stock);
			stock.Input("保存後に基準数を変更", vm => vm.TargetSuText = "11");
			await stock.RunAsync("基準11保存", vm => vm.SaveCommand);
			await monthly.RunAsync("古い計算の確定拒否", vm => vm.CommitCommand);
			var stillDraft = (await session.QueryAsync<TranAutoReplenishBatch>("WHERE Id=@0", staleId.ToString())).Single();
			session.Check("Fingerprint不一致は未確定維持・発注生成なし", stillDraft.Status == 0 && (await Orders(session, data)).Count == beforeOrders && (monthly.Vm.ErrorText.Length > 0 || monthly.Vm.StatusMessage.Contains("再計算")), new { monthly.Vm.ErrorText, monthly.Vm.StatusMessage });
			await monthly.RunAsync("旧補充取消", vm => vm.CancelCommand);
			session.Check("取消で履歴保持・実伝票なし", monthly.Vm.CurrentBatch?.Status == 2 && (await Orders(session, data)).Count == beforeOrders);
			await ReadStockAsync(stock);
			stock.Input("基準10へ戻す", vm => vm.TargetSuText = "10");
			await stock.RunAsync("基準10保存", vm => vm.SaveCommand);
			await monthly.RunAsync("再計算", vm => vm.PreviewCommand);
			await monthly.RunAsync("再保存", vm => vm.SaveCommand);
			var commitRequest = new AutoReplenishParam(data.Warehouse.Id, monthly.Vm.BasisDay, AutoReplenishOperation.Commit,
				ExecutionKey: monthly.Vm.CurrentBatch!.ExecutionKey, Id_Batch: monthly.Vm.CurrentBatch.Id, ExpectedVdu: monthly.Vm.CurrentBatch.Vdu);
			await monthly.RunAsync("補充確定", vm => vm.CommitCommand);
			var orders = await Orders(session, data);
			var allocations = await session.QueryAsync<TranHaibun>("WHERE Id_Soko=@0 AND Id_Shohin=@1", data.Warehouse.Id.ToString(), data.Product.Id.ToString());
			if (!session.Check("確定は区分15発注2・区分1配分8を生成", monthly.Vm.CurrentBatch?.Status == 1 && orders.Count == 1 && orders[0].Kubun == 15 && orders[0].SuTotal == 2 && allocations.Count == 1 && allocations[0].Kubun == 1 && allocations[0].Su == 8, monthly.Vm.StatusMessage)) return;
			var reply = await SendAsync(commitRequest);
			session.Check("同一補充の確定再試行は既存結果・重複なし", reply.Code >= 0 && (await Orders(session, data)).Count == 1 && (await session.QueryAsync<TranHaibun>("WHERE Id_Soko=@0 AND Id_Shohin=@1", data.Warehouse.Id.ToString(), data.Product.Id.ToString())).Count == 1, reply.Option);
			await monthly.RunAsync("発注後プレビュー", vm => vm.PreviewCommand);
			session.Check("既存発注2と配分8で追加発注・配分なし", monthly.Vm.Rows.All(x => x.Row.Su == 0 && x.Row.TransferSu == 0));

			var receipt = session.OpenView<ShiireInputView, ShiireInputViewModel>();
			await Application.Current.Dispatcher.InvokeAsync(receipt.View.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
			await receipt.WaitAsync("仕入画面描画・処理終了", vm => receipt.View.IsLoaded && !vm.HasRunningCommand && !vm.IsDetailLoading);
			receipt.Input("生成発注を仕入入荷", vm => vm.CurrentEdit = CreateReceipt(data, orders.Single().Id));
			await receipt.RunAsync("仕入画面から2点入荷", vm => vm.DoInsertOnDetailTabCommand);
			if (!session.Check("仕入画面の登録採番", receipt.Vm.CurrentEdit.Id > 0, receipt.Vm.CurrentEdit.Id)) return;
			await monthly.RunAsync("入荷後次回補充", vm => vm.PreviewCommand);
			session.Check("入荷後は店舗残需要2を在庫配分、追加発注0", monthly.Vm.Rows.Sum(x => x.Row.TransferSu) == 2 && monthly.Vm.Rows.Sum(x => x.Row.Su) == 0, monthly.Vm.StatusMessage);
			await monthly.RunAsync("入荷分補充保存", vm => vm.SaveCommand);
			await monthly.RunAsync("入荷分補充確定", vm => vm.CommitCommand);
			await ConfirmTransfersAsync(session, data, monthly);
			await CheckSupplierUiAsync(session, data);
		} finally { session.SetDialogResponder(null); }
	}

	static async Task CheckButtonReachabilityAsync(VmSession session, Window view, string screens, string name) {
		var width = view.Width;
		var height = view.Height;
		try {
			foreach (var minimum in new[] { false, true }) {
				view.Width = minimum ? view.MinWidth : width;
				view.Height = minimum ? view.MinHeight : height;
				await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
				var failed = new List<string>();
				foreach (var button in Descendants(view).OfType<Button>().Where(x => x.Command != null && x.IsVisible).ToList()) {
					button.BringIntoView();
					await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
					var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
					var visible = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
					for (DependencyObject? parent = VisualTreeHelper.GetParent(button); parent != null && parent != view; parent = VisualTreeHelper.GetParent(parent)) {
						if (parent is System.Windows.Controls.ScrollContentPresenter presenter)
							visible.Intersect(presenter.TransformToAncestor(view).TransformBounds(new Rect(presenter.RenderSize)));
					}
					visible.Inflate(1, 1);
					if (!visible.Contains(bounds)) failed.Add(button.Content?.ToString() ?? button.Name);
				}
				session.Check(name + (minimum ? ":最小サイズ" : ":通常サイズ") + "で全操作ボタンへ到達", failed.Count == 0,
					new { view.ActualWidth, view.ActualHeight, FailedButtons = failed });
				if (minimum) session.Note(name + ":最小サイズ画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name + "_Minimum") });
			}
		} finally {
			view.Width = width; view.Height = height;
			await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		}
	}
	// 横スクロール境界の部分列だけは到達確認で判定し、他の文字切れを免除しない。
	static async Task CaptureMonthlyAsync(VmSession session, Window view, string screens) {
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		var path = ScreenLayoutCheck.SaveJpeg(view, screens, "03_Preview");
		session.Note("03_Preview:画面画像", new { Path = path });
		var grid = Descendants(view).OfType<DataGrid>().First();
		var scroll = Descendants(grid).OfType<ScrollViewer>().FirstOrDefault(x => x.ScrollableWidth > 0);
		var issues = ScreenLayoutCheck.Inspect(view);
		var scrollIssues = issues.Where(x => x.Kind == "列が横スクロール外").ToList();
		var otherIssues = issues.Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && x.Kind != "列が横スクロール外").ToList();
		session.Check("03_Preview:表示崩れなし", otherIssues.Count == 0 && (scrollIssues.Count == 0 || scroll != null), new { Issues = otherIssues, ScrollBoundaryColumns = scrollIssues });
		if (scroll == null) { session.Fail("補充内訳の横スクロール", "横スクロール可能なScrollViewerがありません。"); return; }
		scroll.ScrollToRightEnd();
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		var lastColumn = grid.Columns.OrderBy(x => x.DisplayIndex).Last();
		var header = Descendants(grid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().FirstOrDefault(x => x.Column == lastColumn);
		var lastBounds = header?.TransformToAncestor(view).TransformBounds(new Rect(header.RenderSize)) ?? Rect.Empty;
		session.Check("補充内訳は右端へスクロールして最終列まで確認できる", scroll.ScrollableWidth > 0 && scroll.HorizontalOffset >= scroll.ScrollableWidth - 1
			&& header is { IsVisible: true } && lastBounds.Width > 0 && lastBounds.Left >= 0 && lastBounds.Right <= view.ActualWidth + 1,
			new { scroll.ScrollableWidth, scroll.HorizontalOffset, Header = lastColumn.Header, lastBounds });
		var rightIssues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && x.Kind != "列が横スクロール外").ToList();
		session.Check("03_PreviewRight:日付・入力・ボタンの表示崩れなし", rightIssues.Count == 0, rightIssues);
		session.Note("03_PreviewRight:画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, "03_PreviewRight") });
		scroll.ScrollToLeftEnd();
	}
	static void Fill(AutoReplenishSettingViewModel vm, AutoReplenishSeeder.Result data) {
		vm.Soko = data.Warehouse; vm.Shohin = data.Product;
		vm.Skus = new ObservableCollection<MasterShohinColSiz>(data.Product.Jcolsiz!); vm.SelectedSku = vm.Skus.Single();
	}
	static async Task ReadStockAsync(ViewDriver<ZaikoAutoHojunMenteViewModel> driver) {
		driver.Input("基準一覧選択", vm => vm.SelectedRow = vm.Rows.Single());
		await driver.RunAsync("基準一覧読込", vm => vm.LoadSelectedCommand);
	}
	static bool CheckPlan(VmSession session, AutoOrderReplenishExecuteViewModel vm, int transfer, int order, string label) =>
		session.Check(label + "必要10＝配分8＋発注2", vm.ErrorText.Length == 0 && vm.Rows.Sum(x => x.Row.TransferSu) == transfer && vm.Rows.Sum(x => x.Row.Su) == order && vm.Rows.Sum(x => x.Row.DemandSu) == transfer + order, new { vm.ErrorText, vm.StatusMessage, vm.SummaryText });
	static Task<List<Tran13Hachu>> Orders(VmSession session, AutoReplenishSeeder.Result data) => session.QueryAsync<Tran13Hachu>("WHERE Id_Soko=@0 AND Id_Shiire=@1", data.Warehouse.Id.ToString(), data.Supplier.Id.ToString());
	static Task<CvMsg> SendAsync(AutoReplenishParam request) => AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg201_Op_Execute, DataType = typeof(AutoReplenishParam), DataMsg = Common.SerializeObject(request) }, AppGlobal.GetDefaultCallContext());

	static Tran03Shiire CreateReceipt(AutoReplenishSeeder.Result d, long orderId) => new() {
		DenDay = DateTime.Today.ToString("yyyyMMdd"), KakeDay = DateTime.Today.ToString("yyyyMMdd"), Id_Soko = d.Warehouse.Id,
		VSoko = new(d.Warehouse.Id, d.Warehouse.Code, d.Warehouse.Name), Id_Shiire = d.Supplier.Id,
		VShiire = new(d.Supplier.Id, d.Supplier.Code, d.Supplier.Name), Id_Shain = d.Employee.Id,
		VShain = new(d.Employee.Id, d.Employee.Code, d.Employee.Name), Kubun = (int)EnumShiire.Shiire,
		IsPay = 1, Rate = 100, TaxCalcUnit = (int)EnumTaxCalcUnit.Slip, RelateNo1 = checked((int)orderId),
		Jmeisai = [new() { No = 1, Id_Shohin = d.Product.Id, Code_Shohin = d.Product.Code, Mei_Shohin = d.Product.Name,
			Id_Col = d.Sku.Id_Col, Code_Col = d.Sku.Code_Col, Mei_Col = d.Sku.Mei_Col, Id_Siz = d.Sku.Id_Siz,
			Code_Siz = d.Sku.Code_Siz, Mei_Siz = d.Sku.Mei_Siz, JanCode = d.Sku.Jan1, Id_Tax = 1,
			Su = 2, Tanka = 1000, Kingaku = 2000, Gedai = 1000, Jodai = 2000 }] };

	static async Task ConfirmTransfersAsync(VmSession session, AutoReplenishSeeder.Result data, ViewDriver<AutoOrderReplenishExecuteViewModel> monthly) {
		var confirm = session.OpenView<HaibunCommitView, HaibunCommitViewModel>();
		var day = DateTime.Today.ToString("yyyy/MM/dd");
		confirm.Input("自動補充配分の確定条件", vm => { vm.DenDayFromText = day; vm.DenDayToText = day; vm.KakuteiDayText = day; vm.SokoCode = data.Warehouse.Code; });
		await confirm.RunAsync("自動補充配分検索", vm => vm.SearchCommand);
		foreach (var row in confirm.Vm.Rows) row.IsChecked = true;
		await confirm.RunAsync("自動補充配分確定", vm => vm.ConfirmSelectedCommand);
		var transfers = await session.QueryAsync<Tran10IdoOut>("WHERE Id_Soko=@0 AND Id_Ido=@1", data.Warehouse.Id.ToString(), data.Store.Id.ToString());
		session.Check("店舗配分10が移動出庫へ変換", transfers.Sum(x => x.SuTotal) == 10);
		await monthly.RunAsync("店舗積送中の補充計算", vm => vm.PreviewCommand);
		session.Check("配分完了後は店舗積送供給を控除して二重補充なし", monthly.Vm.ErrorText.Length == 0 && monthly.Vm.Rows.All(x => x.Row.Su == 0 && x.Row.TransferSu == 0), new { monthly.Vm.StatusMessage, monthly.Vm.ErrorText });
	}

	static async Task CheckSupplierUiAsync(VmSession session, AutoReplenishSeeder.Result data) {
		var product = session.OpenView<MasterShohinMenteView, MasterShohinMenteViewModel>();
		await Application.Current.Dispatcher.InvokeAsync(product.View.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
		product.Input("通常仕入商品", vm => vm.CurrentEdit = data.Product);
		await product.WaitAsync("商品編集データ適用", vm => product.View.IsLoaded && vm.CurrentEdit.Id == data.Product.Id);
		foreach (var tab in Descendants(product.View).OfType<TabControl>().ToList()) {
			for (var i = 0; i < tab.Items.Count; i++) {
				tab.SelectedIndex = i; product.View.UpdateLayout();
				var supplier = Descendants(product.View).OfType<TextBox>().FirstOrDefault(x => BindingOperations.GetBinding(x, TextBox.TextProperty)?.Path?.Path == "CurrentEdit.Id_ConsignmentShiire");
				if (supplier == null) continue;
				await product.WaitAsync("委託仕入先入力欄の描画", vm => supplier.IsVisible && supplier.Text == data.Supplier.Id.ToString());
				var calc = Descendants(product.View).OfType<FrameworkElement>().FirstOrDefault(x => BindingOperations.GetBinding(x, UIElement.IsEnabledProperty)?.Path?.Path == "IsConsumptionPurchase");
				session.Check("通常仕入でも委託仕入先入力可・消化計算条件は無効", supplier.IsEnabled && calc is { IsEnabled: false } && !product.Vm.IsConsumptionPurchase);
				return;
			}
		}
		await Task.CompletedTask;
		session.Fail("商品委託仕入先の実入力欄", "委託仕入先TextBoxが見つかりません。");
	}
	static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
			var child = VisualTreeHelper.GetChild(root, i); yield return child;
			foreach (var nested in Descendants(child)) yield return nested;
		}
	}
}
