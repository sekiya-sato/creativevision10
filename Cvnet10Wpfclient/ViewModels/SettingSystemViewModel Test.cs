using CodeShare;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels;

public partial class SettingSystemViewModel {

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Test01Async(CancellationToken cancellationToken) {
		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho),
					where: "Kubun=@0", order: "Kubun,Code", ["SIZ"]
				))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;

			if (list != null) {
				var list2 = new ObservableCollection<MasterMeisho>(list.Cast<MasterMeisho>());
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"ÉfÅ[É^éÊìæé∏îs: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}

    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Test02Async(CancellationToken cancellationToken) {


    }
}