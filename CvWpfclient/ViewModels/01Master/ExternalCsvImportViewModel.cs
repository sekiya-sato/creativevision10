using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;

namespace CvWpfclient.ViewModels._01Master;

public partial class ExternalCsvImportViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial string FilePath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TableName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ModelName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int DataRowCount { get; set; }

	[ObservableProperty]
	public partial int ImportableRowCount { get; set; }

	[ObservableProperty]
	public partial int ErrorCount { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<ExternalCsvPreviewRow> PreviewRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<ExternalCsvImportErrorRow> ErrorRows { get; set; } = [];

	[ObservableProperty]
	public partial ExternalCsvImportErrorRow? SelectedError { get; set; }

	private readonly Dictionary<string, Type> tableTypeMap = CsvImportEngine.CreateTableTypeMap();
	private readonly CsvImportMasterResolver masterResolver;
	private readonly List<object> importRecords = [];
	private Type? importType;
	private List<CsvImportColumnSpec> columnSpecs = [];

	public ExternalCsvImportViewModel() {
		masterResolver = new CsvImportMasterResolver(tableTypeMap);
	}

	[RelayCommand]
	private void Init() {
		Message = "取込レイアウトCSVを選択してください。";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SelectFileAsync(CancellationToken ct) {
		var dialog = new OpenFileDialog {
			Title = "取込レイアウトCSVを選択",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false
		};
		if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true) {
			return;
		}

		FilePath = dialog.FileName;
		await ValidateFileAsync(ct);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ValidateFileAsync(CancellationToken ct) {
		ClearImportState();
		if (string.IsNullOrWhiteSpace(FilePath)) {
			AddError(0, "", "ファイルを選択してください。");
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			var rows = await CsvImportEngine.ReadCsvRowsAsync(FilePath, ct);
			ct.ThrowIfCancellationRequested();
			await BuildImportRecordsAsync(rows, ct);
			RefreshSummary();
			Message = ErrorCount == 0
				? $"{ImportableRowCount:N0} 件を取込できます。"
				: $"{ErrorCount:N0} 件のエラーがあります。行番号と内容を確認してください。";
		}
		catch (OperationCanceledException) {
			Message = "検証をキャンセルしました。";
		}
		catch (Exception ex) {
			AddError(0, "", ex.Message);
			RefreshSummary();
			Message = "検証に失敗しました。";
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ImportAsync(CancellationToken ct) {
		if (importType == null || importRecords.Count == 0) {
			MessageEx.ShowWarningDialog("取込可能なデータがありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (ErrorRows.Count > 0) {
			MessageEx.ShowWarningDialog("エラーが残っています。エラーを修正してから再検証してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{ModelName} を {importRecords.Count:N0} 件登録しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(InsertBulkParam),
				DataMsg = Common.SerializeObject(new InsertBulkParam(importType, JsonConvert.SerializeObject(importRecords)))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				MessageEx.ShowErrorDialog($"CSV取込エラー: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			Message = $"{importRecords.Count:N0} 件を登録しました。";
			MessageEx.ShowInformationDialog("CSV取込が完了しました。", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "取込をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"CSV取込失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	private void ClearImportState() {
		TableName = string.Empty;
		ModelName = string.Empty;
		DataRowCount = 0;
		ImportableRowCount = 0;
		ErrorCount = 0;
		PreviewRows = [];
		ErrorRows = [];
		importRecords.Clear();
		masterResolver.ClearCache();
		columnSpecs = [];
		importType = null;
	}

	private async Task BuildImportRecordsAsync(IReadOnlyList<CsvTextRow> rows, CancellationToken ct) {
		if (rows.Count < 3) {
			AddError(0, "", "CSVは最低3行（テーブル行、列名行、型行）が必要です。");
			return;
		}

		var tableRow = rows[0];
		if (tableRow.Fields.Count == 0 || string.IsNullOrWhiteSpace(tableRow.Fields[0])) {
			AddError(tableRow.LineNo, "Table名", "1行目1列目にテーブル名がありません。");
			return;
		}

		TableName = tableRow.Fields[0].Trim();
		if (!tableTypeMap.TryGetValue(CsvImportEngine.NormalizeTableKey(TableName), out var modelType)) {
			AddError(tableRow.LineNo, "Table名", $"対応するモデル定義が見つかりません: {TableName}");
			return;
		}

		importType = modelType;
		ModelName = modelType.Name;
		columnSpecs = CsvImportEngine.BuildColumnSpecs(rows[1], rows[2], tableRow, modelType, AddError);
		if (columnSpecs.Count == 0) {
			AddError(rows[1].LineNo, "", "取込対象列がありません。");
		}
		if (ErrorRows.Count > 0) {
			return;
		}

		DataRowCount = Math.Max(0, rows.Count - 3);
		for (var index = 3; index < rows.Count; index++) {
			ct.ThrowIfCancellationRequested();
			var row = rows[index];
			if (row.Fields.All(string.IsNullOrWhiteSpace)) {
				continue;
			}

			var item = Activator.CreateInstance(modelType);
			if (item == null) {
				AddError(row.LineNo, "", $"{modelType.Name} のインスタンス作成に失敗しました。");
				continue;
			}

			var beforeErrorCount = ErrorRows.Count;
			await ApplyRowAsync(item, row, ct);
			if (ErrorRows.Count == beforeErrorCount) {
				importRecords.Add(item);
				if (PreviewRows.Count < 100) {
					PreviewRows.Add(new ExternalCsvPreviewRow {
						LineNo = row.LineNo,
						Status = "取込可",
						Key = GetPreviewKey(item),
						Summary = BuildPreviewSummary(item)
					});
				}
			}
		}
	}

	private async Task ApplyRowAsync(object item, CsvTextRow row, CancellationToken ct) {
		foreach (var spec in columnSpecs) {
			ct.ThrowIfCancellationRequested();
			var value = spec.ColumnIndex < row.Fields.Count ? row.Fields[spec.ColumnIndex] : string.Empty;
			if (spec.Property.Name is "Id" or "Vdc" or "Vdu") {
				continue;
			}

			try {
				if (CsvImportEngine.IsForeignCodeProperty(spec.Property)) {
					await masterResolver.ApplyForeignCodeAsync(item, spec, value, ct);
				}
				else {
					var converted = CsvImportEngine.ConvertFieldValue(value, spec);
					spec.Property.SetValue(item, converted);
				}
			}
			catch (InvalidDataException ex) {
				AddError(row.LineNo, spec.ColumnName, ex.Message);
			}
			catch (Exception ex) {
				AddError(row.LineNo, spec.ColumnName, $"値の設定に失敗しました: {ex.Message}");
			}
		}
	}

	private void AddError(int lineNo, string columnName, string detail) {
		ErrorRows.Add(new ExternalCsvImportErrorRow {
			LineNo = lineNo,
			ColumnName = columnName,
			Detail = detail
		});
	}

	private void RefreshSummary() {
		ErrorCount = ErrorRows.Count;
		ImportableRowCount = importRecords.Count;
	}

	private static string GetPreviewKey(object item) {
		var code = item.GetType().GetProperty("Code")?.GetValue(item)?.ToString();
		if (!string.IsNullOrWhiteSpace(code)) {
			return code;
		}
		return item.GetType().GetProperty("Id")?.GetValue(item)?.ToString() ?? string.Empty;
	}

	private static string BuildPreviewSummary(object item) {
		var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
		if (!string.IsNullOrWhiteSpace(name)) {
			return name;
		}
		var ryaku = item.GetType().GetProperty("Ryaku")?.GetValue(item)?.ToString();
		return ryaku ?? string.Empty;
	}
}

public sealed class ExternalCsvPreviewRow {
	public int LineNo { get; init; }
	public string Status { get; init; } = string.Empty;
	public string Key { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
}

public sealed class ExternalCsvImportErrorRow {
	public int LineNo { get; init; }
	public string ColumnName { get; init; } = string.Empty;
	public string Detail { get; init; } = string.Empty;
}
