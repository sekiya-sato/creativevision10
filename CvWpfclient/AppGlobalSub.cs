using CodeShare;
using CvAsset;
using CvBase;
using Newtonsoft.Json;


namespace CvWpfclient;
/// <summary>
/// グローバル変数
/// </summary>
public static partial class AppGlobal {
	static MasterSysman? sysman = null;

	/// <summary>
	/// 消費税率を取得する
	/// </summary>
	/// <param name="no"></param>
	/// <param name="date_ymd"></param>
	/// <returns></returns>
	async public static Task<int> LogicGetTax(int no, string date_ymd) {
		int tax = 10; // デフォルト値 消費税10%
		if (sysman == null) {
			await LogicGetSysman();
		}
		var systax = sysman?.Jsub?.Where(x => x.Id == no).FirstOrDefault() ?? new MasterSysTax();
		tax = systax.TaxRate;
		if (Common.CompareYmd(date_ymd, systax.DateFrom) >= 0) {
			tax = systax.TaxNewRate;
		}
		return tax;
	}
	async public static Task<MasterSysman> LogicGetSysman() {
		if (sysman == null) {
			var param = new QueryByIdParam(typeof(MasterSysman), 1);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryByIdParam),
				DataMsg = Common.SerializeObject(param)
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext());
			if (reply.Code < 0) {
				return new MasterSysman();
			}
			var des = JsonConvert.DeserializeObject<MasterSysman>(reply.DataMsg);
			sysman = des ?? new MasterSysman();
			if (sysman.Id_Soko > 0) {
				param = new QueryByIdParam(typeof(MasterTokui), sysman.Id_Soko);
				msg = new CvMsg {
					Code = 0,
					Flag = CvFlag.Msg101_Op_Query,
					DataType = typeof(QueryByIdParam),
					DataMsg = Common.SerializeObject(param)
				};
				var reply2 = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext());
				if (reply2.Code >= 0) {
					var des2 = JsonConvert.DeserializeObject<MasterTokui>(reply2.DataMsg) ?? new MasterTokui();
					sysman.VSoko = new CodeNameView() { Sid = des2.Id, Cd = des2.Code, Mei = des2.Name };
				}
			}
		}
		return sysman;
	}





}
