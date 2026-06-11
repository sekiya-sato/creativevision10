using Microsoft.Extensions.Logging;

namespace CvBase;

public class DefineDataTable {


	/// <summary>
	/// ロガーインスタンス [Logger instance]
	/// </summary>
	private static readonly ILogger<DefineDataTable> _logger = new NLogExtender<DefineDataTable>();

	/// <summary>
	/// データベースの初期化処理を行う。テーブルの存在チェックと作成を行う。
	/// </summary>
	/// <param name="db">データベースインスタンス</param>
	/// <param name="isForce">強制的に作成するかどうか</param>
	/// <returns>初期化が成功したかどうか</returns>
	public bool Initialize(ExDatabase db, bool isForce) {
		var ret = false;
		// SQLiteのバージョンは 3.49.1 以降 (2025/05/27) select sqlite_version();

		// ToDo: テーブルの存在チェックと作成は、テーブルごとに行うのではなく、まとめて行うようにすること / テーブルが追加された場合、事前作成が必要なものはここに追加すること

		var tableTypes = new List<Type> {
			// システムテーブル
			typeof(SysUpdateDb),
			typeof(SysSequence),
			typeof(SysLogin),
			typeof(SysHistJwt),
			typeof(SysHistAutoexec),

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

			// マスタテーブル4
			typeof(MasterYosanBrand),
			typeof(MasterYosanHanbai),

			// ポイント系テーブル
			typeof(MasterPointRank),
			typeof(TranPointRireki),
			typeof(SummaryPoint),

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
			// 配分テーブル
			typeof(TranHaibun),
			typeof(TranHoju),

			// 集計テーブル
			typeof(SummaryStock),
			typeof(SummaryRealStock),
			// 掛関係テーブル
			typeof(SummaryUriKake),
			typeof(SummaryUriSei),
			typeof(SummaryKaiKake),
			typeof(SummaryKaiShi)
			/* Product: 以下のテーブルは、優先順位低いが、いずれ作成する予定
			上代一括変更
				TranJodai : 伝票No,日付 (from-to),セールCD(Meisho'S01') タイトル  [店舗CD] [商品CD,(色CD,サイズCD) : 掛率,上代]
				DerivedJodai : TranJodai-No,日付 (from-to),Id_Shohin,Jodai ??
			原価変更登録
				TranGenka : 伝票No,日付(年月+末),セールCD(Meisho'S01') 評価区分(0:通常,1:評価替え) OFF率 [商品CD] :上代 掛率 元原価 新原価
			自動補充設定 (売上/在庫)
				MasterAutoSupply
			 */
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

	/* 全データベースファイルの構造
	 * システム系：Sys：管理に関わるテーブル、システム全体に関わるテーブル
	 *		更新履歴、連番、ログイン、ログイン履歴、マスター操作履歴
	 *		
	 * マスター系：Master：マスターデータを管理するテーブル
	 *		システム管理、名称、社員、顧客、商品、設定、得意先、仕入先
	 * トランザクション系：Tran：取引に関わるテーブル
	 *		売上、仕入、移動、入金、支払、棚卸、受発注、HHT 取込
	 * 集計系：Summary：集計データを管理するテーブル
	 *		現在庫、年月在庫
	 * 派生系：Derived：マスタからの派生データを管理するテーブル
	 *		商品マスタの色サイズ展開
	 * 
	 */

}
