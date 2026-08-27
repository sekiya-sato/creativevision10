using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class ConvertSelectedViewModel : BaseViewModel {
	[ObservableProperty]
	public partial bool IsInitDb { get; set; }

	[ObservableProperty]
	public partial bool IsRunning { get; set; }

	/// <summary>
	/// 一度でも実行を開始したか。誤って再実行することを防ぐため、実行開始後は
	/// <see cref="ExecuteCommand"/> を無効のままにする（ウィンドウを開き直すまで再実行不可）。
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial bool HasExecuted { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<ConvertTaskItem> Tasks { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<string> StreamMessages { get; set; } = [];

	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		try {
			HasExecuted = false;
			Tasks.Clear();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg041_ConvertList,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code != 0) {
				MessageEx.ShowErrorDialog($"変換プログラム一覧の取得に失敗しました。\n{reply.Option}", owner: ClientLib.GetActiveView(this));
				return;
			}
			var taskNames = Common.DeserializeObject<List<string>>(reply.DataMsg) ?? [];
			foreach (var name in taskNames) {
				Tasks.Add(new ConvertTaskItem(name));
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"変換プログラム一覧の取得中にエラーが発生しました。\n{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	private void SelectAll() {
		foreach (var task in Tasks) {
			task.IsSelected = true;
		}
	}

	[RelayCommand]
	private void ClearSelection() {
		foreach (var task in Tasks) {
			task.IsSelected = false;
		}
	}

	private bool CanExecute() => !HasExecuted;

	[RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		var selectedTasks = Tasks.Where(t => t.IsSelected).Select(t => t.Name).ToList();
		if (selectedTasks.Count == 0) {
			MessageEx.ShowWarningDialog("変換プログラムを選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog("選択した変換プログラムを実行しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		if (IsRunning) {
			return;
		}
		try {
			IsRunning = true;
			// 実行を開始した時点で確定的に無効化する（キャンセル・エラーで終わっても再実行させない。
			// サーバ側で一部書き込みが進んでいる可能性があるため、再実行はウィンドウの開き直しを要求する）
			HasExecuted = true;
			ProgressValue = 0;
			StreamMessages.Clear();
			cancellationToken.ThrowIfCancellationRequested();

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg040_ConvertDb,
				DataType = typeof(ConvertSelectedDbParam),
				DataMsg = Common.SerializeObject(new ConvertSelectedDbParam(IsInitDb, selectedTasks))
			};

			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StreamMessages.Insert(0, streamMsg.DataMsg);
				}
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) {
					break;
				}
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		finally {
			IsRunning = false;
		}
	}
}

public partial class ConvertTaskItem : ObservableObject {
	[ObservableProperty]
	public partial bool IsSelected { get; set; }

	public string Name { get; }

	/// <summary>プログラム名 + 補足名（一覧表示用）。実行時の送信には <see cref="Name"/> を使う。</summary>
	public string DisplayName { get; }

	public ConvertTaskItem(string name) {
		Name = name;
		DisplayName = ConvertTaskDisplayNames.GetDisplayName(name);
	}
}

/// <summary>
/// 変換プログラム名(<see cref="ConvertDb"/>の<c>_stepDefinitions</c>のName)に対する画面表示用の補足名。
/// <para>
/// Tran系は末尾の(NN)が旧伝票処理区分。<c>ConvertDbTran.cs</c>の<c>ConvertTranHeadersByRange</c>への実引数に基づく
/// （関数名の数字とずれるものがある。例: CnvTran61Choseiの実際の旧区分は18）。
/// </para>
/// 辞書に無いNameはそのまま表示するため、変換プログラムが追加されても表示は壊れない。
/// </summary>
internal static class ConvertTaskDisplayNames {
	private static readonly Dictionary<string, string> _labels = new() {
		["CnvMasterConfig"] = "設定マスタ",
		["CnvMasterSys"] = "システム管理マスタ",
		["CnvMasterMeisho"] = "名称マスタ",
		["CnvMasterShain"] = "社員マスタ",
		["CnvMasterEndCustomer"] = "顧客マスタ",
		["CnvMasterShohin"] = "商品マスタ",
		["CnvMasterTokui"] = "得意先マスタ",
		["CnvMasterShiire"] = "仕入先マスタ",
		["CnvMasterMaterial"] = "生地・付属マスタ",
		["CnvAfterMaster"] = "マスタ紐付け後処理",
		["CnvAfterMasterAddress"] = "マスタ住所正規化後処理",
		["CnvTran00HonUri"] = "本部売上データ(00)",
		["CnvTran01TenUri"] = "店舗売上データ(01)",
		["CnvTran02Material"] = "生地・付属仕入データ(02)",
		["CnvTran03Shiire"] = "仕入データ(03)",
		["CnvTran05Ido"] = "移動データ(05)",
		["CnvTran06Nyukin"] = "入金データ(06)",
		["CnvTran07Shiharai"] = "支払データ(07)",
		["CnvTran60Tana"] = "棚卸データ(60)",
		["CnvTran61Chosei"] = "在庫調整データ(18)",
		["CnvTran10Ido"] = "積送移動出データ(10)",
		["CnvTran11IdoIn"] = "積送移動入データ(11)",
		["CnvTran12Jyuchu"] = "受注データ(12)",
		["CnvTran13Hachu"] = "発注データ(13)",
	};

	public static string GetDisplayName(string name) =>
		_labels.TryGetValue(name, out var label) ? $"{name}  {label}" : name;
}
