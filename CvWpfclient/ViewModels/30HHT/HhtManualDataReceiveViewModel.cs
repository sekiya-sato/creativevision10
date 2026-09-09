using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._30HHT;

public partial class HhtManualDataReceiveViewModel : Helpers.BaseViewModel {
	private const string DefaultInputDirectory = @"C:\hht\";
	private const int ExpectedFieldCount = 16;
	/// <summary>区分(VULCANタイプ)の有効範囲 1:売上 - 12:客数</summary>
	private const int MinType0 = 1;
	private const int MaxType0 = 12;

	[ObservableProperty]
	public partial string InputDirectory { get; set; } = string.Empty;

	[RelayCommand]
	private void Init() {
		InputDirectory = DefaultInputDirectory;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ReceiveDataAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(InputDirectory)) {
			MessageEx.ShowErrorDialog("入力先を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();

			var directoryPath = Path.GetFullPath(InputDirectory);
			if (!Directory.Exists(directoryPath)) {
				MessageEx.ShowErrorDialog($"入力先が存在しません: {directoryPath}", owner: ClientLib.GetActiveView(this));
				return;
			}

			var filePaths = Directory.EnumerateFiles(directoryPath)
				.Where(IsTargetInputFile)
				.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (filePaths.Count == 0) {
				MessageEx.ShowErrorDialog("対象ファイルが見つかりません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			var backupNames = BuildBackupNames(filePaths);
			var records = await LoadRecordsAsync(filePaths, backupNames, ct);
			if (records.Count == 0) {
				MessageEx.ShowErrorDialog("対象ファイルに取込対象データがありません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataMsg = Common.SerializeObject(new InsertBulkParam(typeof(TranVulcanHht), Common.SerializeObject(records))),
				DataType = typeof(InsertBulkParam),
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				MessageEx.ShowErrorDialog($"HHTデータ受信エラー: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}
			var count = records.Count;
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				count = list.Count;
			}
			// 登録が成功してから入力ファイルをbackディレクトリへ移動する(失敗時は入力先に残す)
			MoveToBackDirectory(filePaths, backupNames);
			var fileNames = string.Join(",", filePaths.Select(Path.GetFileName));
			MessageEx.ShowInformationDialog($"完了しました({count}件,{fileNames})", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"受信に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 入力ファイルごとのバックアップ名(受信日時_ファイル連番.txt)を決める。
	/// </summary>
	private static Dictionary<string, string> BuildBackupNames(IEnumerable<string> filePaths) {
		var stamp = DateTime.Now.ToDtStrDateTimeShort();
		var backupNames = new Dictionary<string, string>();
		var fileCnt = 1;
		foreach (var filePath in filePaths) {
			backupNames.Add(filePath, $"{stamp}_{fileCnt:D3}.txt");
			fileCnt++;
		}
		return backupNames;
	}

	private static async Task<List<TranVulcanHht>> LoadRecordsAsync(IEnumerable<string> filePaths, IReadOnlyDictionary<string, string> backupNames, CancellationToken ct) {
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var encoding = Encoding.GetEncoding("shift_jis");
		List<TranVulcanHht> records = [];
		foreach (var filePath in filePaths) {
			ct.ThrowIfCancellationRequested();
			var fileName = Path.GetFileName(filePath);
			var lines = await File.ReadAllLinesAsync(filePath, encoding, ct);
			for (var index = 0; index < lines.Length; index++) {
				ct.ThrowIfCancellationRequested();
				var line = lines[index];
				if (string.IsNullOrWhiteSpace(line)) {
					continue;
				}
				records.Add(ParseRecord(line, fileName, index + 1, backupNames[filePath]));
			}
		}
		return records;
	}

	/// <summary>
	/// 入力ファイルをbackディレクトリへ移動する。DB登録が成功した後にのみ呼ぶ。
	/// </summary>
	private static void MoveToBackDirectory(IEnumerable<string> filePaths, IReadOnlyDictionary<string, string> backupNames) {
		foreach (var filePath in filePaths) {
			var directory = Path.GetDirectoryName(filePath);
			if (directory == null) {
				continue;
			}
			var backDirectory = Path.Combine(directory, "back");
			if (!Directory.Exists(backDirectory)) {
				Directory.CreateDirectory(backDirectory);
			}
			var destPath = Path.Combine(backDirectory, backupNames[filePath]);
			if (File.Exists(destPath)) {
				File.Delete(destPath);
			}
			File.Move(filePath, destPath);
		}
	}

	private static TranVulcanHht ParseRecord(string line, string fileName, int lineNo, string backupName) {
		List<string> fields;
		try {
			fields = ParseCsvLine(line);
		}
		catch (InvalidDataException ex) {
			throw new InvalidDataException($"{fileName} {lineNo}行目: {ex.Message}");
		}

		if (fields.Count < ExpectedFieldCount) {
			throw new InvalidDataException($"{fileName} {lineNo}行目: 項目数が不足しています。期待値={ExpectedFieldCount} 実際={fields.Count}");
		}
		var newRec = new TranVulcanHht {
			// field名は、VULCAN定義に従う
			Type0 = GetType0(fields, 0, "区分", fileName, lineNo),
			HhtNo = GetInt(fields, 1, 3, "HTNO", fileName, lineNo),
			Serial = GetInt(fields, 2, 5, "シリアル", fileName, lineNo),
			DenDay = GetString(fields, 3, 8, "日付", fileName, lineNo),
			Shop = GetString(fields, 4, 8, "店舗", fileName, lineNo),
			Tanto = GetString(fields, 5, 6, "担当者", fileName, lineNo),
			HanKubun = GetInt(fields, 6, 1, "販区分", fileName, lineNo),
			DenNo = GetString(fields, 7, 13, "伝票NO", fileName, lineNo),
			Jan1 = GetString(fields, 8, 13, "JANコード上段", fileName, lineNo),
			Jan2 = GetString(fields, 9, 13, "JANコード下段", fileName, lineNo),
			Su = GetInt(fields, 10, 6, "数量", fileName, lineNo),
			Tanka = GetInt(fields, 11, 9, "単価", fileName, lineNo),
			ToriSaki = GetString(fields, 12, 8, "取引先", fileName, lineNo),
			KakeRitsu = GetString(fields, 13, 8, "掛率/No/納品日", fileName, lineNo),
			TotalCnt = GetInt(fields, 14, 5, "全件数", fileName, lineNo),
			Filler = GetString(fields, 15, 6, "予備", fileName, lineNo),
			BackupFileName = backupName,
			LineNo = lineNo,
			ComputerName = Environment.MachineName,
			UserName = Environment.UserName
		};
		return newRec;
	}

	private static List<string> ParseCsvLine(string line) {
		List<string> fields = [];
		StringBuilder current = new();
		var inQuotes = false;

		for (var index = 0; index < line.Length; index++) {
			var ch = line[index];

			if (ch == '"') {
				if (inQuotes && index + 1 < line.Length && line[index + 1] == '"') {
					current.Append('"');
					index++;
					continue;
				}

				inQuotes = !inQuotes;
				continue;
			}

			if (ch == ',' && !inQuotes) {
				fields.Add(current.ToString());
				current.Clear();
				continue;
			}

			current.Append(ch);
		}

		if (inQuotes) {
			throw new InvalidDataException("CSVの引用符が閉じられていません。");
		}

		fields.Add(current.ToString());
		return fields;
	}

	private static bool IsTargetInputFile(string filePath) {
		var fileName = Path.GetFileName(filePath);
		if (string.IsNullOrWhiteSpace(fileName)) {
			return false;
		}
		// 'HKALLS' で始まるか（大文字小文字無視）
		return fileName.StartsWith("HKALLS", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetString(IReadOnlyList<string> fields, int index, int maxLength, string fieldName, string fileName, int lineNo) {
		var value = fields[index].Trim();
		if (value.Length > maxLength) {
			throw CreateFieldError(fileName, lineNo, fieldName, $"文字数超過です。最大={maxLength}");
		}
		return value;
	}

	private static int GetInt(IReadOnlyList<string> fields, int index, int maxDigits, string fieldName, string fileName, int lineNo) {
		var value = fields[index].Trim();
		if (string.IsNullOrEmpty(value))
			return 0;
		ValidateDigits(value, maxDigits, fieldName, fileName, lineNo);
		if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
			throw CreateFieldError(fileName, lineNo, fieldName, "数値に変換できません。");
		}
		return result;
	}

	private static long GetLong(IReadOnlyList<string> fields, int index, int maxDigits, string fieldName, string fileName, int lineNo) {
		var value = fields[index].Trim();
		if (string.IsNullOrEmpty(value))
			return 0;
		ValidateDigits(value, maxDigits, fieldName, fileName, lineNo);
		if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
			throw CreateFieldError(fileName, lineNo, fieldName, "数値に変換できません。");
		}

		return result;
	}

	private static decimal GetDecimal(IReadOnlyList<string> fields, int index, int maxIntegerDigits, int maxScale, string fieldName, string fileName, int lineNo) {
		var value = fields[index].Trim();
		if (string.IsNullOrEmpty(value))
			return 0;
		if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) {
			throw CreateFieldError(fileName, lineNo, fieldName, "数値に変換できません。");
		}

		var normalized = value.TrimStart('+', '-');
		var parts = normalized.Split('.');
		if (parts.Length > 2) {
			throw CreateFieldError(fileName, lineNo, fieldName, "小数形式が不正です。");
		}

		var integerDigits = parts[0].Length;
		var scale = parts.Length == 2 ? parts[1].Length : 0;
		if (integerDigits > maxIntegerDigits || scale > maxScale) {
			throw CreateFieldError(fileName, lineNo, fieldName, $"桁数が不正です。整数部最大={maxIntegerDigits} 小数部最大={maxScale}");
		}

		return result;
	}

	private static void ValidateDigits(string value, int maxDigits, string fieldName, string fileName, int lineNo) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw CreateFieldError(fileName, lineNo, fieldName, "値が空です。");
		}

		var normalized = value.TrimStart('+', '-');
		if (normalized.Length > maxDigits) {
			throw CreateFieldError(fileName, lineNo, fieldName, $"桁数超過です。最大={maxDigits}");
		}

		if (!normalized.All(char.IsAsciiDigit)) {
			throw CreateFieldError(fileName, lineNo, fieldName, "数値項目に数値以外が含まれています。");
		}
	}
	/// <summary>
	/// VULCANの区分フィールドは 1-9,A-C の1桁16進数で表現されるため、16進数として 1-12 の数値へ変換する。
	/// 16進変換はこの区分フィールド専用で、他の数値項目には適用しない(A-Fが混入した際に黙って数値化されるのを防ぐ)。
	/// </summary>
	private static int GetType0(IReadOnlyList<string> fields, int index, string fieldName, string fileName, int lineNo) {
		var value = fields[index].Trim();
		if (value.Length != 1
			|| !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)
			|| result < MinType0 || result > MaxType0) {
			throw CreateFieldError(fileName, lineNo, fieldName, $"1-9,A-C({MinType0}-{MaxType0})のいずれかを指定してください。値={value}");
		}
		return result;
	}

	private static InvalidDataException CreateFieldError(string fileName, int lineNo, string fieldName, string detail) {
		return new InvalidDataException($"{fileName} {lineNo}行目 {fieldName}: {detail}");
	}
}

