using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;


namespace Cvnet10Wpfclient.ViewModels;

internal partial class SysLoginViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string title = "ログインマスターメンテ画面";

	[ObservableProperty]
	DateTime startTime = DateTime.Now;
	[ObservableProperty]
	TimeSpan getListTime = TimeSpan.Zero;

	Type Tabletype = typeof(SysLogin);
	[RelayCommand]
	public async Task Init() {
	}
    /// <summary>
    /// 表示リスト
    /// [Display list]
    /// </summary>
    [ObservableProperty]
	public ObservableCollection<SysLogin> listData = [];
    /// <summary>
    /// 現在の選択行
    /// [Currently selected row]
    /// </summary>
    [ObservableProperty]
	public SysLogin current = new ();

	partial void OnCurrentChanged(SysLogin? oldValue, SysLogin newValue) {
		if (newValue == null) {
			CurrentEdit = new();
			return;
		}
		if (oldValue?.Id != newValue?.Id) {
			CurrentEdit = Common.CloneObject<SysLogin>(Current);
			LastLoginDate = $"{CurrentEdit.LastDate.ToDateStr()}";
		}
	}
    /// <summary>
    /// 修正用アイテム
    /// [Item for modification]
    /// </summary>
    [ObservableProperty]
	public SysLogin currentEdit = new ();

	[ObservableProperty]
	string lastLoginDate = string.Empty;

	[ObservableProperty]
	public long count = 0;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task DoList(CancellationToken ct) {
		StartTime = DateTime.Now;
		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: Tabletype
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;

			if (list != null) {
				ListData = new ObservableCollection<SysLogin>(list.Cast<SysLogin>());
				Count = ListData.Count;
				Current = ListData.First()??new SysLogin();
			}
			GetListTime = DateTime.Now - StartTime;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	public void DoSelectShain() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterShain), "");
		vm.InitParam = (int)Current.Id_Shain;
		var ret = ClientLib.ShowDialogView(selWin, this);
		if (ret != true) return;
		var meisho = vm.Current as MasterShain;
		var cur = CurrentEdit as SysLogin;
		if (meisho == null || cur == null) return;
		cur.Id_Shain = meisho.Id;
		cur.Code_Shain = meisho.Code;
		cur.Mei_Shain = meisho.Name;
	}
	[RelayCommand]
	public void DoSetPass() {
		var cur = CurrentEdit as SysLogin;
		if (cur == null) return;
		var warning = (cur.CryptPassword.Length >= 20) ? "すでに暗号化されているようですが、\n": "";
		if (MessageEx.ShowQuestionDialog(warning+"表示されているパスワードの文字を暗号化しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
			return;
		var newPass = Common.EncryptLoginRequest(cur.CryptPassword, cur.VdateC);
		if(!string.IsNullOrEmpty(newPass))
			cur.CryptPassword = newPass;
	}


	[RelayCommand]
	public void DoOutputJson() {
		if (ListData == null) return;
		var outstr = JsonConvert.SerializeObject(ListData, Formatting.Indented);
		var dialog = new Microsoft.Win32.SaveFileDialog();
		dialog.FileName = Tabletype.Name + DateTime.Now.ToDtStrDate2(); // Default file name
		dialog.DefaultExt = ".json"; // Default file extension
		dialog.Filter = "Text documents (.json)|*.json"; // Filter files by extension
		dialog.DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		// Show save file dialog box
		bool? ret = dialog.ShowDialog(ClientLib.GetActiveView(this));

		// Process save file dialog box results
		if (ret != true) return;
		// Save document
		string filename = dialog.FileName;
		using (StreamWriter writer = new StreamWriter(filename)) {
            // ファイルに書き込む [Write to a file]
            writer.WriteLine(outstr);
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task DoUpdate(CancellationToken ct) {
		if (ListData == null || CurrentEdit == null) return;
		if (MessageEx.ShowQuestionDialog("更新しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
			return;
		try {
			ct.ThrowIfCancellationRequested();
			if (CurrentEdit.Id == 0) 
				return;
			// 処理を実行
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
			msg.DataType = typeof(UpdateParam);
			msg.DataMsg = Common.SerializeObject(new UpdateParam(typeof(SysLogin), Common.SerializeObject(CurrentEdit)));
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			var item = Common.DeserializeObject<SysLogin>(reply.DataMsg ?? "");
			// 修正の場合、特に不要
			if (item != null)
				MessageEx.ShowInformationDialog($"修正しました\nコード: {item.Id}", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task DoDelete(CancellationToken ct) {
		if (ListData.Count>0 && Current.Id >0) {
			if (MessageEx.ShowQuestionDialog("削除しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
				return;
			// 処理を実行
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
			msg.DataType = typeof(DeleteParam);
			msg.DataMsg = Common.SerializeObject(new DeleteByIdParam(typeof(SysLogin), CurrentEdit.Id , Current.Vdu));
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code != 0) {
				MessageEx.ShowErrorDialog("削除できませんでした", owner: ClientLib.GetActiveView(this));
				return;
			}
            // Currentの次のオブジェクトを取得 [Retrieve the next object of the current one]
            var currentIndex = ListData.IndexOf(Current);
			var nextIndex = currentIndex + 1 < ListData.Count ? currentIndex + 1 : 0;
			var nextItem = ListData.ElementAtOrDefault(nextIndex);
			ListData.Remove(Current);
			Current = nextItem ?? ListData.First()?? new SysLogin();
			Count = ListData.Count;
		}
	}
	[RelayCommand]
	public async Task DoInsert(CancellationToken ct) {
		if (ListData != null && CurrentEdit != null) {
			if (MessageEx.ShowQuestionDialog("挿入しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
				return;
			try {
				ct.ThrowIfCancellationRequested();
				// 追加処理を実行
				var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(InsertParam);
				msg.DataMsg = Common.SerializeObject(new InsertParam(typeof(SysLogin), Common.SerializeObject(CurrentEdit)));
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
				var item = Common.DeserializeObject<SysLogin>(reply.DataMsg ?? "");
				if (item != null) {
					ListData.Add(item);
					Current = item;
					Count = ListData.Count;
				}
			}
			catch (OperationCanceledException) {
				return;
			}
		}
	}
}
