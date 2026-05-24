using Microsoft.Extensions.Logging;

namespace CvBase;

public class DefineDataTable {


	/// <summary>
	/// ロガーインスタンス [Logger instance]
	/// </summary>
	private static readonly ILogger<DefineDataTable> _logger = new NLogExtender<DefineDataTable>();

	public bool Initialize(ExDatabase db, bool isForce) {
		var ret = false;
		// ToDo: テーブルの存在チェックと作成は、テーブルごとに行うのではなく、まとめて行うようにすること
		// ToDo: テーブルが追加された場合、事前作成が必要なものはここに追加すること

		var tableTypes = new List<Type> {
			// システムテーブル
			typeof(SysUpdateDb),
			typeof(SysSequence),
			typeof(SysLogin),
			typeof(SysHistJwt),

			// マスタテーブル1
			typeof(MasterSysman),
			typeof(MasterMeisho),

			// マスタテーブル2
			typeof(MasterShain),
			typeof(MasterEndCustomer),
			typeof(MasterShohin),

			// マスタテーブル3
			typeof(MasterTokui),
			typeof(MasterShiire),
			typeof(MasterConfig),

			// トランザクションテーブル
			typeof(Tran00Uriage),
			typeof(Tran01Tenuri),
			typeof(Tran03Shiire),
			typeof(Tran05Ido),
			typeof(Tran06Nyukin),
			typeof(Tran07Shiharai),
			typeof(Tran60Tana),
			typeof(Tran10IdoOut),
			typeof(Tran11IdoIn),
			typeof(Tran12Jyuchu),
			typeof(Tran13Hachu),
			typeof(TranHhtData),
			typeof(TranVulcanHht),

			// 集計テーブル
			typeof(SummaryStock),
			typeof(SummaryRealStock),
		};
		foreach (var tableType in tableTypes) {
			if (!db.CreateTable(tableType, isForce)) {
				_logger.LogError("テーブルの作成に失敗しました。テーブル名: {TableName}", tableType.Name);
				return false;
			}
		}
		// DBの整合性を管理
		UpdateDb.WriteVersionInfoAsync(db).Wait();
		// DerivedClassの作成
		ret = db.CreateDerivedTable<DerivedShohinColSiz>(isForce);
		// 他、追加処理
		//var summaryDb = new CvDomainLogic.SummaryDb(db);
		//summaryDb.CalcSummaryRealStock(DateTime.Now.ToString("yyyyMM"));

		return ret;
	}

}
