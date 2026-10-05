using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// 物流連携画面（L02〜L04）の共通基底。設定照会・処理中表示・サーバ呼出しの例外処理をまとめる。
/// </summary>
public abstract partial class LogisticsViewModelBase : BaseViewModel {
	/// <summary>設定照会に成功したか（失敗時は実行させない）</summary>
	protected bool SettingsLoaded { get; private set; }

	/// <summary>設定照会の結果（対象倉庫の一覧に使う）</summary>
	protected LogisticsSettingsInfo? SettingsInfo { get; private set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasUnusableReason))]
	public partial string UnusableReason { get; set; } = string.Empty;

	public bool HasUnusableReason => !string.IsNullOrEmpty(UnusableReason);

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "連携設定を取得しています...";

	[ObservableProperty]
	public partial string ResultText { get; set; } = string.Empty;

	/// <summary>実行系の操作ができるか（設定取得済み・設定不備なし・処理中でない）</summary>
	protected bool CanRunBase => SettingsLoaded && !IsProcessing && !HasUnusableReason;

	partial void OnIsProcessingChanged(bool value) => RefreshCommands();

	partial void OnUnusableReasonChanged(string value) => RefreshCommands();

	/// <summary>コマンドの実行可否を更新する</summary>
	protected abstract void RefreshCommands();

	/// <summary>連携設定を照会する。失敗・設定不備は <see cref="UnusableReason"/> に表示する</summary>
	/// <returns>照会できたか（設定不備でも照会できれば true）</returns>
	protected async Task<bool> LoadSettingsAsync() {
		IsProcessing = true;
		try {
			ClientLib.Cursor2Wait();
			var (info, error) = await LogisticsClient.ExecuteAsync<LogisticsSettingsInfo>(new LogisticsSettingsQueryParam(), CancellationToken.None);
			if (info == null) {
				UnusableReason = $"連携設定の取得に失敗しました。{error}";
				StatusMessage = UnusableReason;
				return false;
			}
			SettingsInfo = info;
			SettingsLoaded = true;
			UnusableReason = info.UnusableReason ?? string.Empty;
			StatusMessage = HasUnusableReason ? "連携設定に不備があるため実行できません。" : string.Empty;
			return true;
		}
		catch (Exception ex) {
			UnusableReason = $"連携設定の取得に失敗しました。{ex.Message}";
			StatusMessage = UnusableReason;
			return false;
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// サーバ処理を呼ぶ（処理中表示・待機カーソル・エラーダイアログ）。失敗・中止は null。
	/// </summary>
	/// <param name="param">契約のパラメータ</param>
	/// <param name="label">処理名（状態表示用）</param>
	/// <param name="isUpdate">DBやファイルを変える処理か（通信エラー時は結果不明として案内する）</param>
	protected async Task<T?> CallAsync<T>(object param, string label, bool isUpdate) where T : class {
		IsProcessing = true;
		StatusMessage = $"{label}を実行しています...";
		try {
			ClientLib.Cursor2Wait();
			var (result, error) = await LogisticsClient.ExecuteAsync<T>(param, CancellationToken.None);
			if (result == null) {
				StatusMessage = $"{label}に失敗しました。{error}";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return null;
			}
			StatusMessage = $"{label}が完了しました。";
			return result;
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{label}を中止しました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{label}を中止しました。";
		}
		catch (Exception ex) {
			// 更新処理は通信エラーでもサーバ側で完了している可能性がある
			StatusMessage = isUpdate
				? $"{label}の結果が不明です。送受信履歴（連携エラーデータ照会）を確認してください。{ex.Message}"
				: $"{label}に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
		return null;
	}

	/// <summary>確認ダイアログ（はい=true）</summary>
	protected bool Confirm(string message) =>
		MessageEx.ShowQuestionDialog(message, owner: ClientLib.GetActiveView(this)) == System.Windows.MessageBoxResult.Yes;
}

/// <summary>物流連携画面の表示用の変換</summary>
internal static class LogisticsText {
	/// <summary>enum 値の [Comment] 表示名（定義外の値は数値のまま）</summary>
	internal static string EnumText<TEnum>(int value) where TEnum : struct, Enum {
		if (!Enum.IsDefined(typeof(TEnum), value)) {
			return value.ToString(CultureInfo.InvariantCulture);
		}
		var name = Enum.GetName(typeof(TEnum), value) ?? string.Empty;
		var comment = typeof(TEnum).GetField(name)?.GetCustomAttributes(typeof(CommentAttribute), false).OfType<CommentAttribute>().FirstOrDefault();
		return comment?.Content ?? name;
	}

	/// <summary>バッチ状態の表示名（方向で enum を切り替える）</summary>
	internal static string BatchStatusText(int direction, int status) =>
		direction == (int)EnumLogisticsDirection.Send ? EnumText<EnumLogisticsSendStatus>(status) : EnumText<EnumLogisticsReceiveStatus>(status);

	/// <summary>yyyyMMdd を yyyy/MM/dd にする（形式外はそのまま）</summary>
	internal static string Day(string? yyyymmdd) =>
		yyyymmdd is { Length: 8 } d && d.All(char.IsAsciiDigit) ? $"{d[..4]}/{d[4..6]}/{d[6..]}" : yyyymmdd ?? string.Empty;

	/// <summary>UTC Ticks をローカルの yyyy/MM/dd HH:mm:ss にする</summary>
	internal static string LocalTime(long utcTicks) =>
		utcTicks <= 0 ? string.Empty : new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

	/// <summary>コードと名称をつなぐ</summary>
	internal static string CodeName(string? code, string? name) => string.Join(" ", new[] { code, name }.Where(s => !string.IsNullOrWhiteSpace(s)));

	/// <summary>実行結果の表示文（要約・種別ごとの結果・警告）</summary>
	internal static string RunResult(LogisticsRunResult result) {
		var text = new StringBuilder();
		text.AppendLine(result.Message ?? string.Empty);
		foreach (var kind in result.Kinds ?? []) {
			var file = string.IsNullOrEmpty(kind.FileName) ? "(ファイルなし)" : kind.FileName;
			var batch = kind.BatchId > 0 ? $" バッチ{kind.BatchId}" : string.Empty;
			text.AppendLine($"・{LogisticsDataKind.DisplayName(kind.Kind)} {kind.Count:N0}行 {file}{batch}");
		}
		var warnings = result.Warnings ?? [];
		if (warnings.Length > 0) {
			text.AppendLine();
			text.AppendLine($"警告 {warnings.Length:N0} 件:");
			foreach (var warning in warnings) {
				text.AppendLine($"・{warning}");
			}
		}
		return text.ToString().TrimEnd();
	}
}

/// <summary>物流連携の行（<see cref="TranLogisticsLine"/>）の表示行。L03・L04 で共用</summary>
public sealed class LogisticsLineRow {
	public LogisticsLineRow(TranLogisticsLine line) {
		Line = line;
	}

	public TranLogisticsLine Line { get; }
	public long Id => Line.Id;
	public int LineNo => Line.LineNo;
	public int Status => Line.Status;
	public string StatusName => LogisticsText.EnumText<EnumLogisticsLineStatus>(Line.Status) + (Line.Id_LineOrg > 0 ? "(訂正版)" : string.Empty);
	public string ErrorCode => Line.ErrorCode;
	public string ErrorMsg => Line.ErrorMsg;
	public int Su => Line.Su;
	public int Su2 => Line.Su2;
	public string WorkDay => LogisticsText.Day(Line.WorkDay);
	public string RefText => Line.RefId > 0 ? $"{Line.RefTable} {Line.RefId}{(Line.RefNo > 0 ? $"-{Line.RefNo}" : string.Empty)}" : string.Empty;
	public string TargetText => Line.TargetId > 0 ? $"{Line.TargetTable} {Line.TargetId}" : string.Empty;
	public string RawText => Line.RawText;
	/// <summary>エラー行</summary>
	public bool IsError => Line.Status == (int)EnumLogisticsLineStatus.Error;
	/// <summary>警告付きの行（エラー行を除く）</summary>
	public bool IsWarning => !IsError && IsWarningCode(Line.ErrorCode);
	/// <summary>除外・訂正済み（反映対象外）</summary>
	public bool IsInactive => Line.Status is (int)EnumLogisticsLineStatus.Excluded or (int)EnumLogisticsLineStatus.Corrected;
	/// <summary>除外・訂正版の対象（未処理・エラー）</summary>
	public bool IsEditable => Line.Status is (int)EnumLogisticsLineStatus.Pending or (int)EnumLogisticsLineStatus.Error;

	internal static bool IsWarningCode(string? code) => code?.StartsWith('W') == true;
}

/// <summary>訂正版の1項目（列見出しと値）</summary>
public sealed partial class LogisticsFieldRow : ObservableObject {
	public string Header { get; init; } = string.Empty;
	/// <summary>元の値（変更の強調表示用）</summary>
	public string Original { get; init; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsChanged))]
	public partial string Value { get; set; } = string.Empty;

	public bool IsChanged => Value != Original;
}
