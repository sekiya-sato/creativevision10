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
using System.Text;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>残高区分の選択肢。</summary>
public sealed record BalanceKindOption(EnumOpeningBalanceKind Kind, string Name);

/// <summary>締日の選択肢。</summary>
public sealed record BalanceShimeOption(int Value, string Name);

/// <summary>取込プレビューの1行。</summary>
public sealed class BalanceRegistrationPreviewRow {
	public int LineNo { get; init; }
	public string Status { get; init; } = string.Empty;
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public long Amount { get; init; }
	public long BreakdownTotal { get; init; }
	public long Balance { get; init; }
	public string Note { get; init; } = string.Empty;
}

/// <summary>検証結果の1行。警告も同じグリッドに出す。</summary>
public sealed class BalanceRegistrationErrorRow {
	public string Kind { get; init; } = string.Empty;
	public int LineNo { get; init; }
	public string ColumnName { get; init; } = string.Empty;
	public string Detail { get; init; } = string.Empty;
	public bool IsWarning { get; init; }
}

/// <summary>
/// 残高登録処理。移行時の期首売掛残・請求残・買掛残・支払残を、期首日より前の年月／年月日を持つ
/// <c>Summary*</c> 行として投入する。
/// <para>
/// ①期首情報 → ②テンプレートCSV出力 → ③CSV読込と登録 の順に操作する。CSVの解析・検証・行生成は
/// <see cref="OpeningBalanceCsv"/>（CvBase の純ロジック）、登録は <c>OpeningBalanceImportParam</c> 経由で
/// サーバー側の1トランザクションに任せる。
/// </para>
/// <para>
/// 繰越の引き継ぎ方は売掛・買掛が <c>Balance</c> 列、請求・支払が <c>TotalIn-TotalSales</c> の合計差と
/// 異なるため、期首行では双方を矛盾なく埋める。仕様は
/// `Doc/spec/2026-08-21_残高登録処理_詳細設計.md` を参照する。
/// </para>
/// </summary>
public partial class BalanceRegistrationViewModel : Helpers.BaseViewModel {
	public IReadOnlyList<BalanceKindOption> KindItems { get; } = [
		new(EnumOpeningBalanceKind.UriKake, "売掛"),
		new(EnumOpeningBalanceKind.UriSei, "請求"),
		new(EnumOpeningBalanceKind.KaiKake, "買掛"),
		new(EnumOpeningBalanceKind.KaiShi, "支払"),
	];

	// ---- ① 期首情報 ------------------------------------------------------------

	[ObservableProperty]
	public partial string FiscalStartDate { get; set; } = OpeningBalanceCsv.UnsetFiscalStartDate;

	[ObservableProperty]
	public partial string FiscalStartDateText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsFiscalStartUnset { get; set; } = true;

	[ObservableProperty]
	public partial EnumOpeningBalanceKind SelectedKind { get; set; } = EnumOpeningBalanceKind.UriKake;

	[ObservableProperty]
	public partial ObservableCollection<BalanceShimeOption> ShimeItems { get; set; } = [];

	[ObservableProperty]
	public partial int SelectedShime { get; set; }

	[ObservableProperty]
	public partial bool IsClosingBased { get; set; }

	/// <summary>期首残のキー日付。売掛・買掛は yyyy/MM、請求・支払は yyyy/MM/dd。</summary>
	[ObservableProperty]
	public partial string KeyDateText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string KeyDateLabel { get; set; } = "期首残の年月";

	[ObservableProperty]
	public partial string KeyDateGuide { get; set; } = string.Empty;

	// ---- ② テンプレート --------------------------------------------------------

	[ObservableProperty]
	public partial string OwnerCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string OwnerCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string OwnerLabel { get; set; } = "得意先";

	[ObservableProperty]
	public partial int TargetCount { get; set; }

	[ObservableProperty]
	public partial int ExistingCount { get; set; }

	/// <summary>true=全取引先（残高欄は空）／false=既存の期首残がある取引先のみ。</summary>
	[ObservableProperty]
	public partial bool IsTemplateAllOwners { get; set; } = true;

	[ObservableProperty]
	public partial bool IncludeBreakdown { get; set; }

	// ---- ③ 取込 ----------------------------------------------------------------

	[ObservableProperty]
	public partial string FilePath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string FormatName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int DataRowCount { get; set; }

	[ObservableProperty]
	public partial int NewCount { get; set; }

