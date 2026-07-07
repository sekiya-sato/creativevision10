using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using Grpc.Core;
using System.Collections;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._02Yosan;

public partial class ShopBudgetReportViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	string title = "店舗予算表";

	[ObservableProperty]
	DateTime selectedYearMonth = DateTime.Now;

	[ObservableProperty]
	string selectedYearMonthString = DateTime.Now.ToString("yyyy/MM", CultureInfo.InvariantCulture);

	[ObservableProperty]
	string shopCodeFrom = string.Empty;

	[ObservableProperty]
	string shopCodeTo = string.Empty;

	[ObservableProperty]
	bool isByShop = true;

	[ObservableProperty]
	bool isDateComparison = true;

	partial void OnSelectedYearMonthChanged(DateTime value) {
		SelectedYearMonthString = value.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	}

	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		ClientLib.Exit(this);
	}

	[RelayCommand]
	void Init() { }

	[RelayCommand]
	void SelectShopCodeFrom() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code");
		if (tokui == null) return;
		ShopCodeFrom = tokui.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectShopCodeTo() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code");
		if (tokui == null) return;
		ShopCodeTo = tokui.Code ?? string.Empty;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoOutputPdf(CancellationToken ct) {
		if (!TryApplySelectedYearMonth()) return;
		ct.ThrowIfCancellationRequested();

		try {
			ClientLib.Cursor2Wait();
			var csvData = await BuildPrintCsvDataAsync(ct);
			if (string.IsNullOrEmpty(csvData)) {
				Message = "印刷データが作成できませんでした";
				return;
			}
			await RunPrintPdfAsync("ShopBudgetReport.qfm", new PrintByCsvParam(csvData), null, ct);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	async Task<string> BuildPrintCsvDataAsync(CancellationToken ct) {
		var (dateFrom, dateTo) = GetDateRange();
		var daysInMonth = DateTime.DaysInMonth(SelectedYearMonth.Year, SelectedYearMonth.Month);
		var yearMonthLabel = SelectedYearMonth.ToString("yy年MM月", CultureInfo.InvariantCulture);

		var shops = await GetShopsAsync(ct);
		if (shops.Count == 0) {
			MessageEx.ShowWarningDialog("対象店舗がありません。", owner: ClientLib.GetActiveView(this));
			return string.Empty;
		}

		var shopIds = shops.Select(s => s.Id).ToList();
		var shopMap = shops.ToDictionary(s => s.Id, s => (Code: s.Code ?? string.Empty, Name: s.Name ?? string.Empty));

		var budgetRows = await GetBudgetAsync(dateFrom, dateTo, shopIds, ct);
		var salesRows = await GetSalesAsync(dateFrom, dateTo, shopIds, ct);

		var (prevDateFrom, prevDateTo) = GetPrevYearDateRange();
		var prevSalesRows = await GetSalesAsync(prevDateFrom, prevDateTo, shopIds, ct);

		var budgetByShopDay = budgetRows
			.GroupBy(r => (r.Id_Tenpo, r.DenDay))
			.ToDictionary(g => g.Key, g => g.Sum(r => r.UriYosan));

		var salesByShopDay = salesRows
			.GroupBy(r => (r.Id_Tenpo, r.DenDay))
			.ToDictionary(g => g.Key, g => g.Sum(r => (long)r.KingakuTotal));

		var prevSalesByShopDay = prevSalesRows
			.GroupBy(r => (r.Id_Tenpo, r.DenDay))
			.ToDictionary(g => g.Key, g => g.Sum(r => (long)r.KingakuTotal));

		var lines = new List<string>();
		var stores = IsByShop
			? shops.Select(s => (Id: s.Id, Code: s.Code ?? string.Empty, Name: s.Name ?? string.Empty)).ToList()
			: new List<(long Id, string Code, string Name)> { (0, string.Empty, "全店") };

		foreach (var store in stores) {
			long cumBudget = 0;
			long cumSales = 0;
			long cumPrevSales = 0;

			for (int day = 1; day <= daysInMonth; day++) {
				var date = new DateTime(SelectedYearMonth.Year, SelectedYearMonth.Month, day);
				var dayStr = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
				var dayOfWeekStr = GetDayOfWeekString(date);

				long budget = 0;
				long sales = 0;
				long prevSales = 0;

				if (IsByShop) {
					budgetByShopDay.TryGetValue((store.Id, dayStr), out budget);
					salesByShopDay.TryGetValue((store.Id, dayStr), out sales);
					var prevDayStr = GetPrevYearDayStr(dayStr);
					prevSalesByShopDay.TryGetValue((store.Id, prevDayStr), out prevSales);
				}
				else {
					budget = shopIds.Sum(id => {
						budgetByShopDay.TryGetValue((id, dayStr), out var v);
						return v;
					});
					sales = shopIds.Sum(id => {
						salesByShopDay.TryGetValue((id, dayStr), out var v);
						return v;
					});
					var prevDayStr = GetPrevYearDayStr(dayStr);
					prevSales = shopIds.Sum(id => {
						prevSalesByShopDay.TryGetValue((id, prevDayStr), out var v);
						return v;
					});
				}

				cumBudget += budget;
				cumSales += sales;
				cumPrevSales += prevSales;

				var budgetK = budget / 1000;
				var cumBudgetK = cumBudget / 1000;
				var prevRatio = prevSales != 0 ? (double)sales / prevSales * 100 : 0;
				var budgetDiff = sales - budget;
				var budgetRatio = budget != 0 ? (double)sales / budget * 100 : 0;

				var fields = new string[] {
					store.Code,
					store.Name,
					yearMonthLabel,
					day.ToString("00", CultureInfo.InvariantCulture),
					dayOfWeekStr,
					budgetK.ToString(CultureInfo.InvariantCulture),
					cumBudgetK.ToString(CultureInfo.InvariantCulture),
					sales.ToString(CultureInfo.InvariantCulture),
					cumSales.ToString(CultureInfo.InvariantCulture),
					prevSales.ToString(CultureInfo.InvariantCulture),
					cumPrevSales.ToString(CultureInfo.InvariantCulture),
					prevRatio.ToString("F1", CultureInfo.InvariantCulture),
					budgetDiff.ToString(CultureInfo.InvariantCulture),
					budgetRatio.ToString("F1", CultureInfo.InvariantCulture),
					"0"
				};

				lines.Add(string.Join(",", fields.Select(EscapeCsvField)));
			}
		}

		return string.Join("\r\n", lines) + "\r\n";
	}

	async Task<List<MasterTokui>> GetShopsAsync(CancellationToken ct) {
		var where = "TenType=6";
		if (!string.IsNullOrWhiteSpace(ShopCodeFrom)) {
			where += $" AND Code >= '{EscapeSqlLiteral(ShopCodeFrom)}'";
		}
		if (!string.IsNullOrWhiteSpace(ShopCodeTo)) {
			where += $" AND Code <= '{EscapeSqlLiteral(ShopCodeTo)}'";
		}
		var param = new QueryListParam(typeof(MasterTokui), where, "Code");
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var reply = await SendMessageAsync(msg, ct);
		var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
		return list?.Cast<MasterTokui>().ToList() ?? [];
	}

	async Task<List<MasterYosanBrand>> GetBudgetAsync(string dateFrom, string dateTo, List<long> shopIds, CancellationToken ct) {
		if (shopIds.Count == 0) return [];
		var idList = string.Join(",", shopIds);
		var where = $"DenDay >= '{dateFrom}' AND DenDay <= '{dateTo}' AND Id_Tenpo IN ({idList})";
		var param = new QueryListParam(typeof(MasterYosanBrand), where, "DenDay, Id_Tenpo");
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var reply = await SendMessageAsync(msg, ct);
		var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
		return list?.Cast<MasterYosanBrand>().ToList() ?? [];
	}

	async Task<List<Tran01Tenuri>> GetSalesAsync(string dateFrom, string dateTo, List<long> shopIds, CancellationToken ct) {
		if (shopIds.Count == 0) return [];
		var idList = string.Join(",", shopIds);
		var where = $"DenDay >= '{dateFrom}' AND DenDay <= '{dateTo}' AND Id_Tenpo IN ({idList})";
		var param = new QueryListParam(typeof(Tran01Tenuri), where, "DenDay, Id_Tenpo");
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var reply = await SendMessageAsync(msg, ct);
		var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
		return list?.Cast<Tran01Tenuri>().ToList() ?? [];
	}

	(string dateFrom, string dateTo) GetDateRange() {
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;
		var days = DateTime.DaysInMonth(year, month);
		return (
			new DateTime(year, month, 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			new DateTime(year, month, days).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
		);
	}

	(string dateFrom, string dateTo) GetPrevYearDateRange() {
		var year = SelectedYearMonth.Year;
		var month = SelectedYearMonth.Month;
		var days = DateTime.DaysInMonth(year, month);
		var prevYearMonth = SelectedYearMonth.AddMonths(-12);
		var prevDays = DateTime.DaysInMonth(prevYearMonth.Year, prevYearMonth.Month);
		var toDay = Math.Min(days, prevDays);
		return (
			new DateTime(prevYearMonth.Year, prevYearMonth.Month, 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			new DateTime(prevYearMonth.Year, prevYearMonth.Month, toDay).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
		);
	}

	string GetPrevYearDayStr(string dayStr) {
		if (!DateTime.TryParseExact(dayStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var current)) {
			return dayStr;
		}
		var baseDate = current.AddMonths(-12);
		if (!IsDateComparison) {
			var diff = current.DayOfWeek - baseDate.DayOfWeek;
			baseDate = baseDate.AddDays(diff);
		}
		return baseDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
	}

	static string GetDayOfWeekString(DateTime date) => date.DayOfWeek switch {
		DayOfWeek.Monday => "月",
		DayOfWeek.Tuesday => "火",
		DayOfWeek.Wednesday => "水",
		DayOfWeek.Thursday => "木",
		DayOfWeek.Friday => "金",
		DayOfWeek.Saturday => "土",
		DayOfWeek.Sunday => "日",
		_ => ""
	};

	static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

	static string EscapeCsvField(string? value) {
		var text = value ?? string.Empty;
		if (text.Contains('"')) {
			text = text.Replace("\"", "\"\"");
		}
		return text.IndexOfAny([',', '\"', '\r', '\n']) >= 0
			? $"\"{text}\""
			: text;
	}

	async Task<CvMsg> SendMessageAsync(CvMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct));
	}

	TResult? ShowSelectDialog<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not Sub.SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	bool TryApplySelectedYearMonth() {
		var value = SelectedYearMonthString.Trim();
		var formats = new[] { "yyyy/MM", "yyyy/M", "yyyyMM", "yyyy-MM", "yyyy-M" };
		if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
			MessageEx.ShowWarningDialog("年月は yyyy/MM 形式で入力してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		SelectedYearMonth = new DateTime(parsed.Year, parsed.Month, 1);
		SelectedYearMonthString = SelectedYearMonth.ToString("yyyy/MM", CultureInfo.InvariantCulture);
		return true;
	}

	async Task RunPrintPdfAsync(string? formFile, PrintByCsvParam? csvParam, QueryListSqlParam? sqlParam, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(formFile)) {
			Message = "印刷フォームファイルが設定されていません";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		if (csvParam is null && sqlParam is null) {
			Message = "印刷データが設定されていません";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		if (csvParam is not null && sqlParam is not null) {
			Message = "印刷データは CSV と SQL のどちらか一方だけ設定してください";
			MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
			return;
		}

		try {
			var param = (object?)csvParam ?? sqlParam!;
			var dataType = csvParam is not null ? typeof(PrintByCsvParam) : typeof(QueryListSqlParam);
			var msg = new PrintOperation {
				DataType = dataType,
				DataMsg = Common.SerializeObject(param),
				FormFile = formFile,
			};

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			string? pdfdata = null;
			await foreach (var streamMsg in coreService.PrintPdfAsync(msg, AppGlobal.GetDefaultCallContext(ct))) {
				ct.ThrowIfCancellationRequested();
				Message = string.Join(" ", new[] { streamMsg.StatusString, streamMsg.DataMsg }.Where(s => !string.IsNullOrWhiteSpace(s)));
				if (streamMsg.Status == -2) {
					Message = streamMsg.DataMsg;
					MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
					return;
				}
				if (streamMsg.Status < 0) {
					var errorDetail = string.IsNullOrWhiteSpace(streamMsg.DataMsg) ? streamMsg.StatusString : streamMsg.DataMsg;
					Message = $"PDF出力失敗: {errorDetail}";
					MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
					return;
				}

				if (streamMsg.IsCompleted) {
					pdfdata = streamMsg.DataMsg;
					break;
				}
			}

			if (string.IsNullOrWhiteSpace(pdfdata)) {
				Message = "PDF出力結果が取得できませんでした";
				MessageEx.ShowWarningDialog(Message, owner: ClientLib.GetActiveView(this));
				return;
			}

			var viewTitle = string.IsNullOrWhiteSpace(ClientLib.GetActiveView(this)?.Title)
				? "PDF表示"
				: $"{ClientLib.GetActiveView(this)?.Title} - PDF表示";
			var view = new Views.Sub.WebPdfView { Title = viewTitle };
			if (view.DataContext is not WebPdfViewModel vm) {
				Message = "PDF表示画面の初期化に失敗しました";
				MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
				return;
			}

			vm.Pdfdata = $"{AppGlobal.Url}/wrk/{pdfdata}";
			view.Title += " " + vm.Pdfdata;
			ClientLib.ShowDialogView(view, this, IsDialog: false);
			view.Owner = null;
			Message = $"PDFを表示しました: {pdfdata}";
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			Message = "PDF出力をキャンセルしました";
			return;
		}
		catch (Exception ex) {
			Message = $"PDF出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
	}
}
