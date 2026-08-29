using CvAsset;
using Microsoft.Extensions.Logging;

namespace CvBase;

public class DefineDataTable {


	/// <summary>
	/// ロガーインスタンス [Logger instance]
	/// </summary>
	private static readonly ILogger<DefineDataTable> _logger = new NLogExtender<DefineDataTable>();
	/// <summary>
	/// 事前作成するテーブルの型一覧。
	/// <para>
	/// DDL生成のスナップショットテスト(3DBで同じ列集合になるかの検証)からも参照するため
	/// メソッド内のローカル変数ではなく公開メンバとして持つ。
	/// </para>
	/// </summary>
	public static IReadOnlyList<Type> TableTypes { get; } = new List<Type> {
			// システムテーブル
			typeof(SysUpdateDb),
			typeof(SysSequence),
			typeof(SysLogin),
			typeof(SysHistJwt),
			typeof(SysHistAutoexec),
			typeof(SysPermissionProfile),
			typeof(SysPermissionProfileDetail),

			// マスタテーブル1
			typeof(MasterSysman),
			typeof(MasterMeisho),

			// マスタテーブル2
			typeof(MasterShain),
			typeof(MasterEndCustomer),
			typeof(MasterEndCustomerAccount),
			typeof(MasterShohin),
			typeof(DerivedShohinColSiz),
			typeof(MasterMaterial),

			// マスタテーブル3
			typeof(MasterTokui),
			typeof(MasterShiire),
			typeof(MasterConfig),

			// マスタテーブル4
			typeof(MasterYosanBrand),
			typeof(MasterYosanHanbai),
			typeof(MasterShipping),

			// ポイント系テーブル
			typeof(MasterPointRank),
			typeof(TranPointRireki),
			typeof(SummaryPoint),

			// トランザクションテーブル
			typeof(Tran00Uriage),
			typeof(Tran01Tenuri),
			typeof(Tran02Material),
			typeof(Tran03Shiire),
			typeof(Tran04PosSeisan),
			typeof(Tran05Ido),
			typeof(Tran06Nyukin),
			typeof(Tran07Shiharai),
			typeof(Tran60Tana),
			typeof(Tran61Chosei),
			typeof(Tran10IdoOut),
			typeof(Tran11IdoIn),
			typeof(Tran12Jyuchu),
			typeof(Tran13Hachu),
			typeof(TranHhtData),
			typeof(TranVulcanHht),
			// 配分テーブル
			typeof(TranHaibun),
			typeof(TranHoju),
			// 上代一括変更テーブル (DerivedJodai は TranJodai の IDerivedOrigin 経由で自動展開される)
			typeof(TranJodai),
			typeof(DerivedJodai),

			// 集計テーブル
			typeof(SummaryStock),
			typeof(SummaryRealStock),
			// 掛関係テーブル
			typeof(SummaryUriKake),
			typeof(SummaryUriSei),
			typeof(SummaryKaiKake),
			typeof(SummaryKaiShi),
			// 追加テーブル
			typeof(TranTokuiPromotion),
			typeof(TranShopPromotion),

			typeof(Tran60TanaDate),
			/* Product: 以下のテーブルは、優先順位低いが、いずれ作成する予定
			原価変更登録
				TranGenka : 伝票No,日付(年月+末),セールCD(Meisho'S01') 評価区分(0:通常,1:評価替え) OFF率 [商品CD] :上代 掛率 元原価 新原価
			自動補充設定 (売上/在庫)
				MasterAutoSupply
			 */
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
			 * ■ V*列(CodeNameView)の扱いはこのテーブル種別で決まる (詳細は AGENTS.md / .omo/20260727_master_vcolumn_sync_design.md)
			 *		Tran系   : 伝票作成時点の名称を保持する監査値。マスタ改名時に伝播しない(意図的)。現行名称が必要なら Id_* からJOINする
			 *		Master系 : 常に現行名称。CvDomainLogic/MasterCascadeDb がマスタ更新時に伝播する
			 *		           Master系にV*列を追加したら MasterCascadeDb.VRules への登録も必須
			 *		Summary系: V*列を持たない(JOIN前提)。新規に追加する参照列もこの方針に合わせることを推奨
			 */
	};


