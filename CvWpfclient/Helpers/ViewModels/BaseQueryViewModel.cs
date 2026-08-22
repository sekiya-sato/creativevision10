/*
# description
BaseQueryViewModel は「条件を入力して検索し、結果を DataGrid に表示する」照会画面の共通基底クラスです。
帳票(BaseReportViewModel)がPDF出力を前提にするのに対し、こちらは画面表示が目的です。

サーバへの問い合わせは QueryListSqlParam(Msg101_Op_Query) を使います。
このAPIは結果を必ず「DBにマップされた型」へ materialize するため、任意の列形状は返せません。
したがって集計や複数テーブルの突き合わせは、テーブル単位に型付きで取得してから
クライアント側で合成します（既存の ZaikoQueryViewModel と同じ方針）。

# example
public partial class SampleQueryViewModel : BaseQueryViewModel {
	protected override string QueryTitle => "サンプル照会";
	protected override void OnClearConditions() { ... }
	protected override async Task OnSearchAsync(CancellationToken ct) { ... }
}
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using System.Collections;
using System.Windows;

namespace CvWpfclient.Helpers;

public abstract partial class BaseQueryViewModel : BaseViewModel {
	[ObservableProperty]
	public partial string Title { get; set; } = string.Empty;

	/// <summary>画面下部などに表示する状態メッセージ</summary>
	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	/// <summary>検索中フラグ。ボタンの活性制御に使う</summary>
	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>取得件数の上限（入力欄）</summary>
	[ObservableProperty]
	public partial string MaxCountText { get; set; } = "500";

	/// <summary>画面タイトル。派生クラスで上書きする</summary>
	protected virtual string QueryTitle => string.Empty;

	/// <summary>この ViewModel を DataContext に持つ Window（ダイアログの Owner 用）</summary>
	protected virtual Window? ActiveWindow => ClientLib.GetActiveView(this);

	protected BaseQueryViewModel() {
		Title = QueryTitle;
	}

	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		ClientLib.Exit(this);
	}

	/// <summary>ContentRendered から呼ばれる初期化。必要な画面のみ上書きする</summary>
	[RelayCommand]
	protected virtual void Init() { }

	/// <summary>検索本体。派生クラスで実装する</summary>
	protected abstract Task OnSearchAsync(CancellationToken ct);

	/// <summary>検索条件のクリア。派生クラスで実装する</summary>
	protected abstract void OnClearConditions();

	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task Search(CancellationToken ct) {
		if (IsBusy) return;
		StartBusy("検索中...");
		try {
			await OnSearchAsync(ct);
		}
		catch (OperationCanceledException) {
			Message = "検索を中断しました";
		}
		catch (Exception ex) {
			Message = $"検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			FinishBusy();
		}
	}

	[RelayCommand]
	protected void ClearConditions() {
		OnClearConditions();
		MaxCountText = "500";
		Message = "検索条件をクリアしました";
	}

	protected void StartBusy(string message) {
		IsBusy = true;
		Message = message;
		ClientLib.Cursor2Wait();
	}

	protected void FinishBusy() {
		IsBusy = false;
		ClientLib.Cursor2Normal();
	}

	/// <summary>取得件数上限の検証。不正なら警告を出して false。</summary>
	protected bool TryGetMaxCount(out int maxCount) {
		if (!int.TryParse(MaxCountText.Trim(), out maxCount) || maxCount < 1 || maxCount > 100000) {
			MessageEx.ShowWarningDialog("取得件数は 1〜100000 で入力してください。", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	/// <summary>
	/// 生SQLを投げて DBマップ型のリストを取得する。
	/// T はサーバ側でも解決できる型（CvBase のテーブルクラス）である必要がある。
	/// </summary>
	protected Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		CoreServiceClient.QuerySqlListAsync<T>(sql, parameters, ct);

	protected static string AddSqlParameter(List<string> parameters, object value) =>
		SqlWhere.AddParameter(parameters, value);

	protected static string BuildCodeRangeWhere(List<string> parameters, string columnName, string? codeFrom, string? codeTo) =>
		SqlWhere.CodeRange(parameters, columnName, codeFrom, codeTo);

	/// <summary>yyyyMMdd 8桁へ変換</summary>
	protected static string ToDenDay(DateTime date) => date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

	static readonly string[] DateFormats = ["yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd", "yyyy-MM-dd", "yyyy-M-d"];

	/// <summary>
	/// 日付文字列を警告なしで解釈する。
	/// <para>SQLの既定値を決めるなど、利用者へ知らせる必要が無い場面で使う（不正なら呼び出し側が既定値へ落とす）。</para>
	/// </summary>
	protected static bool TryParseDateQuiet(string? text, out DateTime date) =>
		DateTime.TryParseExact((text ?? string.Empty).Trim(), DateFormats,
			System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

	/// <summary>日付文字列を検証する。不正なら警告を出して false。</summary>
	protected bool TryParseDate(string? text, out DateTime date) {
		if (!TryParseDateQuiet(text, out date)) {
			MessageEx.ShowWarningDialog("日付は yyyy/MM/dd 形式で入力してください。", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	/// <summary>選択ダイアログでコードを選ばせる。キャンセル時は null。</summary>
	protected string? SelectCode<T>(string where, string order = "Code") where T : BaseDbClass, IBaseCodeName {
		var selected = PrintPdfHelper.ShowSelectDialog<T>(this, typeof(T), where, order);
		return selected?.Code;
	}

	protected string? SelectSokoCode() => SelectCode<MasterTokui>("TenType=0");
	protected string? SelectShopCode() => SelectCode<MasterTokui>("TenType=6");
	protected string? SelectTokuiCode() => SelectCode<MasterTokui>("");
	protected string? SelectShohinCode() => SelectCode<MasterShohin>("");
}
