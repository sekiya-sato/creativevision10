using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// 外部CSVマスタ更新。<see cref="ExternalCsvImportViewModel"/> と同じ取込レイアウトCSVを使うが、
/// 新規登録は行わず、モデルのユニークキー（<see cref="CvBase.KeyDmlAttribute"/> の <c>IsUnique=true</c>）で
/// 既存行を検索して見つかった場合のみ、CSVにある列だけを上書き更新する。
/// </summary>
public partial class ExternalCsvUpdateViewModel : Helpers.BaseViewModel {
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
	private string[] uniqueKeyColumns = [];

	public ExternalCsvUpdateViewModel() {
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
				? $"{ImportableRowCount:N0} 件を更新できます。"
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
			MessageEx.ShowWarningDialog("更新可能なデータがありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (ErrorRows.Count > 0) {
			MessageEx.ShowWarningDialog("エラーが残っています。エラーを修正してから再検証してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{ModelName} を {importRecords.Count:N0} 件更新しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var updated = 0;
			var failed = 0;
			foreach (var item in importRecords) {
				ct.ThrowIfCancellationRequested();
				var msg = new CvMsg {
					Code = 0,
					Flag = CvFlag.Msg201_Op_Execute,
					DataType = typeof(UpdateParam),
					DataMsg = Common.SerializeObject(new UpdateParam(importType, Common.SerializeObject(item)))
				};
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
				if (reply.Code < 0) {
					failed++;
					var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
					AddError(0, GetPreviewKey(item), $"更新エラー: {detail} ({reply.Code})");
					continue;
				}
				updated++;
			}

			RefreshSummary();
			Message = failed == 0
				? $"{updated:N0} 件を更新しました。"
				: $"{updated:N0} 件を更新しました。{failed:N0} 件は失敗しました。";
			MessageEx.ShowInformationDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "更新をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"CSV更新失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
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
		uniqueKeyColumns = [];
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

		uniqueKeyColumns = CsvImportEngine.GetUniqueKeyColumnNames(modelType) ?? [];
		if (uniqueKeyColumns.Length == 0) {
			AddError(tableRow.LineNo, "Table名", $"{modelType.Name} にはユニークキーが定義されていないため更新できません。");
		}
		else {
			var missingKeyColumns = uniqueKeyColumns.Where(k => !columnSpecs.Any(s => string.Equals(s.Property.Name, k, StringComparison.OrdinalIgnoreCase))).ToList();
			if (missingKeyColumns.Count > 0) {
				AddError(rows[1].LineNo, "", $"更新キー列がCSVに含まれていません: {string.Join(",", missingKeyColumns)}");
			}
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
			if (ErrorRows.Count != beforeErrorCount) {
				continue;
			}

			var merged = await FindAndMergeExistingAsync(modelType, item, row.LineNo, ct);
			if (merged == null) {
				continue;
			}

			importRecords.Add(merged);
			if (PreviewRows.Count < 100) {
				PreviewRows.Add(new ExternalCsvPreviewRow {
					LineNo = row.LineNo,
					Status = "更新可",
					Key = GetPreviewKey(merged),
					Summary = BuildPreviewSummary(merged)
				});
			}
		}
	}

	/// <summary>
	/// ユニークキーで既存行を検索し、見つかった場合はCSVの列仕様ぶんだけ既存インスタンスへ上書きして返す。
	/// 0件・複数件・通信エラーはすべて行エラーとして記録し <c>null</c> を返す（新規登録は行わない）。
	/// </summary>
	private async Task<object?> FindAndMergeExistingAsync(Type modelType, object item, int lineNo, CancellationToken ct) {
		var keyDesc = string.Join(",", uniqueKeyColumns.Select(k => $"{k}={modelType.GetProperty(k)?.GetValue(item)}"));
		try {
			var (where, parameters) = BuildKeyWhereClause(modelType, item);
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var query = new QueryListParam(modelType, where, null, parameters, maxCount: 2);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(query)
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0 && reply.Code != -1) {
				var detail = string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option;
				AddError(lineNo, "", $"検索に失敗しました: {detail} ({reply.Code})");
				return null;
			}

			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
			if (list == null || list.Count == 0) {
				AddError(lineNo, "", $"更新対象が見つかりません（キー: {keyDesc}）");
				return null;
			}
			if (list.Count > 1) {
				AddError(lineNo, "", $"キーが一意ではありません（キー: {keyDesc}）");
				return null;
			}

			var existing = list[0]!;
			foreach (var spec in columnSpecs) {
				if (spec.Property.Name is "Id" or "Vdc" or "Vdu") {
					continue;
				}
				spec.Property.SetValue(existing, spec.Property.GetValue(item));
			}
			return existing;
		}
		catch (Exception ex) {
			AddError(lineNo, "", $"検索に失敗しました: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// ユニークキー列の値からWHERE句を組み立てる。数値列はSQLへ直接埋め込む
	/// （<see cref="Helpers.BaseMatchingViewModel"/> と同じ理由。SQLiteは動的型のため、
	/// 数値列を文字列パラメータと比較すると一致しない）。値は取込時に数値検証済みなので埋め込み安全。
	/// 文字列列はバインドパラメータにする。
	/// </summary>
	private (string where, string[] parameters) BuildKeyWhereClause(Type modelType, object item) {
		var conditions = new List<string>();
		var parameters = new List<string>();
		foreach (var keyColumn in uniqueKeyColumns) {
			var property = modelType.GetProperty(keyColumn) ?? throw new InvalidDataException($"更新キー列が見つかりません: {keyColumn}");
			var value = property.GetValue(item);
			if (value is long or int or short or byte) {
				conditions.Add($"{keyColumn}={Convert.ToInt64(value, CultureInfo.InvariantCulture)}");
			}
			else {
				conditions.Add($"{keyColumn}=@{parameters.Count}");
				parameters.Add(value?.ToString() ?? string.Empty);
			}
		}
		return (string.Join(" and ", conditions), [.. parameters]);
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