	/// <summary>
	/// データベースの初期化処理を行う。テーブルの存在チェックと作成を行う。
	/// </summary>
	/// <param name="db">データベースインスタンス</param>
	/// <param name="isForce">強制的に作成するかどうか</param>
	/// <returns>初期化が成功したかどうか</returns>
	public async Task<bool> InitializeAsync(ExDatabase db, bool isForce, CancellationToken ct = default) {
		var ret = false;
		// SQLiteのバージョンは 3.49.1 以降 (2025/05/27) select sqlite_version();

		// ToDo: テーブルの存在チェックと作成は、テーブルごとに行うのではなく、まとめて行うようにすること / テーブルが追加された場合、事前作成が必要なものはここに追加すること

		var tableTypes = TableTypes;
		foreach (var tableType in tableTypes) {
			/* if (typeof(IDerivedClass).IsAssignableFrom(tableType)) {} */
			if (!db.CreateTable(tableType, isForce)) {
				_logger.LogError("テーブルの作成に失敗しました。テーブル名: {TableName}", tableType.Name);
				return false;
			}
		}
		ret = true;
		// DBがなにもない場合、初期データを作成する
		InitializeDatabase(db);
		// 個別の初期化処理
		MasterShipping.CreateDefaultData(db);
		SysPermissionProfile.CreateDefaultData(db);

		// DBの整合性を管理
		await UpdateDb.WriteVersionInfoAsync(db, ct);
		// 他、追加処理
		//var summaryDb = new CvDomainLogic.SummaryDb(db);
		//summaryDb.CalcSummaryRealStock(DateTime.Now.ToString("yyyyMM"));

		return ret;
	}
	/// <summary>
	/// データがないとき、最低限の初期データを作成する
	/// </summary>
	/// <param name="db"></param>
	public void InitializeDatabase(ExDatabase db) {
		var dblist = db.GetTableCounts();
		var totalcnt = dblist.Sum(c => c.Item3);
		if (totalcnt > 0)
			return;
		// ログインデータなど最低限のデータを作成する
		var now = DateTime.Now;
		var shain = new MasterShain { Code = "0001", Name = "管理者", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() };
		db.Insert<MasterShain>(shain);
		var syslogin = new SysLogin {
			LoginId = now.ToDtStrDate2(),
			Id_Shain = shain.Id,
			ExpDate = now.AddMonths(1).ToDtStrDate2(),
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<SysLogin>(syslogin);
		syslogin.CryptPassword = Common.EncryptLoginRequest(syslogin.LoginId, syslogin.VdateC);
		db.Update(syslogin);
		var sysman = new MasterSysman {
			Name = $"株式会社 CreativeVision10 デモシステム {now.ToDtStrDate2()}",
			PostalCode = "100-0013",
			Address1 = "東京都",
			Address2 = "千代田区",
			FiscalStartDate = new DateTime(now.Year, 1, 1).ToDtStrDate2(),
			ShimeBi = 99,
			ModifyDaysEx = 100,
			ModifyDaysPre = 100,
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate(),
			Jsub = [new MasterSysTax { Id = 1, DateFrom = "19010101", TaxRate = 10 }, new MasterSysTax { Id = 2, DateFrom = "19010101", TaxRate = 8 }, new MasterSysTax { Id = 3, DateFrom = "19010101", TaxRate = 10 },]

		};
		db.Insert<MasterSysman>(sysman);
		var meishoList = new List<MasterMeisho> {
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "IDX", Name = "名称区分インデックス", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "BRD", Name = "ブランド", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "ITM", Name = "アイテム", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "COL", Name = "カラー", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "SIZ", Name = "サイズ", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "SLE", Name = "セール", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "IDX", KubunName = "名称区分", Code = "CHR", Name = "調整理由", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "NewBrand", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "ITM", KubunName = "アイテム", Code = "01", Name = "NewItem", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "COL", KubunName = "カラー", Code = "01", Name = "NewColor", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "SIZ", KubunName = "サイズ", Code = "01", Name = "NewSize", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "SLE", KubunName = "セール", Code = "0001", Name = "セール", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			// 調整理由（在庫強制調整）。コード10〜19=加算(+)/20〜29=減算(−)。ChoseiRiyu.CalcFlag を参照
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "10", Name = "入庫", Odr = 10, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "20", Name = "紛失", Odr = 20, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "21", Name = "盗難", Odr = 21, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "22", Name = "破損", Odr = 22, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "23", Name = "検品ミス", Odr = 23, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "29", Name = "その他", Odr = 29, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		};
		db.InsertBulk<MasterMeisho>(meishoList);
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "サンプル商品",
			Id_Brand = meishoList.FirstOrDefault(c => c.Kubun == "BRD")?.Id ?? 0,
			VBrand = new CodeNameView {
				Sid = meishoList.FirstOrDefault(c => c.Kubun == "BRD")?.Id ?? 0,
				Cd = meishoList.FirstOrDefault(c => c.Kubun == "BRD")?.Code ?? string.Empty,
				Mei = meishoList.FirstOrDefault(c => c.Kubun == "BRD")?.Name ?? string.Empty
			},
			Id_Item = meishoList.FirstOrDefault(c => c.Kubun == "ITM")?.Id ?? 0,
			VItem = new CodeNameView {
				Sid = meishoList.FirstOrDefault(c => c.Kubun == "ITM")?.Id ?? 0,
				Cd = meishoList.FirstOrDefault(c => c.Kubun == "ITM")?.Code ?? string.Empty,
				Mei = meishoList.FirstOrDefault(c => c.Kubun == "ITM")?.Name ?? string.Empty
			},
			TankaGenka = 1000,
			TankaJodai = 2000,
			TankaJodaiOrg = 2000,
			Jcolsiz = [new MasterShohinColSiz {
				Id_Col = meishoList.FirstOrDefault(c => c.Kubun == "COL")?.Id ?? 0,
				Code_Col = meishoList.FirstOrDefault(c => c.Kubun == "COL")?.Code ?? string.Empty,
				Mei_Col = meishoList.FirstOrDefault(c => c.Kubun == "COL")?.Name ?? string.Empty,
				Id_Siz = meishoList.FirstOrDefault(c => c.Kubun == "SIZ")?.Id ?? 0,
				Code_Siz = meishoList.FirstOrDefault(c => c.Kubun == "SIZ")?.Code ?? string.Empty,
				Mei_Siz = meishoList.FirstOrDefault(c => c.Kubun == "SIZ")?.Name ?? string.Empty
			}],
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<MasterShohin>(shohin);
		db.Execute(DerivedShohinColSiz.InsertSql, shohin.Id);
		var customer = new MasterEndCustomer {
			Code = "0001",
			Name = "サンプル顧客",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<MasterEndCustomer>(customer);
		var shiire = new MasterShiire {
			Code = "0001",
			Name = "サンプル仕入先",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<MasterShiire>(shiire);
		var tokui = new MasterTokui {
			Code = "0001",
			Name = "サンプル得意先",
			Id_Shain = shain.Id,
			VShain = new CodeNameView {
				Sid = shain.Id,
				Cd = shain.Code,
				Mei = shain.Name
			},
			TenType = 1,
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<MasterTokui>(tokui);
		var tenpo = new MasterTokui {
			Code = "0101",
			Name = "サンプル店舗A",
			Id_Shain = shain.Id,
			VShain = new CodeNameView {
				Sid = shain.Id,
				Cd = shain.Code,
				Mei = shain.Name
			},
			TenType = 6,
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		};
		db.Insert<MasterTokui>(tenpo);
	}



}