	[ObservableProperty]
	public partial int OverwriteCount { get; set; }

	[ObservableProperty]
	public partial int DeleteCount { get; set; }

	[ObservableProperty]
	public partial int SkipCount { get; set; }

	[ObservableProperty]
	public partial int ErrorCount { get; set; }

	[ObservableProperty]
	public partial int WarningCount { get; set; }

	[ObservableProperty]
	public partial long TotalAmount { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<BalanceRegistrationPreviewRow> PreviewRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<BalanceRegistrationErrorRow> ErrorRows { get; set; } = [];

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool CanRegister { get; set; }

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	/// <summary>直近の検証で確定した登録内容。登録実行はこれだけを送る。</summary>
	private OpeningBalanceBuildResult? buildResult;
	private string validatedKeyDate = string.Empty;

	private OpeningBalanceKindSpec Spec => OpeningBalanceCsv.GetSpec(SelectedKind);

	// ---- 初期化と条件変更 ------------------------------------------------------

	[RelayCommand]
	private async Task InitAsync(CancellationToken ct) {
		try {
			var sysman = await AppGlobal.LogicGetSysman();
			FiscalStartDate = string.IsNullOrWhiteSpace(sysman.FiscalStartDate)
				? OpeningBalanceCsv.UnsetFiscalStartDate
				: sysman.FiscalStartDate;
			FiscalStartDateText = OpeningBalanceCsv.FormatDate(FiscalStartDate);
			IsFiscalStartUnset = FiscalStartDate == OpeningBalanceCsv.UnsetFiscalStartDate;
			if (IsFiscalStartUnset) {
				Message = "期首日が未設定です。システム管理マスタで期首年月日を設定してください。";
				return;
			}
			await ApplyKindAsync(ct);
		}
		catch (OperationCanceledException) {
			Message = "初期化をキャンセルしました。";
		}
		catch (Exception ex) {
			Message = $"初期化に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
	}

	partial void OnSelectedKindChanged(EnumOpeningBalanceKind value) {
		if (IsFiscalStartUnset) {
			return;
		}
		_ = ApplySafeAsync(ct => ApplyKindAsync(ct));
	}

	partial void OnSelectedShimeChanged(int value) {
		if (IsFiscalStartUnset || !IsClosingBased || value == 0) {
			return;
		}
		ApplyDefaultKeyDate();
		_ = ApplySafeAsync(ReloadTargetCountAsync);
	}

	partial void OnKeyDateTextChanged(string value) {
		ClearImportState();
		UpdateKeyDateGuide();
	}

	/// <summary>区分の切替。締日一覧・キー日付・対象件数をまとめて作り直す。</summary>
	private async Task ApplyKindAsync(CancellationToken ct) {
		var spec = Spec;
		IsClosingBased = spec.IsClosingBased;
		OwnerLabel = spec.OwnerLabel;
		KeyDateLabel = spec.KeyLabel;
		ClearImportState();

		if (spec.IsClosingBased) {
			await LoadShimeItemsAsync(ct);
		}
		else {
			ShimeItems = [];
			SelectedShime = 0;
		}
		ApplyDefaultKeyDate();
		await ReloadTargetCountAsync(ct);
	}

	private async Task LoadShimeItemsAsync(CancellationToken ct) {
		var sql = $"SELECT DISTINCT Shime1 FROM {Spec.MasterTableName} " +
			"WHERE Shime1 BETWEEN 1 AND 31 OR Shime1 = 99 ORDER BY Shime1";
		var rows = await QuerySqlListAsync<SummaryClosingCheckRow>(sql, [], ct);
		ShimeItems = new ObservableCollection<BalanceShimeOption>(rows
			.Select(x => new BalanceShimeOption(x.Shime1, OpeningBalanceCsv.FormatShime(x.Shime1))));
		SelectedShime = ShimeItems.FirstOrDefault()?.Value ?? 0;
		if (ShimeItems.Count == 0) {
			Message = $"{Spec.OwnerLabel}マスタに有効な締日がありません。";
		}
	}

	private void ApplyDefaultKeyDate() {
		var (keyDate, _) = OpeningBalanceCsv.GetDefaultKeyDate(SelectedKind, FiscalStartDate, SelectedShime);
		KeyDateText = OpeningBalanceCsv.FormatDate(keyDate);
		UpdateKeyDateGuide();
	}

	private void UpdateKeyDateGuide() {
		if (!TryGetKeyDate(out var keyDate, out var error)) {
			KeyDateGuide = error;
			return;
		}
		KeyDateGuide = $"{OpeningBalanceCsv.FormatDate(keyDate)} の行として登録します。" +
			$"期首({FiscalStartDateText})以降の再計算では上書きされません。";
	}

	/// <summary>画面のキー日付を yyyyMM / yyyyMMdd へ正規化する。</summary>
	private bool TryGetKeyDate(out string keyDate, out string error) {
		keyDate = string.Empty;
		error = string.Empty;
		var spec = Spec;
		var text = OpeningBalanceCsv.ToHalfWidth(KeyDateText ?? string.Empty)
			.Replace("/", string.Empty).Replace("-", string.Empty).Trim();
		var format = spec.IsClosingBased ? "yyyyMMdd" : "yyyyMM";
		if (text.Length != spec.KeyLength
			|| !DateTime.TryParseExact(spec.IsClosingBased ? text : text + "01", "yyyyMMdd",
				CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			error = $"{spec.KeyLabel}は {format.Replace("yyyy", "yyyy/").Replace("MMdd", "MM/dd")} 形式で入力してください。";
			return false;
		}
		if (!OpeningBalanceCsv.IsBeforeFiscalStart(text, FiscalStartDate, spec)) {
			error = $"{spec.KeyLabel} {OpeningBalanceCsv.FormatDate(text)} は期首({FiscalStartDateText})以降です。期首より前を指定してください。";
			return false;
		}
		keyDate = text;
		return true;
	}

	// ---- 対象取引先の照会 ------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ReloadTargetCountAsync(CancellationToken ct) {
		if (IsFiscalStartUnset || !TryGetKeyDate(out var keyDate, out _)) {
			TargetCount = 0;
			ExistingCount = 0;
			return;
		}
		var rows = await LoadOwnerRowsAsync(keyDate, TemplateScope, ct);
		TargetCount = rows.Count;
		ExistingCount = rows.Count(x => x.HasExisting != 0);
	}

	/// <summary>テンプレート出力の絞り込み。実運用の対象集合（倉庫・直営店を除く／選択締日／コード範囲）に合わせる。</summary>
	private EnumOpeningBalanceOwnerScope TemplateScope =>
		EnumOpeningBalanceOwnerScope.OwnerTypeFilter
		| EnumOpeningBalanceOwnerScope.ClosingFilter
		| EnumOpeningBalanceOwnerScope.CodeRange;

	private async Task<List<OpeningBalanceOwnerRow>> LoadOwnerRowsAsync(
		string keyDate, EnumOpeningBalanceOwnerScope scope, CancellationToken ct) {
		var sql = OpeningBalanceCsv.BuildOwnerQuerySql(SelectedKind, scope);
		return await QuerySqlListAsync<OpeningBalanceOwnerRow>(sql,
			[keyDate, OwnerCodeFrom.Trim(), OwnerCodeTo.Trim(), SelectedShime.ToString(CultureInfo.InvariantCulture)], ct);
	}

	// ---- ② テンプレート出力 ---------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExportTemplateAsync(CancellationToken ct) {
		if (!ValidateCondition(out var keyDate)) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			var scope = IsTemplateAllOwners
				? TemplateScope
				: TemplateScope | EnumOpeningBalanceOwnerScope.ExistingOnly;
			var rows = await LoadOwnerRowsAsync(keyDate, scope, ct);
			if (rows.Count == 0) {
				MessageEx.ShowWarningDialog($"出力対象の{Spec.OwnerLabel}がありません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			var dialog = new SaveFileDialog {
				Title = "期首残高テンプレートCSVを保存",
				Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
				DefaultExt = ".csv",
				FileName = $"期首残高_{Spec.DisplayName}_{keyDate}.csv",
			};
			if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true) {
				return;
			}

			var lines = OpeningBalanceCsv.BuildTemplateLines(
				SelectedKind, IncludeBreakdown, FiscalStartDate, keyDate, SelectedShime,
				rows.Select(x => new OpeningBalanceTemplateRow(
					x.Code, x.Name, x.Shime1,
					x.HasExisting != 0 ? x.Amount : 0,
					IncludeBreakdown && x.HasExisting != 0 ? x.ToBreakdown() : null,
					x.DueDay)));

			// Excelで開いたときに日本語が化けないようBOM付きUTF-8で出力する(取込はBOM有無どちらも可)
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
			await File.WriteAllLinesAsync(dialog.FileName, lines, encoding, ct);
			Message = $"{dialog.FileName} を作成しました（{rows.Count:N0} 件）。Excelで期首残高を記入して③で取り込んでください。";
			MessageEx.ShowInformationDialog("テンプレートCSVを作成しました。", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "テンプレート出力をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"テンプレート出力に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	// ---- ③ CSV読込と検証 ------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SelectFileAsync(CancellationToken ct) {
		var dialog = new OpenFileDialog {
			Title = "期首残高CSVを選択",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false,
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
		if (!ValidateCondition(out var keyDate)) {
			return;
		}
		if (string.IsNullOrWhiteSpace(FilePath)) {
			AddError(new OpeningBalanceCsvError { Detail = "CSVファイルを選択してください。" });
			RefreshSummary();
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			IsProcessing = true;
			var text = await CsvImportEngine.ReadCsvTextAsync(FilePath, ct);
			ct.ThrowIfCancellationRequested();

			var parsed = ParseCsv(text, out var formatName);
			FormatName = formatName;
			DataRowCount = parsed.Rows.Count;
			foreach (var error in parsed.Errors) {
				AddError(error);
			}
			if (parsed.HasError) {
				RefreshSummary();
				Message = $"{ErrorCount:N0} 件のエラーがあります。行番号と内容を確認してください。";
				return;
			}

			// コード解決は絞り込み無しで引く。絞ると対象外の取引先が「マスタにありません」という
			// 誤ったエラーになり、TenTypeの警告や締日不一致の指摘が出せなくなる
			var owners = await LoadOwnerRowsAsync(keyDate, EnumOpeningBalanceOwnerScope.All, ct);
			ct.ThrowIfCancellationRequested();

			var (_, dayFrom) = OpeningBalanceCsv.GetDefaultKeyDate(SelectedKind, FiscalStartDate, SelectedShime);
			var result = OpeningBalanceCsv.Build(new OpeningBalanceBuildRequest {
				Kind = SelectedKind,
				KeyDate = keyDate,
				DayFrom = dayFrom,
				FiscalStartDate = FiscalStartDate,
				SelectedShime = SelectedShime,
				Rows = parsed.Rows,
				Owners = owners.ToDictionary(x => x.Code, x => x.ToOwner(), StringComparer.OrdinalIgnoreCase),
				ExistingAmounts = owners.Where(x => x.HasExisting != 0).ToDictionary(x => x.Id, x => x.Amount),
			});

			foreach (var error in result.Errors) {
				AddError(error);
			}
			PreviewRows = new ObservableCollection<BalanceRegistrationPreviewRow>(result.Entries
				.Take(100)
				.Select(x => new BalanceRegistrationPreviewRow {
					LineNo = x.LineNo,
					Status = x.StatusText,
					Code = x.OwnerCode,
					Name = x.OwnerName,
					Amount = x.Amount,
					BreakdownTotal = x.BreakdownTotal,
					Balance = x.Record is null ? 0 : -x.Amount,
					Note = x.Note,
				}));

			buildResult = result;
			validatedKeyDate = keyDate;
			NewCount = result.NewCount;
			OverwriteCount = result.OverwriteCount;
			DeleteCount = result.DeleteCount;
			SkipCount = result.SkipCount;
			TotalAmount = result.TotalAmount;
			RefreshSummary();

			Message = ErrorCount > 0
				? $"{ErrorCount:N0} 件のエラーがあります。行番号と内容を確認してください。"
				: NewCount + OverwriteCount + DeleteCount == 0
					? "登録対象がありません。期首残高が入力されているか確認してください。"
					: $"登録 {NewCount:N0} 件 / 上書き {OverwriteCount:N0} 件 / 削除 {DeleteCount:N0} 件を反映できます。";
		}
		catch (OperationCanceledException) {
			Message = "検証をキャンセルしました。";
		}
		catch (Exception ex) {
			AddError(new OpeningBalanceCsvError { Detail = ex.Message });
			RefreshSummary();
			Message = "検証に失敗しました。";
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 標準形式（日本語1行ヘッダ）と詳細形式（外部CSVマスタ取込と同じ3行ヘッダ）を自動判別して解析する。
	/// </summary>
	private OpeningBalanceCsvParseResult ParseCsv(string text, out string formatName) {
		var layoutResult = TryParseLayoutCsv(text);
		if (layoutResult != null) {
			formatName = "詳細形式（3行ヘッダ）";
			return layoutResult;
		}
		formatName = "標準形式";
		return OpeningBalanceCsv.Parse(text, SelectedKind);
	}

	/// <summary>
	/// 詳細形式かどうかを判定し、そうなら標準形式と同じ行表現へ読み替える。標準形式なら null を返す。
	/// 1行目1列目が選択中の区分の対象テーブル名（旧テーブル名を含む）であることを条件にする。
	/// </summary>
	private OpeningBalanceCsvParseResult? TryParseLayoutCsv(string text) {
		var result = new OpeningBalanceCsvParseResult();
		List<CsvTextRow> rows;
		try {
			rows = CsvText.Parse(text);
		}
		catch (InvalidDataException) {
			return null;
		}
		if (rows.Count < 3 || rows[0].Fields.Count == 0) {
			return null;
		}

		var tableKey = CsvImportEngine.NormalizeTableKey(rows[0].Fields[0]);
		var modelType = SelectedKind switch {
			EnumOpeningBalanceKind.UriKake => typeof(SummaryUriKake),
			EnumOpeningBalanceKind.UriSei => typeof(SummaryUriSei),
			EnumOpeningBalanceKind.KaiKake => typeof(SummaryKaiKake),
			_ => typeof(SummaryKaiShi),
		};
		var accepted = new[] { modelType.Name, CsvImportEngine.NormalizeTableKey(GetOldTableName(modelType)) };
		if (!accepted.Any(x => x.Length > 0 && string.Equals(x, tableKey, StringComparison.OrdinalIgnoreCase))) {
			return null;
		}

		var spec = Spec;
		var columnRow = rows[1];
		var typeRow = rows[2];
		var specs = CsvImportEngine.BuildColumnSpecs(columnRow, typeRow, rows[0], modelType,
			(lineNo, column, detail) => result.Errors.Add(new OpeningBalanceCsvError {
				LineNo = lineNo, ColumnName = column, Detail = detail,
			}));
		if (result.HasError) {
			return result;
		}

		var ownerSpec = specs.FirstOrDefault(x => x.Property.Name == spec.OwnerColumn);
		var balanceSpec = specs.FirstOrDefault(x => x.Property.Name == nameof(SummaryUriKake.Balance));
		if (ownerSpec == null || balanceSpec == null) {
			result.Errors.Add(new OpeningBalanceCsvError {
				LineNo = columnRow.LineNo,
				Detail = $"詳細形式には {spec.OwnerColumn} と Balance の列が必要です。",
			});
			return result;
		}

		for (var index = 3; index < rows.Count; index++) {
			var row = rows[index];
			if (row.Fields.All(string.IsNullOrWhiteSpace)) {
				continue;
			}

			var code = GetLayoutField(row, ownerSpec).Trim();
			if (code.Length == 0) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo, ColumnName = ownerSpec.ColumnName, Detail = "コードが空です。",
				});
				continue;
			}
			// 詳細形式の Balance は内部符号(負=未回収)。標準形式の期首残高(正数)へ反転して合わせる
			if (!OpeningBalanceCsv.TryParseAmount(GetLayoutField(row, balanceSpec), out var balance, out var error)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo, ColumnName = balanceSpec.ColumnName, Detail = error,
				});
				continue;
			}

			var breakdown = new OpeningBalanceBreakdown();
			var failed = false;
			foreach (var (field, propertyName) in GetLayoutBreakdownMap()) {
				var columnSpec = specs.FirstOrDefault(x => x.Property.Name == propertyName);
				if (columnSpec == null) {
					continue;
				}
				if (!OpeningBalanceCsv.TryParseAmount(GetLayoutField(row, columnSpec), out var value, out var breakdownError)) {
					result.Errors.Add(new OpeningBalanceCsvError {
						LineNo = row.LineNo, ColumnName = columnSpec.ColumnName, Detail = breakdownError,
					});
					failed = true;
					continue;
				}
				SetLayoutBreakdown(breakdown, field, value);
			}
			if (failed) {
				continue;
			}

			result.Rows.Add(new OpeningBalanceCsvRow {
				LineNo = row.LineNo,
				Code = code,
				Amount = -balance,
				HasBreakdownColumn = true,
				Breakdown = breakdown,
			});
		}

		if (result.Rows.Count == 0 && !result.HasError) {
			result.Errors.Add(new OpeningBalanceCsvError { Detail = "データ行がありません。" });
		}
		return result;
	}

