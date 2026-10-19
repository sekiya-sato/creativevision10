using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace CvWpfclient.ViewModels._23AiQuery;

/// <summary>
/// AI照会（CSV）: 会話で集計条件を確定し、AIが作ったSQLを検査・実行してCSVにする。
/// Doc/spec/2026-10-19_AI-query_最小変更設計案.md §4・§8
/// <para>
/// 会話状態（会話・条件・確認済みの表）は画面が持ち、サーバ（Msg091）はステートレス。
/// SQLの実行は既存の汎用照会（Msg101 + QueryListSqlParam(typeof(object))）で行い、CSVは画面で作る。
/// 利用者の発言で条件が変わったら、それまでのSQL・結果は「旧版」として実行・保存できなくする。
/// 保存SQLの再実行は Msg092 の検査と Msg101 だけで行い、AIを呼ばない。
/// </para>
/// </summary>
public partial class AiQueryViewModel : BaseViewModel {
	/// <summary>結果プレビューの表示行数（CSVは全件）</summary>
	public const int PreviewRows = 100;
	/// <summary>AIの1往復の待ち時間上限（サーバ側は1回のLLM呼出しごとに AiQuery:TimeoutSeconds）</summary>
	static readonly TimeSpan ChatTimeout = TimeSpan.FromMinutes(10);

	// ---- 会話 ----------------------------------------------------------------

	string conversationId = Guid.NewGuid().ToString("N");
	string baseDate = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
	/// <summary>要求ごとに増やし、遅れて届いた旧応答を捨てる</summary>
	int requestRevision;
	/// <summary>利用者の発言（条件変更）ごとに増やす。SQL・結果はどの条件版で作ったかを持つ</summary>
	int conditionRevision;
	List<string> knownTables = [];
	AiQuerySpec? spec;
	/// <summary>保存SQLを開いて会話で修正するときの出発点</summary>
	AiQuerySqlDraft? baseSql;
	CancellationTokenSource? chatCts;

