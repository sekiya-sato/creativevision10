using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base; // MasterMeisho クラスに必要
using Cvnet10Base.Share;
using System;
using System.Collections.Generic;
using Cvnet10Wpfclient.ViewServices;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MasterSysKanriMenteViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	string m_Title = "システム管理マスターメンテ画面";

	Type tabletype = typeof(MasterSysman);
	[RelayCommand]
	public async Task Init() {
		await DoList(new CancellationToken());
	}

	[ObservableProperty]
	MasterSysman current = new();

	public IReadOnlyList<EnumShime> ShimeBiItems { get; } = Enum.GetValues<EnumShime>();


	[ObservableProperty]
	public long count = 1000;
	[ObservableProperty]
	public long getSkip = 0;

	[ObservableProperty]
	public string? desc0;


	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoList(CancellationToken ct) {
		var timeStart = DateTime.Now;
		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterSysman)
				))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;

			if (list != null) {
				var timespan2 = DateTime.Now - timeStart;
				Desc0 = $"開始{timeStart} 取得、画面展開{timespan2.ToStrSpan()}";
				var list0 = new ObservableCollection<MasterSysman>(list.Cast<MasterSysman>());
				Count = list0.Count;
				Current = list0?.FirstOrDefault()?? new();
			}
			var timespan = DateTime.Now - timeStart;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}


	[RelayCommand(IncludeCancelCommand = true)]
	public async Task DoUpdate(CancellationToken ct) {
		if (MessageEx.ShowQuestionDialog("更新しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes)
			return;
		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(UpdateParam),
				DataMsg = Common.SerializeObject(new UpdateParam(
					itemType: typeof(MasterSysman),
					item: Common.SerializeObject(Current)
				))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as MasterSysman;
			if (item != null) {
				Current = item;
			}
			MessageEx.ShowInformationDialog("修正しました", owner: ClientLib.GetActiveView(this));
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}
}
