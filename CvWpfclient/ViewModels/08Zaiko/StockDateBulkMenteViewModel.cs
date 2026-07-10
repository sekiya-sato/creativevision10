using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;


namespace CvWpfclient.ViewModels._08Zaiko;

public partial class StockDateBulkMenteViewModel : Helpers.BaseViewModel {
	const int AllDaysMask = 0b1111111;
	static readonly WeekdaySelectionItem[] WeekdayOptions = [
		new(1, "日", "日曜日"),
		new(2, "月", "月曜日"),
		new(3, "火", "火曜日"),
		new(4, "水", "水曜日"),
		new(5, "木", "木曜日"),
		new(6, "金", "金曜日"),
		new(7, "土", "土曜日"),
	];

	[ObservableProperty]
	public partial ObservableCollection<StockDateBulkRow> ListData { get; set; } = [];

	[ObservableProperty]
	public partial StockDateBulkRow? Current { get; set; }

	[ObservableProperty]
	public partial string SelectedTokuiText { get; set; } = "未選択";

	[ObservableProperty]
	public partial string BulkTanaDay { get; set; } = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial int TargetCount { get; set; }

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	List<long> selectedTokuiIds = [];

	[RelayCommand]
	void Init() {
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoList(CancellationToken ct) {
		var selected = ShowTokuiMultiSelect();
		if (selected == null) {
			Message = "一覧取得を中止しました。";
			return;
		}
		if (selected.Count == 0) {
			Message = "得意先を選択してください。";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		selectedTokuiIds = [.. selected.Select(x => x.Id).Where(id => id > 0).Distinct()];
		SelectedTokuiText = BuildSelectedTokuiText(selected);
		await LoadRowsAsync(selected, ct);
	}

	[RelayCommand]
	void SelectAllTargets() {
		foreach (var row in ListData) {
			row.IsTarget = true;
		}
		UpdateTargetCount();
	}

	[RelayCommand]
	void ClearAllTargets() {
		foreach (var row in ListData) {
			row.IsTarget = false;
		}
		UpdateTargetCount();
	}

	[RelayCommand]
	void ApplyBulkTanaDay() {
		if (!TryNormalizeYmd(BulkTanaDay, "一括設定の棚卸日", out var normalized)) return;

		var targets = ListData.Where(x => x.IsTarget).ToList();
		if (targets.Count == 0) {
			Message = "設定対象にチェックを付けてください。";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		foreach (var row in targets) {
			row.TanaDay = normalized;
		}
		BulkTanaDay = normalized;
		Message = $"{targets.Count:N0} 件に棚卸日を設定しました。";
	}

	[RelayCommand]
	void SelectAutoHoju(StockDateBulkRow? row) {
		if (row == null) return;

		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return;

		vm.DisplayNameCode = "曜日";
		vm.DisplayNameName = "名称";
		vm.DisplayNameRyaku = "";
		vm.DisplayWidthCode = 80;
		vm.DisplayWidthName = 140;
		vm.DisplayWidthRyaku = 0;
		vm.SetLocalData(WeekdayOptions, "自動補充曜日選択", selectedIds: MaskToOptionIds(row.AutoHoju));

		if (ClientLib.ShowDialogView(selWin, this, true) != true) return;

		var selected = vm.GetSelectedItems<WeekdaySelectionItem>();
		row.AutoHoju = OptionIdsToMask(selected.Select(x => x.Id));
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task UpdateSelected(CancellationToken ct) {
		var targets = ListData.Where(x => x.IsTarget).ToList();
		if (targets.Count == 0) {
			Message = "更新対象にチェックを付けてください。";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		foreach (var row in targets) {
			if (!TryNormalizeYmd(row.TanaDay, $"棚卸日({row.TokuiCode})", out var normalized)) return;
			row.TanaDay = normalized;
			row.AutoHoju = NormalizeAutoHoju(row.AutoHoju);
		}

		if (MessageEx.ShowQuestionDialog($"{targets.Count:N0} 件の棚卸日設定を更新しますか。", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsBusy = true;
		try {
			ClientLib.Cursor2Wait();
			var updateCount = 0;
			var insertCount = 0;
			foreach (var row in targets) {
				ct.ThrowIfCancellationRequested();
				var item = row.ToTran60TanaDate();
				var isUpdate = item.Id > 0;
				object parameter;
				if (isUpdate) {
					parameter = new UpdateParam(typeof(Tran60TanaDate), Common.SerializeObject(item));
				}
				else {
					parameter = new InsertParam(typeof(Tran60TanaDate), Common.SerializeObject(item));
				}
				var reply = await SendExecuteAsync(parameter, parameter.GetType(), ct);
				if (reply.Code < 0) {
					var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
					Message = $"更新に失敗しました。得意先={row.TokuiCode} {detail}";
					MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
					return;
				}
				if (Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) is Tran60TanaDate saved) {
					row.ApplySaved(saved);
				}
				if (isUpdate) updateCount++;
				else insertCount++;
			}

			Message = $"更新しました。追加 {insertCount:N0} 件、修正 {updateCount:N0} 件。";
			MessageEx.ShowInformationDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "更新をキャンセルしました。";
		}
		catch (Exception ex) {
			Message = $"更新に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
			ClientLib.Cursor2Normal();
		}
	}

	async Task LoadRowsAsync(IReadOnlyList<MasterTokui> selected, CancellationToken ct) {
		IsBusy = true;
		try {
			ClientLib.Cursor2Wait();
			var ids = selected.Select(x => x.Id).Where(id => id > 0).Distinct().ToArray();
			var tanaDateMap = await LoadTanaDateMapAsync(ids, ct);
			var rows = selected
				.Where(x => ids.Contains(x.Id))
				.OrderBy(x => x.Code)
				.Select((tokui, index) => CreateRow(index + 1, tokui, tanaDateMap.GetValueOrDefault(tokui.Id)))
				.ToList();

			DetachRows(ListData);
			ListData = new ObservableCollection<StockDateBulkRow>(rows);
			AttachRows(ListData);
			Count = ListData.Count;
			Current = ListData.FirstOrDefault();
			UpdateTargetCount();
			Message = $"{Count:N0} 件を取得しました。";
		}
		catch (OperationCanceledException) {
			Message = "一覧取得をキャンセルしました。";
		}
		catch (Exception ex) {
			Message = $"一覧取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
			ClientLib.Cursor2Normal();
		}
	}

	async Task<Dictionary<long, Tran60TanaDate>> LoadTanaDateMapAsync(long[] ids, CancellationToken ct) {
		if (ids.Length == 0) return [];
		var where = $"Id_Shop IN ({string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)))})";
		var param = new QueryListParam(typeof(Tran60TanaDate), where: where, order: "Id_Shop");
		var list = await QueryListAsync<Tran60TanaDate>(param, typeof(QueryListParam), ct);
		return list
			.GroupBy(x => x.Id_Shop)
			.ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());
	}

	async Task<IReadOnlyList<T>> QueryListAsync<T>(object parameter, Type dataType, CancellationToken ct) {
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = dataType,
			DataMsg = Common.SerializeObject(parameter)
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();
		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) {
			return [];
		}
		return [.. list.Cast<T>()];
	}

	async Task<CvMsg> SendExecuteAsync(object parameter, Type dataType, CancellationToken ct) {
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = dataType,
			DataMsg = Common.SerializeObject(parameter)
		};
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
	}

	IReadOnlyList<MasterTokui>? ShowTokuiMultiSelect() {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return null;
		vm.DisplayNameCode = "CD";
		vm.DisplayNameName = "名称";
		vm.DisplayNameRyaku = "略称";
		vm.SetParam(
			typeof(MasterTokui),
			"TenType in (0,3,6) and IsZaiko=1",
			"Code",
			selectedIds: selectedTokuiIds,
			startPos: selectedTokuiIds.FirstOrDefault());
		if (ClientLib.ShowDialogView(selWin, this, true) != true) return null;
		return vm.GetSelectedItems<MasterTokui>();
	}

	static StockDateBulkRow CreateRow(int rowNo, MasterTokui tokui, Tran60TanaDate? tanaDate) =>
		new() {
			RowNo = rowNo,
			IsTarget = true,
			RecordId = tanaDate?.Id ?? 0,
			Vdc = tanaDate?.Vdc ?? 0,
			Vdu = tanaDate?.Vdu ?? 0,
			Id_Shop = tokui.Id,
			TokuiCode = tokui.Code ?? string.Empty,
			TokuiName = tokui.Name ?? string.Empty,
			FixDay = tanaDate?.FixDay ?? "19010101",
			TanaDay = tanaDate?.TanaDay ?? DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			AutoHoju = NormalizeAutoHoju(tanaDate?.AutoHoju ?? 0)
		};

	void AttachRows(IEnumerable<StockDateBulkRow> rows) {
		foreach (var row in rows) {
			row.PropertyChanged += OnRowPropertyChanged;
		}
	}

	void DetachRows(IEnumerable<StockDateBulkRow> rows) {
		foreach (var row in rows) {
			row.PropertyChanged -= OnRowPropertyChanged;
		}
	}

	void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(StockDateBulkRow.IsTarget)) {
			UpdateTargetCount();
		}
	}

	void UpdateTargetCount() => TargetCount = ListData.Count(x => x.IsTarget);

	static string BuildSelectedTokuiText(IReadOnlyList<MasterTokui> selected) {
		if (selected.Count == 0) return "未選択";
		var labels = selected
			.OrderBy(x => x.Code)
			.Take(5)
			.Select(x => $"{x.Code} {x.Name}".Trim());
		var suffix = selected.Count > 5 ? " ..." : string.Empty;
		return $"{selected.Count:N0} 件: {string.Join(", ", labels)}{suffix}";
	}

	static bool TryNormalizeYmd(string? value, string label, out string normalized) {
		normalized = string.Empty;
		var text = (value ?? string.Empty).Trim();
		string[] formats = ["yyyyMMdd", "yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyy-M-d"];
		if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
			MessageEx.ShowWarningDialog($"{label} は yyyy/MM/dd 形式で入力してください。");
			return false;
		}
		normalized = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	static int NormalizeAutoHoju(int value) => value & AllDaysMask;

	static IEnumerable<long> MaskToOptionIds(int mask) {
		var normalized = NormalizeAutoHoju(mask);
		for (var i = 0; i < 7; i++) {
			if ((normalized & (1 << i)) != 0) {
				yield return i + 1;
			}
		}
	}

	static int OptionIdsToMask(IEnumerable<long> ids) {
		var mask = 0;
		foreach (var id in ids) {
			var bit = (int)id - 1;
			if (bit is >= 0 and <= 6) {
				mask |= 1 << bit;
			}
		}
		return NormalizeAutoHoju(mask);
	}

	public static string FormatAutoHojuText(int value) {
		var mask = NormalizeAutoHoju(value);
		if (mask == 0) return "なし";
		if (mask == AllDaysMask) return "全日";
		return string.Concat(WeekdayOptions.Where(x => (mask & (1 << ((int)x.Id - 1))) != 0).Select(x => x.Code));
	}
}

public partial class StockDateBulkRow : ObservableObject {
	public int RowNo { get; set; }
	public long RecordId { get; set; }
	public long Vdc { get; set; }
	public long Vdu { get; set; }
	public long Id_Shop { get; set; }
	public string TokuiCode { get; set; } = string.Empty;
	public string TokuiName { get; set; } = string.Empty;
	public string FixDay { get; set; } = "19010101";

