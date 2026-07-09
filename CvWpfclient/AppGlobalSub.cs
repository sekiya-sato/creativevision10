using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.Models;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ProtoBuf.Grpc;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;


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
			var param = new QueryByIdParam(typeof(MasterSysman),1);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryByIdParam),
				DataMsg = Common.SerializeObject(param)
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext());
			if (reply.Code < 0) {
				return tax;
			}
			var des = JsonConvert.DeserializeObject<MasterSysman>(reply.DataMsg);
			sysman = des ?? new MasterSysman();
			if(sysman.Jsub == null || sysman.Jsub.Count==0) {
				sysman.Jsub = new List<MasterSysTax>();
				return tax;
			}
		}
		var systax = sysman?.Jsub?.Where(x => x.Id == no).FirstOrDefault()??new MasterSysTax();
		tax = systax.TaxRate;
		if(Common.CompareYmd(date_ymd, systax.DateFrom) >= 0) {
			tax = systax.TaxNewRate;
		}
		return tax;
	}





}
