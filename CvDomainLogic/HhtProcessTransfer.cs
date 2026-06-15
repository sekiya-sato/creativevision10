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
		var ret = _db.Execute("Update TranHhtData set VdCnvDate=-1 where DenDay between @0 and @1 and VdCnvDate=0", new string[] { dateFrom, dateTo });
		var vulcanData = _db.Fetch<TranVulcanHht>($"where DenDay between @0 and @1 and VdCnvDate=-1", new string[] { dateFrom, dateTo });
		// ToDo: VulcanのデータをHhtDataに変換して、VdCnvDateを変換日付に更新する
		var now = DateTime.Now;
		foreach (var item in vulcanData) {
			var hhtData = new TranHhtData {
				DenDay = item.DenDay,
				Kubun = item.Type0 switch {
					1 => "22",
					2 => "27",
					3 => "41",
					4 => "42",
					5 => "10",
					6 => "15",
					7 => "60",
					8 => "08",
					9 => "20",
					10 => "25",
					11 => "40",
					12 => "31",
					13 => "70", // D は未定義
					_ => "99"
				},
			};
			_db.Insert(hhtData);
			_db.Execute("Update TranVulcanHht set VdCnvDate=@0 where Id=@1", [now.ToDtStrDate2(), item.Id]);





			_db.CompleteTransaction();
		}
	}
}