	[ObservableProperty]
	public partial bool IsTarget { get; set; } = true;

	[ObservableProperty]
	public partial string TanaDay { get; set; } = "19010101";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(AutoHojuText))]
	public partial int AutoHoju { get; set; }

	public string AutoHojuText => StockDateBulkMenteViewModel.FormatAutoHojuText(AutoHoju);

	public Tran60TanaDate ToTran60TanaDate() =>
		new() {
			Id = RecordId,
			Vdc = Vdc,
			Vdu = Vdu,
			Id_Shop = Id_Shop,
			TanaDay = TanaDay,
			FixDay = string.IsNullOrWhiteSpace(FixDay) ? "19010101" : FixDay,
			AutoHoju = AutoHoju
		};

	public void ApplySaved(Tran60TanaDate item) {
		RecordId = item.Id;
		Vdc = item.Vdc;
		Vdu = item.Vdu;
		Id_Shop = item.Id_Shop;
		TanaDay = item.TanaDay;
		FixDay = item.FixDay;
		AutoHoju = item.AutoHoju;
	}
}

public sealed class WeekdaySelectionItem : BaseDbClass {
	public WeekdaySelectionItem(long id, string code, string name) {
		Id = id;
		Code = code;
		Name = name;
	}

	public string Code { get; set; }
	public string Name { get; set; }
}
