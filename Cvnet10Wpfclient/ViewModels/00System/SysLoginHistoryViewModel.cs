using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using System.Collections;


namespace Cvnet10Wpfclient.ViewModels._00System;

internal partial class SysLoginHistoryViewModel : Helpers.BaseMenteViewModel<SysHistJwt> {
	[ObservableProperty]
	string title = "ログイン履歴";

	/// <summary>ログインID → 社員表示文字列 (Code Name) のマッピング</summary>
	Dictionary<long, string> loginIdToShainDisplay = [];

	/// <summary>現在選択中の社員表示文字列</summary>
	[ObservableProperty]
	string shainDisplay = string.Empty;


	/// <summary>最新順に取得</summary>
	protected override string? ListOrder => "Id DESC";

	protected override int? ListMaxCount => 6;


	[RelayCommand]
	public async Task Init() {
		await DoList(CancellationToken.None);
	}


	/// <summary>履歴テーブルは修正不可</summary>
	protected override bool CanUpdate() => false;

	/// <summary>履歴テーブルは削除不可</summary>
	protected override bool CanDelete() => false;

	protected override void OnCurrentEditChangedCore(SysHistJwt? oldValue, SysHistJwt newValue) {
		base.OnCurrentEditChangedCore(oldValue, newValue);
		ShainDisplay = newValue != null && loginIdToShainDisplay.TryGetValue(newValue.Id_Login, out var display)
			? display : string.Empty;
	}

	protected override async void AfterList(IList list) {
		Message = $"リスト取得しました (件数={list.Count}, 取得時間 {StartTime.ToDtStrTime()} // {GetListTime.ToStrSpan()})";

		var histList = list.Cast<SysHistJwt>().ToList();
		var loginIds = histList.Select(h => h.Id_Login).Where(id => id > 0).Distinct().ToList();
		if (loginIds.Count == 0) {
			loginIdToShainDisplay = [];
			return;
		}

		try {
			var query = new QueryListParam(
				itemType: typeof(SysLogin),
				where: $"Id IN ({string.Join(",", loginIds)})",
				order: "Id");
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(query)
			};
			var reply = await SendMessageAsync(msg, CancellationToken.None);
			var sysLogins = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
			loginIdToShainDisplay = (sysLogins?.Cast<SysLogin>() ?? [])
				.ToDictionary(
					s => s.Id,
					s => s.VShain is { Cd.Length: > 0 } v ? $"{v.Cd} {v.Mei}" : string.Empty);
		}
		catch {
			loginIdToShainDisplay = [];
		}

		if (CurrentEdit != null) {
			ShainDisplay = loginIdToShainDisplay.TryGetValue(CurrentEdit.Id_Login, out var display)
				? display : string.Empty;
		}
	}
}