	public ObservableCollection<AiQueryChatLine> ChatLines { get; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SendCommand))]
	public partial string InputText { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsIdle))]
	[NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(GenerateCommand), nameof(CancelChatCommand), nameof(RunCommand), nameof(ValidateCommand))]
	[NotifyCanExecuteChangedFor(nameof(ExportCsvCommand), nameof(SaveSqlFileCommand), nameof(SaveSharedCommand), nameof(SavePersonalCommand))]
	public partial bool IsBusy { get; set; }

	public bool IsIdle => !IsBusy;

	/// <summary>直近の対話状態</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
	public partial EnumAiQueryStatus? ChatStatus { get; set; }

	[ObservableProperty]
	public partial string StatusText { get; set; } = "集計したい内容を入力してください（例: 先月の店舗別売上をCSVで欲しい）。";

	/// <summary>確定条件の要約（AIの確認文）</summary>
	[ObservableProperty]
	public partial string SpecSummary { get; set; } = string.Empty;

	// ---- SQL ------------------------------------------------------------------

	[ObservableProperty]
	public partial string SqlTitle { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SqlDescription { get; set; } = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ValidateCommand), nameof(RunCommand), nameof(SaveSqlFileCommand), nameof(SaveSharedCommand), nameof(SavePersonalCommand))]
	public partial string SqlText { get; set; } = string.Empty;

	/// <summary>SQLの手修正を許可する</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSqlReadOnly))]
	public partial bool IsSqlEditable { get; set; }

	public bool IsSqlReadOnly => !IsSqlEditable;

	/// <summary>SQLがサーバ検査（Msg091生成時 / Msg092）を通っている。SQLを変えたら外す</summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveSharedCommand), nameof(SavePersonalCommand))]
	public partial bool IsSqlValidated { get; set; }

	/// <summary>SQLを作った条件版。現在の条件版と違えば旧版</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSqlCurrent))]
	[NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(SaveSqlFileCommand), nameof(SaveSharedCommand), nameof(SavePersonalCommand))]
	public partial int SqlRevision { get; set; } = -1;

	public bool IsSqlCurrent => SqlRevision == conditionRevision && SqlText.Trim().Length > 0;

	public ObservableCollection<AiQueryParamRow> Parameters { get; } = [];

	[ObservableProperty]
	public partial string SqlMessage { get; set; } = string.Empty;

	/// <summary>共有保存から開いたときの行。上書き保存に使う</summary>
	SysAiQuery? loadedShared;

	bool settingSql;

	partial void OnSqlTextChanged(string value) {
		if (settingSql) return;
		// 手修正したら検査からやり直す
		IsSqlValidated = false;
		InvalidateResult("SQLが変更されました。［実行］で検査と実行をやり直してください。");
	}

	// ---- 結果 -----------------------------------------------------------------

	List<Dictionary<string, object>> resultRows = [];
	int resultRevision = -1;

	[ObservableProperty]
	public partial DataView? ResultView { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
	public partial bool HasResult { get; set; }

	[ObservableProperty]
	public partial string ResultInfo { get; set; } = string.Empty;

	/// <summary>0:条件・SQL 1:結果</summary>
	[ObservableProperty]
	public partial int DetailTabIndex { get; set; }

	// ---- 保存一覧 -------------------------------------------------------------

	public ObservableCollection<SysAiQuery> SharedList { get; } = [];
	public ObservableCollection<AiQuerySaved> PersonalList { get; } = [];
	List<SysAiQuery> allShared = [];
	List<AiQuerySaved> allPersonal = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(OpenSharedCommand), nameof(DeleteSharedCommand))]
	public partial SysAiQuery? SelectedShared { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(OpenPersonalCommand), nameof(DeletePersonalCommand))]
	public partial AiQuerySaved? SelectedPersonal { get; set; }

	[ObservableProperty]
	public partial string SavedFilter { get; set; } = string.Empty;

	partial void OnSavedFilterChanged(string value) => ApplySavedFilter();

	[ObservableProperty]
	public partial string SavedMessage { get; set; } = string.Empty;

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	// ---- 初期化 ---------------------------------------------------------------

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		LoadPersonal();
		await LoadSharedAsync(ct);
	}

	// ---- 会話コマンド -----------------------------------------------------------

	/// <summary>送信（Ctrl+Enter）。発言は条件変更として扱い、旧SQL・旧結果を無効にする</summary>
	[RelayCommand(CanExecute = nameof(CanSend))]
	async Task Send() {
		var text = InputText.Trim();
		if (text.Length == 0) return;
		ChatLines.Add(new AiQueryChatLine(EnumAiQueryRole.User, text));
		InputText = string.Empty;
		conditionRevision++;
		OnPropertyChanged(nameof(IsSqlCurrent));
		NotifySqlCommands();
		if (SqlText.Length > 0) SqlMessage = "条件が変更されました。確認後に［SQL作成］でSQLを作り直してください。";
		InvalidateResult("条件が変更されました。");
		await ChatAsync(EnumAiQueryMode.Chat);
	}

	bool CanSend() => IsIdle && InputText.Trim().Length > 0;

	/// <summary>確定条件からSQLを作成する</summary>
	[RelayCommand(CanExecute = nameof(CanGenerate))]
	Task Generate() => ChatAsync(EnumAiQueryMode.Generate);

	bool CanGenerate() => IsIdle && ChatStatus == EnumAiQueryStatus.Confirming;

	/// <summary>AIの応答待ちを取り消す</summary>
	[RelayCommand(CanExecute = nameof(IsBusy))]
	void CancelChat() => chatCts?.Cancel();

	/// <summary>会話・条件・SQL・結果を消して最初からにする（保存一覧は残す）</summary>
	[RelayCommand(CanExecute = nameof(IsIdle))]
	void NewConversation() {
		ResetConversation();
		ClearSql();
		InvalidateResult(string.Empty);
		StatusText = "集計したい内容を入力してください（例: 先月の店舗別売上をCSVで欲しい）。";
	}

	async Task ChatAsync(EnumAiQueryMode mode) {
		var revision = ++requestRevision;
		var sentCondition = conditionRevision;
		var param = new AiQueryChatParam {
			ConversationId = conversationId,
			Revision = revision,
			BaseDate = baseDate,
			Mode = mode,
			Messages = [.. ChatLines.Where(x => !x.IsError).Select(x => new AiQueryMessage { Role = x.Role, Text = x.Text })],
			Spec = spec,
			KnownTables = [.. knownTables],
			BaseSql = baseSql,
		};
		IsBusy = true;
		StatusText = mode == EnumAiQueryMode.Generate ? "AIがSQLを作成しています…（数十秒〜数分かかります。［取消］で中止）" : "AIが考えています…（数十秒〜数分かかります。［取消］で中止）";
		using var cts = new CancellationTokenSource();
		chatCts = cts;
		try {
			var reply = await SendAsync(CvFlag.Msg091_AiQueryChat, param, AppGlobal.GetDefaultCallContext(cts.Token, ChatTimeout));
			var result = Common.DeserializeObject<AiQueryChatResult>(reply.DataMsg ?? "{}") ?? new AiQueryChatResult();
			// 会話を作り直した・新しい要求を出した後の応答は捨てる
			if (result.ConversationId != conversationId || result.Revision != requestRevision) return;
			ApplyChatResult(result, mode, sentCondition);
		}
		catch (OperationCanceledException) {
			StatusText = "AIの応答待ちを取り消しました。";
		}
		catch (Exception ex) {
			ChatLines.Add(new AiQueryChatLine(EnumAiQueryRole.Assistant, $"通信に失敗しました: {ex.Message}", isError: true));
			StatusText = "通信に失敗しました。もう一度送信してください。";
		}
		finally {
			if (ReferenceEquals(chatCts, cts)) chatCts = null;
			IsBusy = false;
		}
	}

	void ApplyChatResult(AiQueryChatResult result, EnumAiQueryMode mode, int sentCondition) {
		knownTables = result.KnownTables;
		if (result.Spec != null) spec = result.Spec;
		if (result.Status == EnumAiQueryStatus.Failed) {
			var detail = result.Errors.Count > 0 ? "\n" + string.Join("\n", result.Errors) : string.Empty;
			ChatLines.Add(new AiQueryChatLine(EnumAiQueryRole.Assistant, result.AssistantText + detail, isError: true));
			StatusText = "AIの処理に失敗しました。確定した条件は残っています。";
			// SQL作成の失敗でも最後のSQLは参考に表示する（検査未通過のまま）
			if (mode == EnumAiQueryMode.Generate && result.Draft != null) {
				SetDraft(result.Draft, sentCondition, validated: false);
				SqlMessage = "検査に通らなかったSQLです。手修正するか、条件を見直してください。";
			}
			return;
		}
		ChatStatus = result.Status;
		if (result.Status == EnumAiQueryStatus.Generated && result.Draft != null) {
			SetDraft(result.Draft, sentCondition, validated: true);
			ChatLines.Add(new AiQueryChatLine(EnumAiQueryRole.Assistant, $"SQLを作成しました（{result.Draft.Title}）。\n{result.Draft.Description}"));
			StatusText = "SQLを作成しました。［実行］で結果を確認し、CSV保存できます。";
			DetailTabIndex = 0;
			return;
		}
		ChatLines.Add(new AiQueryChatLine(EnumAiQueryRole.Assistant, result.AssistantText));
		if (result.Status == EnumAiQueryStatus.Confirming) {
			SpecSummary = result.AssistantText;
			StatusText = "この内容でよければ［SQL作成］、変更があれば入力して送信してください。";
		}
		else {
			StatusText = "AIの質問に回答してください。";
		}
	}

	void ResetConversation() {
		chatCts?.Cancel();
		conversationId = Guid.NewGuid().ToString("N");
		baseDate = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		requestRevision++;
		conditionRevision++;
		knownTables = [];
		spec = null;
		baseSql = null;
		ChatStatus = null;
		SpecSummary = string.Empty;
		ChatLines.Clear();
	}

	// ---- SQL・実行 ------------------------------------------------------------

	void SetDraft(AiQuerySqlDraft draft, int revision, bool validated) {
		settingSql = true;
		try {
			SqlText = draft.Sql;
		}
		finally { settingSql = false; }
		SqlTitle = draft.Title;
		SqlDescription = draft.Description;
		Parameters.Clear();
		for (var i = 0; i < draft.Parameters.Count; i++) Parameters.Add(AiQueryParamRow.From(draft.Parameters[i], i));
		IsSqlEditable = false;
		IsSqlValidated = validated;
		SqlRevision = revision;
		SqlMessage = validated ? "検査済みのSQLです。" : string.Empty;
		InvalidateResult(string.Empty);
		NotifySqlCommands();
	}

	void ClearSql() {
		loadedShared = null;
		SetDraft(new AiQuerySqlDraft(), -1, validated: false);
		SqlMessage = string.Empty;
	}

	AiQuerySqlDraft CurrentDraft() => new() {
		Sql = SqlText.Trim(),
		Parameters = [.. Parameters.Select(x => x.ToParam())],
		Title = SqlTitle.Trim(),
		Description = SqlDescription.Trim(),
	};

	/// <summary>SQL検査のみ（AIを呼ばない）</summary>
	[RelayCommand(CanExecute = nameof(CanValidate))]
	async Task Validate(CancellationToken ct) {
		IsBusy = true;
		try { await ValidateCoreAsync(ct); }
		finally { IsBusy = false; }
	}

	bool CanValidate() => IsIdle && SqlText.Trim().Length > 0;

	async Task<bool> ValidateCoreAsync(CancellationToken ct) {
		var draft = CurrentDraft();
		var reply = await SendAsync(CvFlag.Msg092_AiQueryValidate, new AiQueryValidateParam { Sql = draft.Sql, Parameters = draft.Parameters }, AppGlobal.GetDefaultCallContext(ct));
		var result = Common.DeserializeObject<AiQueryValidateResult>(reply.DataMsg ?? "{}") ?? new AiQueryValidateResult();
		IsSqlValidated = result.IsValid;
		SqlMessage = result.IsValid ? $"検査済みのSQLです（参照: {string.Join(", ", result.Tables)}）。" : "SQLの検査エラー: " + string.Join(" / ", result.Errors);
		if (!result.IsValid) MessageEx.ShowWarningDialog("SQLが検査に通りませんでした。", string.Join("\n", result.Errors), ActiveWindow);
		return result.IsValid;
	}

	/// <summary>検査（未検査なら）→ 既存の汎用照会で実行し、結果をプレビューする</summary>
	[RelayCommand(CanExecute = nameof(CanRun))]
	async Task Run(CancellationToken ct) {
		var missing = Parameters.Where(p => p.Value.Trim().Length == 0 && (p.AskOnRun || p.Type != EnumAiQueryParamType.Text)).ToList();
		if (missing.Count > 0) {
			Warn($"パラメータの値を入力してください: {string.Join(", ", missing.Select(x => x.Label.Length > 0 ? x.Label : x.Placeholder))}");
			return;
		}
		IsBusy = true;
		InvalidateResult("実行しています…");
		var revision = conditionRevision;
		try {
			if (!IsSqlValidated && !await ValidateCoreAsync(ct)) {
				ResultInfo = "SQLが検査に通らないため実行していません。";
				return;
			}
			var values = Parameters.Select(x => x.Value.Trim()).ToArray();
			var reply = await SendAsync(CvFlag.Msg101_Op_Query, new QueryListSqlParam(typeof(object), SqlText.Trim(), values), AppGlobal.GetDefaultCallContext(ct), allowNotFound: true);
			var rows = reply.Code == CvMsgErrorCode.NotFound ? [] : Common.DeserializeObject<List<Dictionary<string, object>>>(reply.DataMsg ?? "[]") ?? [];
			if (revision != conditionRevision) return;
			resultRows = rows;
			resultRevision = revision;
			ResultView = BuildPreview(rows);
			HasResult = rows.Count > 0;
			ResultInfo = rows.Count == 0
				? $"0件でした（{DateTime.Now:HH:mm:ss} 実行）。条件を見直してください。"
				: $"{rows.Count:N0}件（{DateTime.Now:HH:mm:ss} 実行、{rows[0].Count}列）。" + (rows.Count > PreviewRows ? $" 先頭{PreviewRows}件を表示。CSVは全件です。" : string.Empty);
			DetailTabIndex = 1;
		}
		catch (OperationCanceledException) { ResultInfo = "実行を取り消しました。"; }
		catch (Exception ex) { ShowError($"実行失敗: {ex.Message}"); ResultInfo = $"実行失敗: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	bool CanRun() => IsIdle && IsSqlCurrent;

	static DataView BuildPreview(List<Dictionary<string, object>> rows) {
		var table = new DataTable();
		if (rows.Count == 0) return table.DefaultView;
		foreach (var key in rows[0].Keys) {
			// DataGridの自動列は列名の「.」「/」等をバインドパスと解釈するため、列名だけ置き換えて見出しは元のまま出す
			table.Columns.Add(new DataColumn(SafeColumnName(key, table.Columns.Count), typeof(object)) { Caption = key });
		}
		foreach (var row in rows.Take(PreviewRows)) {
			table.Rows.Add([.. row.Values.Select(v => v ?? DBNull.Value)]);
		}
		return table.DefaultView;
	}

	static string SafeColumnName(string name, int index) =>
		name.IndexOfAny(['.', '/', '[', ']', '(', ')', ' ', '\'']) >= 0 || name.Length == 0 ? $"列{index + 1}" : name;

	void InvalidateResult(string message) {
		resultRows = [];
		resultRevision = -1;
		ResultView = null;
		HasResult = false;
		ResultInfo = message;
	}

	// ---- ファイル出力 ---------------------------------------------------------

	/// <summary>表示中の結果（全件）。条件変更・SQL変更で空になる</summary>
	public IReadOnlyList<Dictionary<string, object>> ResultRows => resultRows;

	/// <summary>
	/// 結果を保存する。主形式はCSV（Excel用。UTF-8 BOM付き・見出し行あり、既存画面のCSV出力と同じ）。
	/// 保存ダイアログで JSON（型を保持）も選べる。CSVではコードの先頭0や yyyyMMdd がExcelで数値になり、NULLと空文字も区別できないため
	/// </summary>
	[RelayCommand(CanExecute = nameof(CanExportCsv))]
	void ExportCsv() {
		if (resultRows.Count == 0 || resultRevision != conditionRevision) return;
		var dialog = new SaveFileDialog {
			Title = "AI照会の結果を保存",
			Filter = "CSVファイル（Excel用） (*.csv)|*.csv|JSONファイル（型を保持） (*.json)|*.json",
			FilterIndex = 1,
			DefaultExt = ".csv",
			AddExtension = true,
			FileName = $"{SanitizeFileName(SqlTitle.Length > 0 ? SqlTitle : "AI照会")}_{DateTime.Now:yyyyMMddHHmm}",
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		// ファイル種別の選択を優先し、拡張子を直接 .json と入力した場合も JSON にする
		var json = dialog.FilterIndex == 2 || string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase);
		try {
			WriteResultFile(dialog.FileName, resultRows, json);
			ResultInfo = $"{(json ? "JSON" : "CSV")}を出力しました: {dialog.FileName}";
		}
		catch (Exception ex) { ShowError($"結果の保存失敗: {ex.Message}"); }
	}

	bool CanExportCsv() => IsIdle && HasResult;

	/// <summary>
	/// 結果ファイルを書く。CSVは UTF-8 BOM付き・見出し行あり。
	/// JSONは列順どおりのオブジェクト配列で、数値・文字列・NULLの型をそのまま保つ（UTF-8 BOMなし・整形あり）。
	/// </summary>
	public static void WriteResultFile(string path, IReadOnlyList<Dictionary<string, object>> rows, bool json) {
		if (json) {
			// 共通設定（NULL・既定値を省略）は使わず、NULLの列もキーとして残す
			File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented),
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return;
		}
		using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		rows.WriteDynamicCsv(writer, includeHeader: true);
	}

	/// <summary>SQLをファイル保存。パラメータの値をコメントに残し、プレースホルダーだけのSQLにしない</summary>
	[RelayCommand(CanExecute = nameof(CanUseSql))]
	void SaveSqlFile() {
		var dialog = new SaveFileDialog {
			Title = "SQLを保存",
			Filter = "SQLファイル (*.sql)|*.sql|すべてのファイル (*.*)|*.*",
			DefaultExt = ".sql",
			FileName = $"{SanitizeFileName(SqlTitle.Length > 0 ? SqlTitle : "AI照会")}.sql",
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		try {
			File.WriteAllText(dialog.FileName, BuildSqlFileText(CurrentDraft(), DateTime.Today), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			SqlMessage = $"SQLを保存しました: {dialog.FileName}";
		}
		catch (Exception ex) { ShowError($"SQL保存失敗: {ex.Message}"); }
	}

	bool CanUseSql() => IsIdle && IsSqlCurrent;

	public static string BuildSqlFileText(AiQuerySqlDraft draft, DateTime day) {
		static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ");
		var sb = new StringBuilder();
		sb.Append("-- タイトル: ").AppendLine(OneLine(draft.Title));
		sb.Append("-- 説明: ").AppendLine(OneLine(draft.Description));
		sb.Append("-- 保存日: ").AppendLine(day.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
		for (var i = 0; i < draft.Parameters.Count; i++) {
			var p = draft.Parameters[i];
			sb.AppendLine($"-- パラメータ: @{i} {OneLine(p.Label)}({p.Type}) = {OneLine(p.Value)}");
		}
		sb.AppendLine(draft.Sql);
		return sb.ToString();
	}

	// ---- 保存 -----------------------------------------------------------------

	/// <summary>共有保存（サーバ）。開いた共有SQLなら上書きか新規かを選ぶ</summary>
	[RelayCommand(CanExecute = nameof(CanSave))]
	async Task SaveShared(CancellationToken ct) {
		if (!CheckTitle()) return;
		var update = false;
		if (loadedShared != null) {
			var answer = MessageEx.ShowQuestionDialog($"共有の「{loadedShared.Title}」を上書きしますか？", "［いいえ］で新しい共有保存として追加します。", ActiveWindow);
			update = answer == MessageBoxResult.Yes;
		}
		var draft = CurrentDraft();
		var item = update ? Common.CloneObject(loadedShared!) : new SysAiQuery();
		item.Title = draft.Title;
		item.Description = draft.Description;
		item.SqlText = draft.Sql;
		item.SqlParams = Common.SerializeObject(draft.Parameters);
		IsBusy = true;
		try {
			var msg = update
				? new CvMsg { Flag = CvFlag.Msg201_Op_Execute, DataType = typeof(UpdateParam), DataMsg = Common.SerializeObject(new UpdateParam(typeof(SysAiQuery), Common.SerializeObject(item))) }
				: new CvMsg { Flag = CvFlag.Msg201_Op_Execute, DataType = typeof(InsertParam), DataMsg = Common.SerializeObject(new InsertParam(typeof(SysAiQuery), Common.SerializeObject(item))) };
			var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				Warn("他の端末で更新・削除されています。保存一覧を再読込してから保存し直してください。");
				return;
			}
			if (reply.Code < 0) throw new InvalidOperationException(reply.Option ?? reply.DataMsg);
			loadedShared = Common.DeserializeObject<SysAiQuery>(reply.DataMsg ?? "{}");
			SqlMessage = update ? "共有保存を上書きしました。" : "共有保存しました。";
			await LoadSharedAsync(ct);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"共有保存失敗: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	/// <summary>保存は検査済みの現行SQLだけ</summary>
	bool CanSave() => IsIdle && IsSqlCurrent && IsSqlValidated;

	/// <summary>個人保存（このPC）</summary>
	[RelayCommand(CanExecute = nameof(CanSave))]
	void SavePersonal() {
		if (!CheckTitle()) return;
		var draft = CurrentDraft();
		try {
			var items = AiQueryPersonalStore.Load();
			items.Add(new AiQuerySaved {
				SaveDate = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				Title = draft.Title,
				SqlText = draft.Sql,
				SqlParams = Common.SerializeObject(draft.Parameters),
				Id_Login = ResolveLoginId(),
				Description = draft.Description,
			});
			AiQueryPersonalStore.Save(items);
			SqlMessage = "個人保存しました（このPCだけに保存されます）。";
			LoadPersonal();
		}
		catch (Exception ex) { ShowError($"個人保存失敗: {ex.Message}"); }
	}

	bool CheckTitle() {
		if (SqlTitle.Trim().Length > 0) return true;
		Warn("タイトルを入力してください。");
		return false;
	}

	[RelayCommand(CanExecute = nameof(IsIdle))]
	Task RefreshShared(CancellationToken ct) => LoadSharedAsync(ct);

	async Task LoadSharedAsync(CancellationToken ct) {
		try {
			var reply = await SendAsync(CvFlag.Msg101_Op_Query, new QueryListParam(typeof(SysAiQuery), null, "Vdu DESC"), AppGlobal.GetDefaultCallContext(ct), allowNotFound: true);
			allShared = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? [.. list.Cast<SysAiQuery>()] : [];
			ApplySavedFilter();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { SavedMessage = $"共有保存の取得失敗: {ex.Message}"; }
	}

	void LoadPersonal() {
		try {
			allPersonal = AiQueryPersonalStore.Load();
		}
		catch (Exception ex) {
			allPersonal = [];
			SavedMessage = $"個人保存の読込失敗（ファイルは変更していません）: {ex.Message}";
		}
		ApplySavedFilter();
	}

	void ApplySavedFilter() {
		var filter = SavedFilter.Trim();
		bool Match(string title, string description) => filter.Length == 0
			|| title.Contains(filter, StringComparison.OrdinalIgnoreCase) || description.Contains(filter, StringComparison.OrdinalIgnoreCase);
		SharedList.Clear();
		foreach (var q in allShared.Where(q => Match(q.Title, q.Description))) SharedList.Add(q);
		PersonalList.Clear();
		foreach (var q in allPersonal.AsEnumerable().Reverse().Where(q => Match(q.Title, q.Description))) PersonalList.Add(q);
	}

	[RelayCommand(CanExecute = nameof(CanOpenShared))]
	void OpenShared() {
		if (SelectedShared == null) return;
		OpenSaved(AiQuerySaved.From(SelectedShared));
		loadedShared = SelectedShared;
	}

	bool CanOpenShared() => SelectedShared != null;

	[RelayCommand(CanExecute = nameof(CanOpenPersonal))]
	void OpenPersonal() {
		if (SelectedPersonal == null) return;
		OpenSaved(SelectedPersonal);
	}

	bool CanOpenPersonal() => SelectedPersonal != null;

	/// <summary>保存SQLを開く。会話は新しくし、そのSQLを会話での修正の出発点にする。AIは呼ばない</summary>
	void OpenSaved(AiQuerySaved saved) {
		List<AiQueryParam> parameters;
		try {
			parameters = string.IsNullOrWhiteSpace(saved.SqlParams) ? [] : Common.DeserializeObject<List<AiQueryParam>>(saved.SqlParams) ?? [];
		}
		catch (Exception ex) {
			ShowError($"保存されたパラメータを読めません: {ex.Message}");
			return;
		}
		ResetConversation();
		var draft = new AiQuerySqlDraft { Sql = saved.SqlText, Parameters = parameters, Title = saved.Title, Description = saved.Description };
		loadedShared = null;
		SetDraft(draft, conditionRevision, validated: false);
		baseSql = draft;
		SqlMessage = "保存SQLを開きました。［実行］で検査して実行します。修正したい内容は会話で入力できます。";
		StatusText = "保存SQLを開きました。修正したい場合は内容を入力して送信してください。";
		DetailTabIndex = 0;
		if (Parameters.Any(p => p.AskOnRun)) SqlMessage += " 実行時に入力するパラメータがあります。";
	}

	[RelayCommand(CanExecute = nameof(CanOpenShared))]
	async Task DeleteShared(CancellationToken ct) {
		if (SelectedShared is not SysAiQuery target) return;
		if (MessageEx.ShowQuestionDialog($"共有の「{target.Title}」を削除しますか？", "全員の一覧から削除されます。", ActiveWindow) != MessageBoxResult.Yes) return;
		IsBusy = true;
		try {
			var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
				Flag = CvFlag.Msg201_Op_Execute, DataType = typeof(DeleteParam), DataMsg = Common.SerializeObject(new DeleteParam(typeof(SysAiQuery), Common.SerializeObject(target))),
			}, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) Warn("他の端末で更新・削除されています。再読込します。");
			else if (reply.Code < 0) throw new InvalidOperationException(reply.Option ?? reply.DataMsg);
			if (loadedShared?.Id == target.Id) loadedShared = null;
			await LoadSharedAsync(ct);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { ShowError($"共有保存の削除失敗: {ex.Message}"); }
		finally { IsBusy = false; }
	}

	[RelayCommand(CanExecute = nameof(CanOpenPersonal))]
	void DeletePersonal() {
		if (SelectedPersonal is not AiQuerySaved target) return;
		if (MessageEx.ShowQuestionDialog($"個人保存の「{target.Title}」を削除しますか？", owner: ActiveWindow) != MessageBoxResult.Yes) return;
		try {
			var items = AiQueryPersonalStore.Load();
			// レコードの値比較で同じ内容の1件だけを消す
			var index = items.FindIndex(x => x == target);
			if (index >= 0) items.RemoveAt(index);
			AiQueryPersonalStore.Save(items);
			LoadPersonal();
		}
		catch (Exception ex) { ShowError($"個人保存の削除失敗: {ex.Message}"); }
	}

	/// <summary>個人保存をファイルへ書き出す（端末移行用）</summary>
	[RelayCommand]
	void ExportPersonal() {
		var dialog = new SaveFileDialog {
			Title = "個人保存のエクスポート", Filter = "JSONファイル (*.json)|*.json", DefaultExt = ".json",
			FileName = $"AI照会_個人保存_{DateTime.Now:yyyyMMdd}.json",
		};
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		try {
			AiQueryPersonalStore.Save(AiQueryPersonalStore.Load(), dialog.FileName);
			SavedMessage = $"エクスポートしました: {dialog.FileName}";
		}
		catch (Exception ex) { ShowError($"エクスポート失敗: {ex.Message}"); }
	}

	/// <summary>エクスポートしたファイルを読み込み、個人保存へ追加する（既存は上書きしない）</summary>
	[RelayCommand]
	void ImportPersonal() {
		var dialog = new OpenFileDialog { Title = "個人保存のインポート", Filter = "JSONファイル (*.json)|*.json" };
		if (dialog.ShowDialog(ActiveWindow) != true) return;
		try {
			var imported = AiQueryPersonalStore.Load(dialog.FileName);
			var items = AiQueryPersonalStore.Load();
			var added = imported.Where(x => !items.Contains(x)).ToList();
			items.AddRange(added);
			AiQueryPersonalStore.Save(items);
			LoadPersonal();
			SavedMessage = $"{added.Count:N0}件をインポートしました（同じ内容の{imported.Count - added.Count:N0}件は追加していません）。";
		}
		catch (Exception ex) { ShowError($"インポート失敗（個人保存は変更していません）: {ex.Message}"); }
	}

	// ---- 共通 -----------------------------------------------------------------

	void NotifySqlCommands() {
		OnPropertyChanged(nameof(IsSqlCurrent));
		RunCommand.NotifyCanExecuteChanged();
		SaveSqlFileCommand.NotifyCanExecuteChanged();
		SaveSharedCommand.NotifyCanExecuteChanged();
		SavePersonalCommand.NotifyCanExecuteChanged();
	}

	static async Task<CvMsg> SendAsync(CvFlag flag, object param, ProtoBuf.Grpc.CallContext context, bool allowNotFound = false) {
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0, Flag = flag, DataType = param.GetType(), DataMsg = Common.SerializeObject(param),
		}, context);
		if (reply.Code < 0 && !(allowNotFound && reply.Code == CvMsgErrorCode.NotFound)) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? $"サーバエラー {reply.Code}");
		}
		return reply;
	}

	/// <summary>個人保存に記録するログインId（JWTの SerialNumber = SysLogin.Id）。参考値で権限には使わない</summary>
	static long ResolveLoginId() {
		try {
			var parts = (AppGlobal.LoginJwt ?? string.Empty).Split('.');
			if (parts.Length < 2) return 0;
			var payload = parts[1].Replace('-', '+').Replace('_', '/');
			payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
			var json = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
			var serial = json.Properties().FirstOrDefault(p => p.Name.EndsWith("/serialnumber", StringComparison.OrdinalIgnoreCase))?.Value.ToString();
			return long.TryParse(serial, out var id) ? id : 0;
		}
		catch (FormatException) { return 0; }
		catch (Newtonsoft.Json.JsonException) { return 0; }
	}

	static string SanitizeFileName(string name) {
		var invalid = Path.GetInvalidFileNameChars();
		return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
	}

	void Warn(string message) => MessageEx.ShowWarningDialog(message, owner: ActiveWindow);

	void ShowError(string message) => MessageEx.ShowErrorDialog(message, owner: ActiveWindow);

	/// <summary>画面を閉じるときは応答待ちを取り消す</summary>
	protected override void OnExit() {
		chatCts?.Cancel();
		base.OnExit();
	}
}
