using CvAsset;
using CvBase;

namespace CvDomainLogic;

public partial class HhtProcess {
	/// <summary>
	/// 指定された日付範囲のValcanのデータをHhtdataに変換する
	/// <para>
	/// 旧CVnet互換の中間テーブル(<see cref="TranHhtData"/>)向けの変換であり、現在どこからも呼ばれていない。
	/// CV10の正規経路は <see cref="UpdateVulcan2Tran"/>（<see cref="TranVulcanHht"/> から Tran系へ直接展開する）である。
	/// 仕様は `Doc/spec/archive/2026-08-24_HHTデータ更新詳細設計.md` の決定 12-A を参照する。
	/// </para>
	/// </summary>
	/// <param name="dateFrom">開始日付 (yyyy-MM-dd形式)</param>
	/// <param name="dateTo">終了日付 (yyyy-MM-dd形式)</param>
	public void TransferValcan2Hhtdata(string dateFrom, string dateTo) {
		//TranVulcanHht
		//TranHhtData
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		var ret = _db.Execute("Update TranHhtData set VdCnvDate=-1 where DenDay between @0 and @1 and VdCnvDate=0", [dateFrom, dateTo]);
		// 一旦TranHhtDataのVdCnvDateを-1にして、変換対象を明示する
		var vulcanData = _db.Fetch<TranVulcanHht>($"where DenDay between @0 and @1 and VdCnvDate=-1", [dateFrom, dateTo]);
		var now = DateTime.Now;
		// Vulcanレイアウト定義表では、Store,Tantoなどは前0埋などとなっているが、桁数が明示されてないため、ここではそのまま変換する
		foreach (var item in vulcanData) {
			var hhtData = new TranHhtData {
				Vdc = Common.GetVdate(),
				Vdu = Common.GetVdate(),
				Shop = item.Shop,
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

		}
		// TranHhtDataのVdCnvDateを-1から本来の値に設定する
		ret = _db.Execute("Update TranHhtData set VdCnvDate=@0 where DenDay between @1 and @2 and VdCnvDate=-1", [Common.GetVdate(), dateFrom, dateTo]);
		_db.CompleteTransaction();
	}
}
