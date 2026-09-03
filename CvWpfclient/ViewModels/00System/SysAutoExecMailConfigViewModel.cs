using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvWpfclient.Helpers;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysAutoExecMailConfigViewModel : Helpers.BaseViewModel {
	private readonly GrpcChannel _schedulerChannel;
	private readonly ISchedulerService _schedulerClient;

	[ObservableProperty]
	public partial string Title { get; set; } = "自動実行メール設定";

	[ObservableProperty]
	public partial string Server { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Port { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<string> SecurityValues { get; set; } = new();

	[ObservableProperty]
	public partial string SelectedSecurity { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<string> AuthModeValues { get; set; } = new();

	[ObservableProperty]
	public partial string SelectedAuthMode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string UserId { get; set; } = string.Empty;

	/// <summary>新しいパスワード。空文字なら保存済みの値を変更しない。</summary>
	[ObservableProperty]
	public partial string Credential { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string FromAddress { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string FromName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ToAddress { get; set; } = string.Empty;

	/// <summary>パスワードが登録済みかどうか（値自体はサーバから返らない）</summary>
	[ObservableProperty]
	public partial bool HasCredential { get; set; }

	/// <summary>パスワード登録状況の表示文言</summary>
	[ObservableProperty]
	public partial string CredentialStatus { get; set; } = string.Empty;

	/// <summary>保存済みのパスワードを消去する</summary>
	[ObservableProperty]
	public partial bool ClearCredential { get; set; }

	/// <summary>現在の設定でメール送信できるかどうかの説明</summary>
	[ObservableProperty]
	public partial string ValidationMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>認証方式が None 以外のとき、ユーザーID／パスワード入力を必須にする</summary>
	[ObservableProperty]
	public partial bool IsCredentialRequired { get; set; }

	public SysAutoExecMailConfigViewModel() {
		_schedulerChannel = CreateSchedulerChannel();
		_schedulerClient = _schedulerChannel.CreateGrpcService<ISchedulerService>();
	}

	protected override void OnExit() {
		_schedulerChannel.Dispose();
		base.OnExit();
	}

	private static GrpcChannel CreateSchedulerChannel() {
		var socketsHandler = new SocketsHttpHandler {
			PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
			KeepAlivePingDelay = TimeSpan.FromSeconds(60),
			KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
			EnableMultipleHttp2Connections = true,
			KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
		};

		HttpMessageHandler handler = socketsHandler;
		var subPath = Common.ExtractSubPath(AppGlobal.Url);
		if (!string.IsNullOrEmpty(subPath)) {
			handler = new GrpcSubPathHandler(subPath) {
				InnerHandler = handler,
			};
		}

		var httpClient = new HttpClient(handler) {
			Timeout = Timeout.InfiniteTimeSpan,
		};
		return GrpcChannel.ForAddress(AppGlobal.Url, new GrpcChannelOptions {
			HttpClient = httpClient,
		});
	}

	[RelayCommand]
	public async Task Init() {
		await LoadAsync();
	}

	/// <summary>認証方式が None 以外なら、ユーザーID／パスワードを必須項目として有効化する</summary>
	partial void OnSelectedAuthModeChanged(string value) {
		IsCredentialRequired = !string.IsNullOrEmpty(value) && value != "None";
	}

	/// <summary>設定を再読込する。F5 / 再読込ボタン用。</summary>
	[RelayCommand]
	private async Task LoadAsync() {
		IsBusy = true;
		ValidationMessage = "読込中...";
		try {
			var response = await _schedulerClient.GetAutoExecMailConfigAsync(AppGlobal.GetDefaultCallContext());
			if (response.Result != 0) {
				ValidationMessage = response.Detail;
				MessageEx.ShowErrorDialog(response.Detail, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}

			Server = response.Config.Server;
			Port = response.Config.Port;
			SecurityValues = new ObservableCollection<string>(response.SecurityValues);
			SelectedSecurity = response.Config.Security;
			AuthModeValues = new ObservableCollection<string>(response.AuthModeValues);
			SelectedAuthMode = response.Config.AuthMode;
			UserId = response.Config.UserId;
			FromAddress = response.Config.FromAddress;
			FromName = response.Config.FromName;
			ToAddress = response.Config.ToAddress;

			HasCredential = response.HasCredential;
			CredentialStatus = HasCredential ? "登録済み（変更する場合だけ入力してください）" : "未登録";
			Credential = string.Empty;
			ClearCredential = false;

			ValidationMessage = response.ValidationDetail;
		}
		catch (Exception ex) {
			ValidationMessage = $"取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(ValidationMessage, owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task SaveAsync() {
		if (string.IsNullOrWhiteSpace(ToAddress)) {
			MessageEx.ShowWarningDialog("送信先アドレスを入力してください。", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}
		if (!int.TryParse(Port?.Trim(), out var portNumber) || portNumber is < 1 or > 65535) {
			MessageEx.ShowWarningDialog("ポート番号は1～65535の数字で入力してください。", owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}

		var confirm = MessageEx.ShowQuestionDialog("自動実行メールの設定を保存します。よろしいですか？", owner: Helpers.ClientLib.GetActiveView(this));
		if (confirm != MessageBoxResult.Yes) return;

		var request = new SetAutoExecMailConfigRequest {
			Config = new AutoExecMailConfig {
				Server = Server,
				Port = Port,
				Security = SelectedSecurity,
				AuthMode = SelectedAuthMode,
				UserId = UserId,
				FromAddress = FromAddress,
				FromName = FromName,
				ToAddress = ToAddress,
			},
			Credential = Credential,
			ClearCredential = ClearCredential,
		};

		IsBusy = true;
		try {
			var result = await _schedulerClient.SetAutoExecMailConfigAsync(request, AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				MessageEx.ShowErrorDialog(result.Detail, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			MessageEx.ShowInformationDialog(result.Detail, owner: Helpers.ClientLib.GetActiveView(this));
			await LoadAsync();
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"保存失敗: {ex.Message}", owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task TestSendAsync() {
		var confirm = MessageEx.ShowQuestionDialog(
			"保存済みの設定でテストメールを送信します。\n未保存の変更がある場合は反映されません。\nよろしいですか？",
			owner: Helpers.ClientLib.GetActiveView(this));
		if (confirm != MessageBoxResult.Yes) return;

		IsBusy = true;
		try {
			var result = await _schedulerClient.TestSendAutoExecMailAsync(AppGlobal.GetDefaultCallContext());
			if (result.Result != 0) {
				MessageEx.ShowErrorDialog(result.Detail, owner: Helpers.ClientLib.GetActiveView(this));
				return;
			}
			MessageEx.ShowInformationDialog(result.Detail, owner: Helpers.ClientLib.GetActiveView(this));
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"テスト送信失敗: {ex.Message}", owner: Helpers.ClientLib.GetActiveView(this));
		}
		finally {
			IsBusy = false;
		}
	}
}
