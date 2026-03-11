using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using System.Collections;


namespace Cvnet10Wpfclient.ViewModels._00System;

internal partial class SysLoginHistoryViewModel : Helpers.BaseMenteViewModel<SysHistJwt> {
	[ObservableProperty]
	string title = "ログイン履歴";

	/// <summary>最新順に取得</summary>
	protected override string? ListOrder => "Id DESC";

	[RelayCommand]
	public async Task Init() {
		await DoList(CancellationToken.None);
	}

	/// <summary>履歴テーブルは修正不可</summary>
	protected override bool CanUpdate() => false;

	/// <summary>履歴テーブルは削除不可</summary>
	protected override bool CanDelete() => false;

	protected override void AfterList(IList list) {
		Message = $"リスト取得しました (件数={list.Count}, 取得時間 {StartTime.ToDtStrTime()} // {GetListTime.ToStrSpan()})";
	}
}