	private static string GetLayoutField(CsvTextRow row, CsvImportColumnSpec spec) =>
		spec.ColumnIndex < row.Fields.Count ? row.Fields[spec.ColumnIndex] : string.Empty;

	private IEnumerable<(EnumOpeningBalanceField Field, string PropertyName)> GetLayoutBreakdownMap() {
		yield return (EnumOpeningBalanceField.Main,
			Spec.IsPayable ? nameof(SummaryKaiKake.Shiire) : nameof(SummaryUriKake.Uriage));
		yield return (EnumOpeningBalanceField.Henpin, nameof(SummaryUriKake.Henpin));
		yield return (EnumOpeningBalanceField.Nebiki, nameof(SummaryUriKake.Nebiki));
		if (SelectedKind == EnumOpeningBalanceKind.UriSei) {
			yield return (EnumOpeningBalanceField.Sonota, nameof(SummaryUriSei.Sonota));
		}
		yield return (EnumOpeningBalanceField.Tax1, nameof(SummaryUriKake.Tax1));
		yield return (EnumOpeningBalanceField.Tax2, nameof(SummaryUriKake.Tax2));
		yield return (EnumOpeningBalanceField.Tax3, nameof(SummaryUriKake.Tax3));
		yield return (EnumOpeningBalanceField.Cash, nameof(SummaryUriKake.Cash));
		yield return (EnumOpeningBalanceField.Fee, nameof(SummaryUriKake.Fee));
		yield return (EnumOpeningBalanceField.Densai, nameof(SummaryUriKake.Densai));
		yield return (EnumOpeningBalanceField.Offset, nameof(SummaryUriKake.Offset));
		yield return (EnumOpeningBalanceField.Other, nameof(SummaryUriKake.Other));
		yield return (EnumOpeningBalanceField.TaxableAmount1, nameof(SummaryUriKake.TaxableAmount1));
		yield return (EnumOpeningBalanceField.TaxableAmount2, nameof(SummaryUriKake.TaxableAmount2));
		yield return (EnumOpeningBalanceField.TaxableAmount3, nameof(SummaryUriKake.TaxableAmount3));
	}

