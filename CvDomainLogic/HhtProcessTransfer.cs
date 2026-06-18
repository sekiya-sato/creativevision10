using CvAsset;
using CvBase;

namespace CvDomainLogic;

public partial class HhtProcess {
	/// <summary>
	/// 指定された日付範囲のValcanのデータをHhtdataに変換する
	/// </summary>
	/// <param name="dateFrom">開始日付 (yyyy-MM-dd形式)</param>
	/// <param name="dateTo">終了日付 (yyyy-MM-dd形式)</param>
	public void TransferValcan2Hhtdata(string dateFrom, string dateTo) {
		//TranVulcanHht
		//TranHhtData
		_db.BeginTransaction();
		var ret = _db.Execute("Update TranHhtData set VdCnvDate=-1 where DenDay between @0 and @1 and VdCnvDate=0", [dateFrom, dateTo]);
		// 一旦TranHhtDataのVdCnvDateを-1にして、変換対象を明示する
		var vulcanData = _db.Fetch<TranVulcanHht>($"where DenDay between @0 and @1 and VdCnvDate=-1", [dateFrom, dateTo]);
		var now = DateTime.Now;
		// Vulcanレイアウト定義表では、Store,Tantoなどは前0埋などとなっているが、桁数が明示されてないため、ここではそのまま変換する
		foreach (var item in vulcanData) {
			var hhtData = new TranHhtData {
				Vdc = Common.GetVdate(),
				Vdu = Common.GetVdate(),
				Store = item.Store,
				DenDay = item.DenDay,
				Kubun = item.Type0 switch {
					1 => "22", // 店舗売上
					2 => "27", // 店舗売上返品
					3 => "41", // 移動受
					4 => "42", // 移動出(積送)
					5 => "10", // 仕入
					6 => "15", // 仕入返品
					7 => "60", // 棚卸
					8 => "08", // 発注
					9 => "20", // 出荷売上
					10 => "25",// 出荷売上返品
					11 => "40",// 移動即時
					12 => "31",// 客数
					13 => "70",// 受注 (0xD は未定義)
					_ => "99"
				},
				DenNo = int.TryParse(item.DenNo, out var denNo) ? denNo : 0,
				Tanto = item.Tanto,
				Tori = item.ToriSaki,
				Jan1 = item.Jan1,
				Jan2 = item.Jan2,
				Jodai = item.Tanka,
				Su = item.Su,
				Kakeritsu = decimal.TryParse(item.KakeRitsu, out var kakeritsu) ? kakeritsu / 10 : 0,
				FileName = item.BackupFileName,
				LineNo = item.LineNo,
			};
			// 組み合わせ判定
			switch (item.Type0, item.HanKubun) {
				case (1, 1):
					hhtData.Kubun = "23";// 店舗売上Sale
					break;
				case (1, 2):
					hhtData.Kubun = "24";// 店舗売上社販
					break;
				case (2, 1):
					hhtData.Kubun = "28";// 店舗売上Sale返品
					break;
				case (2, 2):
					hhtData.Kubun = "29";// 店舗売上社販返品
					break;
				case (9, 1):
					hhtData.Kubun = "21";// 出荷売上Sale
					break;
				case (10, 1):
					hhtData.Kubun = "26";// 出荷売上Sale返品
					break;
			}
			if (item.Type0 == 7) {
				hhtData.TanaNo = (int)hhtData.DenNo;
			}
			// ToDo: Vulcanにないhinban,col,sizeなどのデータのセット

			_db.Insert(hhtData);
			item.TargetTableName = nameof(TranHhtData);
			item.TargetId = hhtData.Id;
			_db.Update(item);
			/* TranVulcanHhtとTranHhtDataのテーブル定義
			 *
CREATE TABLE
    "TranVulcanHht" (
        Id INTEGER not null default 0 PRIMARY KEY AUTOINCREMENT,
        Vdc NUMBER not null default 0,
        Vdu NUMBER not null default 0,
        Type0 NUMBER not null default 0,
        HhtNo NUMBER not null default 0,
        Serial NUMBER not null default 0,
        DenDay TEXT NOT NULL DEFAULT '',
        Store TEXT NOT NULL DEFAULT '',
        Tanto TEXT NOT NULL DEFAULT '',
        HanKubun NUMBER not null default 0,
        DenNo TEXT NOT NULL DEFAULT '',
        Jan1 TEXT NOT NULL DEFAULT '',
        Jan2 TEXT NOT NULL DEFAULT '',
        Su NUMBER not null default 0,
        Tanka NUMBER not null default 0,
        ToriSaki TEXT NOT NULL DEFAULT '',
        KakeRitsu TEXT NOT NULL DEFAULT '',
        TotalCnt NUMBER not null default 0,
        Filler TEXT NOT NULL DEFAULT '',
        BackupFileName TEXT NOT NULL DEFAULT '',
        LineNo NUMBER not null default 0,
        ComputerName TEXT NOT NULL DEFAULT '',
        UserName TEXT NOT NULL DEFAULT '',
        VdCnvDate NUMBER not null default 0,
        TargetTableName TEXT NOT NULL DEFAULT '',
        TargetId NUMBER not null default 0,
        ErrorMsg TEXT
    );

CREATE TABLE
    TranHhtData (
        Id INTEGER not null default 0 PRIMARY KEY AUTOINCREMENT,
        Vdc NUMBER not null default 0,
        Vdu NUMBER not null default 0,
        Store TEXT NOT NULL DEFAULT '',
        DenDay TEXT NOT NULL DEFAULT '',
        Kubun TEXT NOT NULL DEFAULT '',
        DenNo NUMBER not null default 0,
        Tanto TEXT NOT NULL DEFAULT '',
        Tori TEXT NOT NULL DEFAULT '',
        Hinban TEXT NOT NULL DEFAULT '',
        Color TEXT NOT NULL DEFAULT '',
        Size TEXT NOT NULL DEFAULT '',
        MotoJodai NUMBER not null default 0,
        Jodai NUMBER not null default 0,
        Gedai NUMBER not null default 0,
        Su NUMBER not null default 0,
        Store2 TEXT NOT NULL DEFAULT '',
        SaleFlg TEXT NOT NULL DEFAULT '',
        TanaNo TEXT NOT NULL DEFAULT '',
        RelateDenNo NUMBER not null default 0,
        Kakeritsu REAL not null default 0,
        NouhinDay TEXT NOT NULL DEFAULT '',
        Yobi03 TEXT NOT NULL DEFAULT '',
        Yobi04 TEXT NOT NULL DEFAULT '',
        Yobi05 TEXT NOT NULL DEFAULT '',
        Yobi06 TEXT NOT NULL DEFAULT '',
        Yobi07 TEXT NOT NULL DEFAULT '',
        Yobi08 TEXT NOT NULL DEFAULT '',
        Yobi09 TEXT NOT NULL DEFAULT '',
        Yobi10 TEXT NOT NULL DEFAULT '',
        Yobi11 TEXT NOT NULL DEFAULT '',
        Yobi12 TEXT NOT NULL DEFAULT '',
        FileName TEXT NOT NULL DEFAULT '',
        LineNo NUMBER not null default 0,
        VdCnvDate NUMBER not null default 0
    );


			 */

		}
		// TranHhtDataのVdCnvDateを-1から本来の値に設定する
		ret = _db.Execute("Update TranHhtData set VdCnvDate=@0 where DenDay between @1 and @2 and VdCnvDate=-1", [Common.GetVdate(), dateFrom, dateTo]);
		_db.CompleteTransaction();
	}
}
