using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels;

public partial class SampleViewModel : Helpers.BaseViewModel {
	// コンストラクタ内でデザイン時を判定して回避
	public SampleViewModel() {
		// デザイン時はここで終了
		if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
			return;
		// 実行時のみの初期化処理...
	}

	/// <summary>
	/// カラーバランスの見本
	/// </summary>
	[ObservableProperty]
	private ObservableCollection<ColorBalanceItem> colorItems = new()
	{
		new("#212025", "#E74545", "#742424","#FFFFFF"),
		new("#4B3A42", "#F38884", "#A45055","#FFFFFF"),
		new("#180A14", "#91242C", "#501919","Transparent"),
		new("#FFFBF2", "#876931", "#E7DECB","Transparent"),
		new("#FFF4E1", "#C6A260", "#F0E1CC","Transparent"),
		new("#72563B", "#392F28", "#866E5A","Transparent"),
		new("#65D0C4", "#FF4F84", "#B4EC38","Transparent"),
		new("#FFCA3E", "#FF8D89", "#C9E9E5","Transparent"),
		new("#C8F0EB", "#F2E073", "#FF90BE","Transparent"),
		new("#46938A", "#BA8C27", "#B7255A","Transparent"),
		new("#AF949B", "#E95774", "#9C6372","#FFFFFF"),
		new("#EEE9E4", "#D75A00", "#FAD61B","Transparent"),
		new("#F8B97E", "#FBFBF8", "#C5BEB6","Transparent"),
		new("#58C6F1", "#EBD6BC", "#F2B134","#227190"),
		new("#FFE4E4", "#E22B26", "#FFCBBD","#EF593C"),
		new("#F1F2EF", "#EFEC49", "#33933A","#D2D6C1"),
		new("#DCF0F8", "#2A61B3", "#FFFFFF","#B7E0E4"),
		new("#131A34", "#2F2F2F", "#F5F5F5","#ADFF00"),
		new("#F2F2F2", "#95A4B7", "#5B5F6A","#FFFFFF"),
		new("#FCE07E", "#323232", "#D4F170","#F4E5B3"),
		new("#DEF1EF", "#BAE6DF", "#FFDED5","#EBA295"),
		new("#FDFEE1", "#C9E3B4", "#F7C8B1","#EEEE89"),
		new("#D3E4F4", "#424F7A", "#DCD4E6","#E9DCED"),
		new("#019AA7", "#F5BBB9", "#F5D235","Transparent"),
		new("#5449EA", "#FE4A87", "#FE4137","Transparent"),
		new("#E5C1CC", "#FC6B91", "#BC8496","Transparent"),
		new("#725661", "#93344A", "#4C1E2D","Transparent"),
		new("Transparent", "Transparent", "Transparent","Transparent"),
	};

	#region テストメッセージ 001, 002
	[ObservableProperty]
	string testMsg001Text = $"テストメッセージ {DateTime.Now}";
	[ObservableProperty]
	string testMsg001Result = string.Empty;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg001(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg001_CopyReply };
		msg.DataType = typeof(string);
		msg.DataMsg = TestMsg001Text;
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		TestMsg001Result = reply.DataMsg;
	}

	[ObservableProperty]
	string testMsg002Result = string.Empty;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg002(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg002_GetVersion };
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply?.DataMsg != null && reply?.DataType != null) {
			var versionInfo = Common.DeserializeObject<CvBase.Share.InfoServer>(reply.DataMsg);
			// 表示用に整形、取得した情報をすべて出す
			TestMsg002Result = $"{versionInfo?.Product}-{versionInfo?.BuildDate} Ver.{versionInfo?.Version} Base:{versionInfo?.BaseDir} Machine:{versionInfo?.MachineName} User:{versionInfo?.UserName} OS:{versionInfo?.OsVersion} DotNet:{versionInfo?.DotNetVersion}";
		}
	}
	#endregion


}

/// <summary>
/// カラーバランス確認用アイテムモデル
/// </summary>
public record ColorBalanceItem(string Color1, string Color2, string Color3, string Color4);

public record EnvDisplayItem(string Key, string Value);