	private static void SetLayoutBreakdown(OpeningBalanceBreakdown breakdown, EnumOpeningBalanceField field, long value) {
		switch (field) {
			case EnumOpeningBalanceField.Main: breakdown.Main = value; break;
			case EnumOpeningBalanceField.Henpin: breakdown.Henpin = value; break;
			case EnumOpeningBalanceField.Nebiki: breakdown.Nebiki = value; break;
			case EnumOpeningBalanceField.Sonota: breakdown.Sonota = value; break;
			case EnumOpeningBalanceField.Tax1: breakdown.Tax1 = value; break;
			case EnumOpeningBalanceField.Tax2: breakdown.Tax2 = value; break;
			case EnumOpeningBalanceField.Tax3: breakdown.Tax3 = value; break;
			case EnumOpeningBalanceField.Cash: breakdown.Cash = value; break;
			case EnumOpeningBalanceField.Fee: breakdown.Fee = value; break;
			case EnumOpeningBalanceField.Densai: breakdown.Densai = value; break;
			case EnumOpeningBalanceField.Offset: breakdown.Offset = value; break;
			case EnumOpeningBalanceField.Other: breakdown.Other = value; break;
			case EnumOpeningBalanceField.TaxableAmount1: breakdown.TaxableAmount1 = value; break;
			case EnumOpeningBalanceField.TaxableAmount2: breakdown.TaxableAmount2 = value; break;
			case EnumOpeningBalanceField.TaxableAmount3: breakdown.TaxableAmount3 = value; break;
		}
	}

