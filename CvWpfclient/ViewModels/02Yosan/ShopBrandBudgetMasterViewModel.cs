using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

/// <summary>
/// 店ブランド予算マスタ（月一括）ViewModel
/// </summary>
public partial class ShopBrandBudgetMasterViewModel : BaseViewModel {

	[ObservableProperty]
	string title = "店ブランド予算マスタ（月一括）";

	[ObservableProperty]
	DateTime selectedYearMonth = DateTime.Now;

	[ObservableProperty]
	long selectedShopId;

	[ObservableProperty]
	string selectedShopCode = string.Empty;

	[ObservableProperty]
	string selectedShopName = string.Empty;

	[ObservableProperty]
	long selectedBrandId;

	[ObservableProperty]
	string selectedBrandCode = string.Empty;

	[ObservableProperty]
	string selectedBrandName = string.Empty;

	[ObservableProperty]
	string selectedYearMonthString = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	double saturdaySundayCoefficient = 1.0;

	[ObservableProperty]
	string holidayDaysText = string.Empty;

	[ObservableProperty]
	long monthlyBudget;

	[ObservableProperty]
	ObservableCollection<DailyBudgetRow> dailyBudgets = [];

	[ObservableProperty]
	long totalBudget;

	[ObservableProperty]
	long remainingBudget;

	[ObservableProperty]
	string message = string.Empty;

	[ObservableProperty]
	bool isBusy;

	[ObservableProperty]
	DailyBudgetRow? selectedDailyBudgetRow;

	bool isApplyingHolidayDays;

	public IEnumerable<DailyBudgetRow> FirstHalfDailyBudgets => DailyBudgets.Where(row => row.Day <= 15);

	public IEnumerable<DailyBudgetRow> SecondHalfDailyBudgets => DailyBudgets.Where(row => row.Day > 15);

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	partial void OnSaturdaySundayCoefficientChanged(double value) {
		foreach (var row in DailyBudgets) {
			UpdateRowCoefficient(row);
		}
		RecalculateTotals();
	}

	partial void OnHolidayDaysTextChanged(string value) {
		ApplyHolidayDays();
	}

	partial void OnDailyBudgetsChanged(ObservableCollection<DailyBudgetRow> value) {
		foreach (var row in value) {
			row.PropertyChanged += OnDailyBudgetRowPropertyChanged;
		}
		NotifyDailyBudgetViews();
		RecalculateTotals();
	}

