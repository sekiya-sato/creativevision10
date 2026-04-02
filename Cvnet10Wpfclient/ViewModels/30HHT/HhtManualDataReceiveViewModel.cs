using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cvnet10Wpfclient.ViewModels._30HHT;

public partial class HhtManualDataReceiveViewModel : Helpers.BaseViewModel {
	private const string DefaultInputDirectory = @"C:\hht\";
	private const int ExpectedFieldCount = 16;

	[ObservableProperty]
	private string inputDirectory = string.Empty;

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

			var records = await LoadRecordsAsync(filePaths, ct);
			if (records.Count == 0) {
				MessageEx.ShowErrorDialog("対象ファイルに取込対象データがありません。", owner: ClientLib.GetActiveView(this));
				return;
			}

			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg302_Op_HhtDataRecv,
				DataMsg = Common.SerializeObject(records),
				DataType = typeof(List<TranHhtdata>)
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				MessageEx.ShowErrorDialog($"HHTデータ受信エラー: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			var count = Common.DeserializeObject<int>(reply.DataMsg);
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

	private static async Task<List<TranHhtdata>> LoadRecordsAsync(IEnumerable<string> filePaths, CancellationToken ct) {
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var encoding = Encoding.GetEncoding("shift_jis");
		List<TranHhtdata> records = [];
		var backupPaths = new Dictionary<string, string>();
		var fileCnt = 1;
		foreach (var filePath in filePaths) {
			ct.ThrowIfCancellationRequested();
			var fileName = Path.GetFileName(filePath);
			var lines = await File.ReadAllLinesAsync(filePath, encoding, ct);
			backupPaths.Add(filePath, $"{DateTime.Now.ToDtStrDateTimeShort()}_{fileCnt:D3}.txt");
			for (var index = 0; index < lines.Length; index++) {
				ct.ThrowIfCancellationRequested();
				var line = lines[index];
				if (string.IsNullOrWhiteSpace(line)) {
					continue;
				}
				records.Add(ParseRecord(line, fileName, index + 1, backupPaths[filePath]));
				fileCnt++;
			}
		}
		// 全て正常終了したら、ファイルをbackディレクトリに移動
		foreach (var filePath in filePaths) {
			var directory = Path.GetDirectoryName(filePath);
			if (directory == null) {
				continue;
			}
			var backDirectory = Path.Combine(directory, "back");
			if (!Directory.Exists(backDirectory)) {
				Directory.CreateDirectory(backDirectory);
			}
			var destPath = Path.Combine(backDirectory, backupPaths[filePath]);
			if (File.Exists(destPath)) {
				File.Delete(destPath);
			}
			File.Move(filePath, destPath);
		}
		return records;
	}

	private static TranHhtdata ParseRecord(string line, string fileName, int lineNo, string backupName) {
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
		var newRec = new TranHhtdata {
			// field名は、VULCAN定義に従う
			Type0 = GetInt(fields, 0, 1, "区分", fileName, lineNo),
			HhtNo = GetInt(fields, 1, 3, "HTNO", fileName, lineNo),
			Serial = GetInt(fields, 2, 5, "シリアル", fileName, lineNo),
			DenDay = GetString(fields, 3, 8, "日付", fileName, lineNo),
			Store = GetString(fields, 4, 8, "店舗", fileName, lineNo),
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

	private static InvalidDataException CreateFieldError(string fileName, int lineNo, string fieldName, string detail) {
		return new InvalidDataException($"{fileName} {lineNo}行目 {fieldName}: {detail}");
	}
}