	private static string GetOldTableName(Type type) =>
		type.GetCustomAttributes(typeof(OldTableCommentAttr), false)
			.OfType<OldTableCommentAttr>()
			.Select(x => x.Name)
			.FirstOrDefault() ?? string.Empty;

	// ---- 登録実行 --------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task RegisterAsync(CancellationToken ct) {
		if (buildResult == null || !CanRegister) {
			MessageEx.ShowWarningDialog("登録できる内容がありません。CSVを読み込んで検証してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (!TryGetKeyDate(out var keyDate, out var keyError) || keyDate != validatedKeyDate) {
			MessageEx.ShowWarningDialog(
				keyError.Length > 0 ? keyError : $"{Spec.KeyLabel}が変わりました。再検証(F5)してください。",
				owner: ClientLib.GetActiveView(this));
			return;
		}

		var spec = Spec;
		var confirm = $"""
{spec.DisplayName}の期首残高を登録します。

  {spec.KeyLabel}  {OpeningBalanceCsv.FormatDate(keyDate)} （期首 {FiscalStartDateText} の直前）
  新規登録  {buildResult.NewCount:N0} 件
  上書き    {buildResult.OverwriteCount:N0} 件
  削除      {buildResult.DeleteCount:N0} 件
  合計 期首残高  {buildResult.TotalAmount:N0}

期首より前の行は、以後の再計算で上書きされません。実行しますか？
""";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			IsProcessing = true;
			var param = new OpeningBalanceImportParam(
				spec.TableName, keyDate, buildResult.OwnerIds,
				JsonConvert.SerializeObject(buildResult.Records));
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(OpeningBalanceImportParam),
				DataMsg = Common.SerializeObject(param),
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				Message = $"期首残高の登録に失敗しました: {detail} ({reply.Code})";
				MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
				return;
			}

