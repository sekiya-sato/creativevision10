using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections;

namespace CvWpfclient.ViewModels._00System;

internal partial class SysAutoExecHistoryViewModel : Helpers.BaseMenteViewModel<SysHistAutoexec> {
	[ObservableProperty]
	string title = "自動実行履歴";

	/// <summary>最新順に取得</summary>
	protected override string? ListOrder => "Id DESC";

	protected override int? ListMaxCount => 400;

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