	void OnDailyBudgetRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (sender is DailyBudgetRow row) {
			if (e.PropertyName == nameof(DailyBudgetRow.IsHoliday)) {
				UpdateRowCoefficient(row);
			}
			if (!isApplyingHolidayDays) {
				RecalculateTotals();
			}
		}
	}

	void UpdateRowCoefficient(DailyBudgetRow row) {
		if (row.IsHoliday) {
			row.Coefficient = 0;
			row.SalesBudget = 0;
		} else if (row.IsSaturday || row.IsSunday) {
			row.Coefficient = SaturdaySundayCoefficient;
		} else {
			row.Coefficient = 1.0;
		}
	}

	[RelayCommand]
	void Init() {
		SelectedYearMonth = DateTime.Now;
		ClearAll();
	}

	[RelayCommand]
	async Task LoadBudget(CancellationToken ct) {
		if (SelectedShopId == 0 || SelectedBrandId == 0) {
			MessageEx.ShowWarningDialog("店舗とブランドを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		IsBusy = true;
		try {
			ct.ThrowIfCancellationRequested();
			ClientLib.Cursor2Wait();

			var (dateFrom, dateTo) = GetDateRange();
			var where = $"Id_Tenpo = {SelectedShopId} AND Id_Brand = {SelectedBrandId} AND DenDay >= '{dateFrom}' AND DenDay <= '{dateTo}'";
			var param = new QueryListParam(
				itemType: typeof(MasterYosanBrand),
				where: where,
				order: "DenDay"
			);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(param)
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();

			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			if (list == null || list.Count == 0) {
				MessageEx.ShowInformationDialog("予算データがありません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			GenerateDailyRows();
			long total = 0;
			foreach (var item in list) {
				if (item is not MasterYosanBrand yosan) continue;
				if (!int.TryParse(yosan.DenDay.Substring(6, 2), out var day)) continue;
				var row = DailyBudgets.FirstOrDefault(r => r.Day == day);
				if (row == null) continue;
				row.SalesBudget = yosan.UriYosan / 1000;
				total += row.SalesBudget;
			}
			MonthlyBudget = total;
			ApplyHolidayDays();
			Message = $"{list.Count}件の予算データを読み込みました。";
		}
		catch (OperationCanceledException) {
			Message = "読み込みをキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"予算読み込み失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void CreateBudget() {
		if (SelectedShopId == 0 || SelectedBrandId == 0) {
			MessageEx.ShowWarningDialog("店舗とブランドを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		GenerateDailyRows();
		AutoAllocateBudget();
	}

	[RelayCommand]
	async Task SaveBudget(CancellationToken ct) {
		if (SelectedShopId == 0 || SelectedBrandId == 0) {
			MessageEx.ShowWarningDialog("店舗とブランドを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (DailyBudgets.Count == 0) {
			MessageEx.ShowWarningDialog("予算データがありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog("予算データを登録しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		IsBusy = true;
		try {
			ct.ThrowIfCancellationRequested();
			ClientLib.Cursor2Wait();

			await DeleteExistingBudgets(ct);
			ct.ThrowIfCancellationRequested();

			var newRecords = new List<MasterYosanBrand>();
			var (dateFrom, _) = GetDateRange();
			var yearMonthStr = dateFrom.Substring(0, 6);
			foreach (var row in DailyBudgets) {
				var dayStr = row.Day.ToString("00", CultureInfo.InvariantCulture);
				var record = new MasterYosanBrand {
					Id_Tenpo = SelectedShopId,
					Id_Brand = SelectedBrandId,
					DenDay = yearMonthStr + dayStr,
					UriYosan = row.SalesBudget * 1000,
					ArariYosan = 0
				};
				newRecords.Add(record);
			}
			var bulkParam = new InsertBulkParam(
				itemType: typeof(MasterYosanBrand),
				item: JsonConvert.SerializeObject(newRecords)
			);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(InsertBulkParam),
				DataMsg = Common.SerializeObject(bulkParam)
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				MessageEx.ShowErrorDialog($"登録に失敗しました: {reply.DataMsg}", owner: ClientLib.GetActiveView(this));
				return;
			}
			Message = "予算データを登録しました。";
			MessageEx.ShowInformationDialog("予算登録しました", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "登録をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"予算登録失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	async Task DeleteBudget(CancellationToken ct) {
		if (SelectedShopId == 0 || SelectedBrandId == 0) {
			MessageEx.ShowWarningDialog("店舗とブランドを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog("予算データを削除しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		IsBusy = true;
		try {
			ct.ThrowIfCancellationRequested();
			ClientLib.Cursor2Wait();
			await DeleteExistingBudgets(ct);
			DailyBudgets.Clear();
			NotifyDailyBudgetViews();
			MonthlyBudget = 0;
			RecalculateTotals();
			Message = "予算データを削除しました。";
			MessageEx.ShowInformationDialog("削除完了しました", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "削除をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"予算削除失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void AutoAllocateBudget() {
		if (MonthlyBudget <= 0) {
			MessageEx.ShowWarningDialog("店舗月売上予算を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (DailyBudgets.Count == 0) {
			GenerateDailyRows();
		}
		var totalCoefficients = DailyBudgets.Sum(r => r.Coefficient);
		if (totalCoefficients <= 0) {
			MessageEx.ShowWarningDialog("按分可能な日がありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		foreach (var row in DailyBudgets) {
			row.SalesBudget = (long)Math.Round(MonthlyBudget * row.Coefficient / totalCoefficients);
		}
		RecalculateTotals();
		Message = "予算を自動配分しました。";
	}

	[RelayCommand]
	void RecalculateTotals() {
		long runningTotal = 0;
		foreach (var row in DailyBudgets) {
			runningTotal += row.SalesBudget;
			row.RunningTotal = runningTotal;
		}
		TotalBudget = runningTotal;
		RemainingBudget = MonthlyBudget - TotalBudget;
	}

	[RelayCommand]
	void SelectShop() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType in (1,3,6)", "Code", startPos: SelectedShopId);
		if (tokui == null) return;
		SelectedShopId = tokui.Id;
		SelectedShopCode = tokui.Code ?? string.Empty;
		SelectedShopName = tokui.Name ?? string.Empty;
	}

	[RelayCommand]
	void SelectBrand() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code", startPos: SelectedBrandId);
		if (meisho == null) return;
		SelectedBrandId = meisho.Id;
		SelectedBrandCode = meisho.Code ?? string.Empty;
		SelectedBrandName = meisho.Name ?? string.Empty;
	}

	[RelayCommand]
	void ClearAll() {
		DailyBudgets.Clear();
		NotifyDailyBudgetViews();
		MonthlyBudget = 0;
		TotalBudget = 0;
		RemainingBudget = 0;
		Message = string.Empty;
	}

	void GenerateDailyRows() {
		DailyBudgets.Clear();
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;
		var daysInMonth = DateTime.DaysInMonth(year, month);
		for (int day = 1; day <= daysInMonth; day++) {
			var date = new DateTime(year, month, day);
			var row = new DailyBudgetRow {
				Day = day,
				DayOfWeek = GetDayOfWeekString(date),
				IsSaturday = date.DayOfWeek == DayOfWeek.Saturday,
				IsSunday = date.DayOfWeek == DayOfWeek.Sunday,
				IsHoliday = false,
				Coefficient = 1.0
			};
			if (row.IsSaturday || row.IsSunday) {
				row.Coefficient = SaturdaySundayCoefficient;
			}
			row.PropertyChanged += OnDailyBudgetRowPropertyChanged;
			DailyBudgets.Add(row);
		}
		NotifyDailyBudgetViews();
		ApplyHolidayDays();
	}

	void ApplyHolidayDays() {
		var holidayDays = ParseHolidayDays(HolidayDaysText);
		isApplyingHolidayDays = true;
		try {
			foreach (var row in DailyBudgets) {
				row.IsHoliday = holidayDays.Contains(row.Day);
				UpdateRowCoefficient(row);
			}
		}
		finally {
			isApplyingHolidayDays = false;
		}
		RecalculateTotals();
	}

	void NotifyDailyBudgetViews() {
		OnPropertyChanged(nameof(FirstHalfDailyBudgets));
		OnPropertyChanged(nameof(SecondHalfDailyBudgets));
	}

	static HashSet<int> ParseHolidayDays(string text) {
		var result = new HashSet<int>();
		if (string.IsNullOrWhiteSpace(text)) return result;
		var tokens = text.Split([' ', '　', '\t', '\r', '\n', ',', '、', '，'], StringSplitOptions.RemoveEmptyEntries);
		foreach (var token in tokens) {
			if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day > 0) {
				result.Add(day);
			}
		}
		return result;
	}

	async Task DeleteExistingBudgets(CancellationToken ct) {
		var (dateFrom, dateTo) = GetDateRange();
		var where = $"Id_Tenpo = {SelectedShopId} AND Id_Brand = {SelectedBrandId} AND DenDay >= '{dateFrom}' AND DenDay <= '{dateTo}'";
		var param = new QueryListParam(
			itemType: typeof(MasterYosanBrand),
			where: where,
			order: "DenDay"
		);
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
		if (list == null) return;
		foreach (var item in list) {
			if (item is not MasterYosanBrand yosan || yosan.Id == 0) continue;
			var deleteParam = new DeleteByIdParam(
				itemType: typeof(MasterYosanBrand),
				id: yosan.Id,
				originalVdu: yosan.Vdu
			);
			var deleteMsg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(DeleteByIdParam),
				DataMsg = Common.SerializeObject(deleteParam)
			};
			await coreService.QueryMsgAsync(deleteMsg, AppGlobal.GetDefaultCallContext(ct));
		}
	}

	(string dateFrom, string dateTo) GetDateRange() {
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;
		var daysInMonth = DateTime.DaysInMonth(year, month);
		var from = new DateTime(year, month, 1);
		var to = new DateTime(year, month, daysInMonth);
		return (from.ToString("yyyyMMdd", CultureInfo.InvariantCulture), to.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
	}

	static string GetDayOfWeekString(DateTime date) {
		return date.DayOfWeek switch {
			DayOfWeek.Monday => "月",
			DayOfWeek.Tuesday => "火",
			DayOfWeek.Wednesday => "水",
			DayOfWeek.Thursday => "木",
			DayOfWeek.Friday => "金",
			DayOfWeek.Saturday => "土",
			DayOfWeek.Sunday => "日",
			_ => ""
		};
	}

	TResult? ShowSelectDialog<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}
}

/// <summary>
/// 日別予算行
/// </summary>
public partial class DailyBudgetRow : ObservableObject {
	[ObservableProperty]
	int day;

	[ObservableProperty]
	string dayOfWeek = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(RunningTotal))]
	long salesBudget;

	[ObservableProperty]
	long runningTotal;

	[ObservableProperty]
	double coefficient = 1.0;

	[ObservableProperty]
	bool isHoliday;

	[ObservableProperty]
	bool isSaturday;

	[ObservableProperty]
	bool isSunday;
}