			var result = Common.DeserializeObject<OpeningBalanceImportResult>(reply.DataMsg ?? "{}");
			Message = $"削除 {result?.Deleted ?? 0:N0} 件 / 登録 {result?.Inserted ?? 0:N0} 件を反映しました。";
			MessageEx.ShowInformationDialog(Message, owner: ClientLib.GetActiveView(this));

			// 登録後の状態（既存行の有無）を反映するため再検証と件数の引き直しを行う
			await ReloadTargetCountAsync(ct);
			await ValidateFileAsync(ct);
		}
		catch (OperationCanceledException) {
			Message = "登録をキャンセルしました。";
		}
		catch (Exception ex) {
			Message = $"期首残高の登録に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	// ---- 共通 ------------------------------------------------------------------

	/// <summary>①の条件が揃っているかを確認する。揃っていなければ理由を表示する。</summary>
	private bool ValidateCondition(out string keyDate) {
		keyDate = string.Empty;
		if (IsFiscalStartUnset) {
			MessageEx.ShowWarningDialog(
				"期首日が未設定です。システム管理マスタで期首年月日を設定してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (IsClosingBased && SelectedShime == 0) {
			MessageEx.ShowWarningDialog("締日を選択してください。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (!TryGetKeyDate(out keyDate, out var error)) {
			MessageEx.ShowWarningDialog(error, owner: ClientLib.GetActiveView(this));
			return false;
		}
		var codeFrom = OwnerCodeFrom.Trim();
		var codeTo = OwnerCodeTo.Trim();
		if (codeFrom.Length > 0 && codeTo.Length > 0 && string.CompareOrdinal(codeFrom, codeTo) > 0) {
			MessageEx.ShowWarningDialog($"{Spec.OwnerLabel}コード範囲の開始と終了が逆です。", owner: ClientLib.GetActiveView(this));
			return false;
		}
		return true;
	}

	private void ClearImportState() {
		PreviewRows = [];
		ErrorRows = [];
		FormatName = string.Empty;
		DataRowCount = 0;
		NewCount = 0;
		OverwriteCount = 0;
		DeleteCount = 0;
		SkipCount = 0;
		ErrorCount = 0;
		WarningCount = 0;
		TotalAmount = 0;
		CanRegister = false;
		buildResult = null;
		validatedKeyDate = string.Empty;
	}

	private void AddError(OpeningBalanceCsvError error) {
		ErrorRows.Add(new BalanceRegistrationErrorRow {
			Kind = error.Kind,
			LineNo = error.LineNo,
			ColumnName = error.ColumnName,
			Detail = error.Detail,
			IsWarning = error.IsWarning,
		});
	}

	private void RefreshSummary() {
		ErrorCount = ErrorRows.Count(x => !x.IsWarning);
		WarningCount = ErrorRows.Count(x => x.IsWarning);
		CanRegister = ErrorCount == 0
			&& buildResult != null
			&& NewCount + OverwriteCount + DeleteCount > 0;
	}

	private async Task ApplySafeAsync(Func<CancellationToken, Task> action) {
		try {
			await action(CancellationToken.None);
		}
		catch (Exception ex) {
			Message = $"対象{Spec.OwnerLabel}の取得に失敗しました: {ex.Message}";
		}
	}

	private static async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list
			? list.Cast<T>().ToList()
			: [];
	}
}
