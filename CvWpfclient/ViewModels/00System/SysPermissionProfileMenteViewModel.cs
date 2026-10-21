using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

/// <summary>
/// 権限プロファイル設定（Doc/spec/2026-10-20_メニュー実行権限と個人メニュー_最小変更設計案.md §3.2）。
/// <para>
/// プロファイルの追加・名称変更・有効/無効と、メニューごとの実行可否（既定/許可/禁止）を設定する。
/// メニューは <see cref="MenuData.CreateDefault"/> をそのまま一覧にし、同じ機能Idの行は連動させる。
/// 保存は <see cref="PermissionProfileSaveParam"/> で1トランザクションの洗い替え。反映は対象者の次回ログインから。
/// </para>
/// </summary>
public partial class SysPermissionProfileMenteViewModel : BaseViewModel {
	[ObservableProperty]
	public partial string Title { get; set; } = "権限プロファイル設定";

	[ObservableProperty]
	public partial ObservableCollection<SysPermissionProfile> Profiles { get; set; } = [];

	[ObservableProperty]
	public partial SysPermissionProfile? SelectedProfile { get; set; }

	/// <summary>編集中のプロファイル（一覧の行とは別のコピー。Id=0は新規）</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsFullAccess), nameof(CanEditRules), nameof(EditHeader))]
	public partial SysPermissionProfile? EditProfile { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<PermissionMenuRow> MenuRows { get; set; } = [];

	[ObservableProperty]
	public partial PermissionMenuRow? SelectedRow { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	public partial bool IsDirty { get; set; }

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	/// <summary>明細に無いメニューの扱い（システム管理マスタの業務権限の既定ポリシー）</summary>
	[ObservableProperty]
	public partial string DefaultModeText { get; set; } = string.Empty;

	/// <summary>設定欄の選択肢。<see cref="PermissionMenuRow.Setting"/> の値と並びを合わせる</summary>
	public IReadOnlyList<string> SettingItems { get; } = PermissionMenuRow.SettingNames;

	public bool IsFullAccess => EditProfile?.Id == SysPermissionProfile.FullAccessId;
	public bool CanEditRules => EditProfile != null && !IsFullAccess;
	public string EditHeader => EditProfile == null ? string.Empty
		: EditProfile.Id == 0 ? "新規プロファイル"
		: IsFullAccess ? $"Id:{EditProfile.Id}（全メニュー実行可・変更不可）"
		: $"Id:{EditProfile.Id}";

	EnumPermissionDefaultMode defaultMode;
	/// <summary>一覧の選択を戻すときに、破棄確認をもう一度出さないためのフラグ</summary>
	bool suppressSelection;

	[RelayCommand]
	private async Task Init() => await ReloadAsync(null);

	/// <summary>一覧・既定ポリシーを読み直す（F5）</summary>
	[RelayCommand]
	private async Task Reload() {
		if (!ConfirmDiscard()) return;
		await ReloadAsync(EditProfile?.Id);
	}

	async Task ReloadAsync(long? selectId) {
		IsBusy = true;
		try {
			var sysman = (await QueryAsync<MasterSysman>(new QueryListParam(typeof(MasterSysman)))).FirstOrDefault();
			defaultMode = sysman?.EnPermissionDefaultMode ?? EnumPermissionDefaultMode.Audit;
			DefaultModeText = $"明細の無いメニュー（設定「既定」）: {FormatDefaultMode(defaultMode)}　※システム管理マスタで変更";
			var list = await QueryAsync<SysPermissionProfile>(new QueryListParam(typeof(SysPermissionProfile), null, "Id"));
			suppressSelection = true;
			Profiles = new ObservableCollection<SysPermissionProfile>(list);
			SelectedProfile = Profiles.FirstOrDefault(x => x.Id == selectId) ?? Profiles.FirstOrDefault();
			suppressSelection = false;
			await LoadProfileAsync(SelectedProfile);
			Message = $"プロファイル {Profiles.Count} 件を読み込みました。";
		}
		catch (Exception ex) {
			Message = $"読込失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			suppressSelection = false;
			IsBusy = false;
		}
	}

	partial void OnSelectedProfileChanged(SysPermissionProfile? oldValue, SysPermissionProfile? newValue) {
		if (suppressSelection || newValue == null || newValue.Id == EditProfile?.Id) return;
		if (!ConfirmDiscard()) {
			// 選択の確定後に戻さないと DataGrid 側の表示が追従しない
			Application.Current.Dispatcher.BeginInvoke(() => {
				suppressSelection = true;
				SelectedProfile = oldValue;
				suppressSelection = false;
			});
			return;
		}
		_ = LoadProfileWithMessageAsync(newValue);
	}

	async Task LoadProfileWithMessageAsync(SysPermissionProfile profile) {
		IsBusy = true;
		try {
			await LoadProfileAsync(profile);
		}
		catch (Exception ex) {
			Message = $"読込失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	/// <summary>プロファイルの明細を読み、メニュー一覧へ反映する。null は新規（全行「既定」）</summary>
	async Task LoadProfileAsync(SysPermissionProfile? profile) {
		var rules = new Dictionary<string, bool>(StringComparer.Ordinal);
		if (profile != null && profile.Id > 0) {
			// Idは数値なので条件へ直接埋め込む（文字列パラメータでは数値列と一致しないため）
			var details = await QueryAsync<SysPermissionProfileDetail>(new QueryListParam(typeof(SysPermissionProfileDetail),
				$"Id_PermissionProfile={profile.Id} and PermissionType={(int)EnumPermissionType.View}"));
			foreach (var row in details) {
				rules[row.FunctionId] = row.IsAllowed;
			}
		}
		EditProfile = profile == null ? new SysPermissionProfile { IsActive = true } : Common.CloneObject(profile);
		MenuRows = BuildRows(rules);
		IsDirty = false;
	}

	/// <summary>メニュー定義を字下げ付きの1列に並べる。同じ機能Idの行は1つの設定を共有する</summary>
	ObservableCollection<PermissionMenuRow> BuildRows(Dictionary<string, bool> rules) {
		var rows = new ObservableCollection<PermissionMenuRow>();
		var byFunction = new Dictionary<string, List<PermissionMenuRow>>(StringComparer.Ordinal);
		void Add(IEnumerable<MenuData> nodes, int level, List<PermissionMenuRow> ancestors) {
			foreach (var node in nodes) {
				if (node.SubItems != null) {
					var folder = new PermissionMenuRow(this, node.Header, null, level);
					rows.Add(folder);
					Add(node.SubItems, level + 1, [.. ancestors, folder]);
					continue;
				}
				if (!node.IsExecutable || node.FunctionId is not string functionId) continue;
				var setting = rules.TryGetValue(functionId, out var allowed)
					? (allowed ? PermissionMenuRow.Allow : PermissionMenuRow.Deny)
					: PermissionMenuRow.Default;
				var leaf = new PermissionMenuRow(this, node.Header, functionId, level, setting);
				rows.Add(leaf);
				foreach (var folder in ancestors) folder.Leaves.Add(leaf);
				if (!byFunction.TryGetValue(functionId, out var same)) byFunction[functionId] = same = [];
				same.Add(leaf);
			}
		}
		Add(MenuData.CreateDefault(), 0, []);
		foreach (var same in byFunction.Values) {
			foreach (var row in same) row.SameFunction = same;
		}
		return rows;
	}

	/// <summary>行の設定が変わった（<see cref="PermissionMenuRow"/> から呼ぶ）</summary>
	internal void OnRowSettingChanged() => IsDirty = true;

	/// <summary>「既定」行の結果表示</summary>
	internal string DefaultResultText => defaultMode == EnumPermissionDefaultMode.Deny ? "不可（既定）" : "可（既定）";

	[RelayCommand]
	private void SetAllow() => ApplyToSelected(PermissionMenuRow.Allow);

	[RelayCommand]
	private void SetDeny() => ApplyToSelected(PermissionMenuRow.Deny);

	[RelayCommand]
	private void SetDefault() => ApplyToSelected(PermissionMenuRow.Default);

	/// <summary>選択行がフォルダなら配下の全メニュー、メニューならその行へ設定を入れる</summary>
	void ApplyToSelected(int setting) {
		if (!CanEditRules || SelectedRow == null) {
			Message = "設定する行（フォルダまたはメニュー）を選んでください。";
			return;
		}
		var targets = SelectedRow.IsFolder ? SelectedRow.Leaves : [SelectedRow];
		foreach (var row in targets) row.Setting = setting;
		Message = $"{targets.Count} 件に「{PermissionMenuRow.SettingNames[setting]}」を設定しました（未保存）。";
	}

	[RelayCommand]
	private async Task NewProfile() {
		if (!ConfirmDiscard()) return;
		suppressSelection = true;
		SelectedProfile = null;
		suppressSelection = false;
		await LoadProfileAsync(null);
		Message = "新規プロファイルのコード・名称を入力し、メニューの許可/禁止を設定して保存してください。";
	}

	/// <summary>編集欄の変更（コード・名称・説明・有効）も未保存として扱う</summary>
	partial void OnEditProfileChanged(SysPermissionProfile? oldValue, SysPermissionProfile? newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= EditProfile_PropertyChanged;
		if (newValue != null) newValue.PropertyChanged += EditProfile_PropertyChanged;
	}

	void EditProfile_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => IsDirty = true;

	bool CanSave() => IsDirty && !IsBusy;

	[RelayCommand(CanExecute = nameof(CanSave))]
	private async Task Save() {
		if (EditProfile == null) return;
		var rows = IsFullAccess ? [] : MenuRows
			.Where(r => !r.IsFolder && r.Setting != PermissionMenuRow.Default)
			.DistinctBy(r => r.FunctionId)
			.Select(r => new SysPermissionProfileDetail { FunctionId = r.FunctionId!, IsAllowed = r.Setting == PermissionMenuRow.Allow })
			.ToArray();
		var param = new PermissionProfileSaveParam(EditProfile, rows);
		IsBusy = true;
		try {
			var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(PermissionProfileSaveParam),
				DataMsg = Common.SerializeObject(param),
			}, AppGlobal.GetDefaultCallContext(CancellationToken.None));
			if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
				MessageEx.ShowWarningDialog("他の端末で更新されています。再読込してから設定し直してください。", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (reply.Code == CvMsgErrorCode.InvalidParameter) {
				MessageEx.ShowWarningDialog(reply.Option ?? reply.DataMsg ?? "入力内容に誤りがあります。", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (reply.Code < 0) throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? $"サーバエラー {reply.Code}");
			var saved = Common.DeserializeObject<SysPermissionProfile>(reply.DataMsg ?? "{}");
			IsDirty = false;
			IsBusy = false;
			await ReloadAsync(saved?.Id);
			Message = $"保存しました（明細 {rows.Length} 件）。対象社員の次回ログインから反映されます。";
		}
		catch (Exception ex) {
			Message = $"保存失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	partial void OnIsBusyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

	/// <returns>続けてよい（未保存が無い、または破棄を選んだ）。画面を閉じるときも View から呼ぶ</returns>
	internal bool ConfirmDiscard() =>
		!IsDirty || MessageEx.ShowQuestionDialog("保存していない変更を破棄しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes;

	static string FormatDefaultMode(EnumPermissionDefaultMode mode) => mode switch {
		EnumPermissionDefaultMode.Warn => "警告付き許可（実行可）",
		EnumPermissionDefaultMode.Deny => "不可（許可したメニューだけ実行可）",
		_ => "監査のみ（実行可）",
	};

	static async Task<List<T>> QueryAsync<T>(QueryListParam param) {
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param),
		}, AppGlobal.GetDefaultCallContext(CancellationToken.None));
		if (reply.Code == CvMsgErrorCode.NotFound) return [];
		if (reply.Code < 0) throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? $"サーバエラー {reply.Code}");
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? [.. list.Cast<T>()] : [];
	}
}

/// <summary>
/// 権限プロファイル設定のメニュー1行（フォルダまたはメニュー）
/// </summary>
public partial class PermissionMenuRow : ObservableObject {
	public const int Default = 0;
	public const int Allow = 1;
	public const int Deny = 2;
	public static readonly IReadOnlyList<string> SettingNames = ["既定", "許可", "禁止"];

	readonly SysPermissionProfileMenteViewModel owner;

	public PermissionMenuRow(SysPermissionProfileMenteViewModel owner, string header, string? functionId, int level, int setting = Default) {
		this.owner = owner;
		Header = header;
		FunctionId = functionId;
		Level = level;
		Setting = setting;
	}

	public string Header { get; }
	/// <summary>機能Id。フォルダは null</summary>
	public string? FunctionId { get; }
	public int Level { get; }
	public bool IsFolder => FunctionId == null;
	/// <summary>字下げ（表示用）</summary>
	public Thickness Indent => new(Level * 18, 0, 0, 0);
	/// <summary>フォルダ配下の全メニュー行（一括設定用）</summary>
	public List<PermissionMenuRow> Leaves { get; } = [];
	/// <summary>同じ機能Idの行（ロール別ショートカットと標準メニュー）。設定を連動させる</summary>
	internal List<PermissionMenuRow> SameFunction { get; set; } = [];

	/// <summary>0=既定 1=許可 2=禁止</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ResultText))]
	public partial int Setting { get; set; }

	partial void OnSettingChanged(int oldValue, int newValue) {
		// 行の再利用などで ComboBox が選択なし(-1)を書き戻しても採用しない（-1 は保存時に「禁止」扱いになるため）
		if (newValue is < Default or > Deny) {
			restoring = true;
			Setting = oldValue;
			restoring = false;
			return;
		}
		if (restoring || SameFunction.Count == 0) return; // 不正値からの復元中・一覧の組立中
		foreach (var row in SameFunction.Where(r => r != this && r.Setting != newValue)) {
			row.Setting = newValue;
		}
		owner.OnRowSettingChanged();
	}

	/// <summary>不正値を元へ戻している間は、連動・未保存扱いにしない</summary>
	bool restoring;

	/// <summary>この設定での実行可否</summary>
	public string ResultText => IsFolder ? string.Empty : Setting switch {
		Allow => "可",
		Deny => "不可",
		_ => owner.DefaultResultText,
	};
}
