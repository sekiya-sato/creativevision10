using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using System.Collections;


namespace Cvnet10Wpfclient.ViewModels._00System;

internal partial class SysLoginViewModel : Helpers.BaseMenteViewModel<SysLogin> {
	[ObservableProperty]
	string title = "ログインマスターメンテ画面";

	[ObservableProperty]
	string lastLoginDate = string.Empty;

	[RelayCommand]
	public async Task Init() {
	}

	protected override bool ConfirmAction(string message) =>
		MessageEx.ShowQuestionDialog(message, owner: ClientLib.GetActiveView(this)) == MsgBoxResult.Yes;

	protected override void OnCurrentChangedCore(SysLogin? oldValue, SysLogin newValue) {
		base.OnCurrentChangedCore(oldValue, newValue);
		LastLoginDate = $"{CurrentEdit.LastDate.ToDateStr()}";
	}

	protected override bool CanUpdate() => Current.Id > 0;

	protected override bool CanDelete() =>
		ListData.Count > 0 && Current.Id > 0;
	protected override void AfterList(IList list) {
		Message = $"リスト取得しました (件数={list.Count}, 取得時間 {StartTime.ToDtStrTime()} // {GetListTime.ToStrSpan()})";
	}
	protected override void AfterInsert(SysLogin item) {
		Message = $"追加しました (Login={item.LoginId}, Id={item.Id})";
	}

	protected override void AfterUpdate(SysLogin item) {
		Message = $"修正しました (Login={item.LoginId}, Id={item.Id})";
	}
	protected override void AfterDelete(SysLogin item) {
		Message = $"削除しました (Login={item.LoginId}, Id={item.Id})";
	}

	[RelayCommand]
	public void DoSelectShain() {
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterShain), "");
		vm.InitParam = (int)CurrentEdit.Id_Shain;
		var ret = ClientLib.ShowDialogView(selWin, this);
		if (ret != true) return;
		var meisho = vm.Current as MasterShain;
		var cur = CurrentEdit as SysLogin;
		if (meisho == null || cur == null) return;
		cur.Id_Shain = meisho.Id;
		cur.VShain?.Sid = meisho.Id;
		cur.VShain?.Cd = meisho.Code;
		cur.VShain?.Mei = meisho.Name;
	}

	[RelayCommand]
	public void DoSetPass() {
		var cur = CurrentEdit as SysLogin;
		if (cur == null) return;
		var warning = (cur.CryptPassword.Length >= 20) ? "すでに暗号化されているようですが、\n" : "";
		if (MessageEx.ShowQuestionDialog(warning + "表示されているパスワードの文字を暗号化しますか？",
			owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
			return;
		var newPass = Common.EncryptLoginRequest(cur.CryptPassword, cur.VdateC);
		if (!string.IsNullOrEmpty(newPass))
			cur.CryptPassword = newPass;
	}
}
