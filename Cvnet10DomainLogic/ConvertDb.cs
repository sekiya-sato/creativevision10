using Cvnet10Base;
using Cvnet10Base.Share;
using System.Linq;
using Cvnet10Asset;
using NLog;
using NPoco.Expressions;

namespace Cvnet10DomainLogic;
/// <summary>
/// データベースを変換するクラス
/// </summary>
public class ConvertDb {
	ExDatabase _fromDb;
	ExDatabase _toDb;
	Logger logger;

	public ConvertDb(ExDatabase fromDb, ExDatabase toDb) {
		_fromDb = fromDb;
		_toDb = toDb;
		logger = LogManager.GetCurrentClassLogger();
	}
	public void ConvertAll(bool isInit = true) {
		logger.Info("変換処理開始");
		/*
		CnvMasterSys(isInit);
		CnvMasterMeisho(isInit);
		CnvMasterShain(isInit);
		CnvMasterEndCustomer(isInit);
		CnvMasterShohin(isInit);
		CnvMasterTokui(isInit);
		CnvMasterShiire(isInit);
		 */
		/*
		 */
		logger.Info("変換処理終了");
		return;
	}

	#region 文字列変換サブロジック
	private string getString(Dictionary<string, object> rec, string key) {
		string? ret = String.Empty;
		if (rec.ContainsKey(key)) {
			ret = rec[key]?.ToString();
		}
		if (ret == "." || ret == null)
			ret = String.Empty;
		return ret;
	}
	// 新規：常に非 null を返すオーバーロード（デフォルト値を指定）
	private string getString(Dictionary<string, object> rec, string key, string defaultValue) {
		return getString(rec, key) ?? defaultValue;
	}

	private int getDataInt(Dictionary<string, object> rec, string key) {
		var data = getString(rec, key);
		if (data == null)
			return 0;
		if (int.TryParse(data, out int val))
			return val;
		if (decimal.TryParse(data, out var dec))
			return (int)decimal.Truncate(dec);
		return val;
	}
	#endregion
	/// <summary>
	/// 共通変換処理
	/// </summary>
	/// <typeparam name="T">対象テーブル型</typeparam>
	/// <param name="sql">元DBに対する取得SQL</param>
	/// <param name="isInit"></param>
	/// <param name="createItem"></param>
	/// <returns></returns>
	private int ConvertMaster<T>(string sql, bool isInit, Func<Dictionary<string, object>, T> createItem) {
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql);
		_toDb.CreateTable(typeof(T), isInit);

		if (rows.Count == 0)
			return 0;

		var list = new List<T>(rows.Count);
		foreach (var rec in rows) {
			list.Add(createItem(rec));
		}

		_toDb.BeginTransaction();
		_toDb.InsertBulk<T>(list);
		_toDb.CompleteTransaction();

		return rows.Count;
	}
	/// <summary>
	/// 汎用名称リストの作成(該当コードなしは作成しない)
	/// </summary>
	/// <param name="maxCnt"></param>
	/// <param name="prefix"></param>
	/// <param name="rec"></param>
	/// <returns></returns>
	private List<MasterGeneralMeisho> ConverterGeneralMeisho(int maxCnt, string prefix, Dictionary<string, object> rec) {
		var pairs = Enumerable.Range(1, maxCnt)
			.Select(i => (Kubun: $"{prefix}{i:D2}", Code: getString(rec, $"名称CD{i:D2}", ".")))
			.ToList();

		if (pairs.Count == 0)
			return [];
		// SQL文の生成
		var inClause = string.Join(",", pairs.Select((_, i) => $"(@{i * 2}, @{i * 2 + 1})"));
		var args = pairs.SelectMany(p => new[] { p.Kubun, p.Code }).ToArray();
		var meishoList = _toDb.Fetch<MasterMeisho>(
			$"where (Kubun,Code) in ({inClause})",
			args
		);
		var retList = new List<MasterGeneralMeisho>(pairs.Count);
		foreach (var meisho in meishoList) {
			retList.Add(new MasterGeneralMeisho() {
				Kb = meisho.Kubun,
				Kbname= meisho.KubunName,
				Sid = meisho.Id,
				Cd = meisho.Code,
				Mei = meisho.Name,
			});
		}
		return retList;
	}
	/// <summary>
	/// システム管理マスタ変換 HC$master_syskanri HC$master_systax
	/// </summary>
	public int CnvMasterSys(bool isInit = true) {
		var mstSys = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$master_syskanri");
		var recSys = mstSys[0];
		var mstTax = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$master_systax order by 消費税CD");
		var newSys = new MasterSysman() {
			Name = getString(recSys, "自社名"),
			PostalCode = getString(recSys, "郵便番号"),
			Address1 = getString(recSys, "住所1"),
			Address2 = getString(recSys, "住所2"),
			Address3 = getString(recSys, "住所3"),
			Tel = getString(recSys, "TEL"),
			Mail = getString(recSys, "管理者MAIL"),
			Hp = getString(recSys, "ホームページ"),
			ShimeBi = getDataInt(recSys, "自社締日"),
			ModifyDaysEx = getDataInt(recSys, "修正有効日数"),
			ModifyDaysPre = getDataInt(recSys, "先付有効日数"),
			BankAccount1 = getString(recSys, "振込先1"),
			BankAccount2 = getString(recSys, "振込先2"),
			BankAccount3 = getString(recSys, "振込先3"),
			FiscalStartDate = getString(recSys, "期首年月日", "19010101"),
			Jsub = new List<MasterSysTax>(),
		};
		foreach (var rec in mstTax) {
			var tax = new MasterSysTax() {
				Id = getDataInt(rec, "消費税CD"),
				TaxRate = getDataInt(rec, "消費税率"),
				DateFrom = getString(rec, "新消費税開始日", "19010101"),
				TaxNewRate = getDataInt(rec, "新消費税率"),
			};
			newSys.Jsub.Add(tax);
		}
		_toDb.CreateTable(typeof(MasterSysman), isInit);
		_toDb.Insert<MasterSysman>(newSys);
		return 1;
	}
	/// <summary>
	/// 名称マスタ変換 HC$master_meisho
	/// </summary>
	public int CnvMasterMeisho(bool isInit = true) {
		const string sql = """
    SELECT 
        T.*, 
        m1.名称 AS KubunName
    FROM HC$master_meisho T
    LEFT OUTER JOIN HC$master_meisho m1 
        ON m1.名称区分 = 'IDX' 
        AND T.名称区分 = m1.名称CD
""";
		return ConvertMaster(sql, isInit, rec => new MasterMeisho() {
			Kubun = getString(rec, "名称区分"),
			KubunName = getString(rec, "KubunName"),
			Code = getString(rec, "名称CD"),
			Name = getString(rec, "名称"),
			Ryaku = getString(rec, "略称"),
			Kana = getString(rec, "カナ"),
		});
	}
	/// <summary>
	/// 社員マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterShain(bool isInit = true) {
		const string sql = "select * from HC$master_shain where 社員CD>'.' order by 社員CD"; // 部門 'BMN' 社員分類 'E01'-'E10'
		return ConvertMaster(sql, isInit, rec => {
			var bumonMeisho = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", ["BMN", getString(rec, "部門", ".")]);
			var item = new MasterShain() {
				Code = getString(rec, "社員CD"),
				Name = getString(rec, "名前"),
				Kana = getString(rec, "フリカナ"),
				Mail = getString(rec, "メール"),
				VTenpo = new() {
					Cd = getString(rec, "店舗CD"), // ToDo: 店舗マスタ(得意先マスタ)読み込み後再度残りの項目を設定する
				},
				Id_Bumon = bumonMeisho?.Id ?? 0,
				VBumon = new(bumonMeisho??new()),
			};
			var meiList = ConverterGeneralMeisho(5, "E", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;
			return item;
		});
	}
	/// <summary>
	/// 顧客マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterEndCustomer(bool isInit = true) {
		const string sql = "select * from HC$master_kokyaku where 顧客CD>'.' order by 顧客CD"; // 顧客分類 'K01'-'K10'
		return ConvertMaster(sql, isInit, rec => {
			var item = new MasterEndCustomer() {
				Code = getString(rec, "顧客CD"),
				Name = getString(rec, "顧客名"),
				Kana = getString(rec, "カナ"),
				PostalCode = getString(rec, "郵便番号"),
				Address1 = getString(rec, "住所1"),
				Address2 = getString(rec, "住所2"),
				Address3 = getString(rec, "住所3"),
				Mail = getString(rec, "メール"),
				Tel = getString(rec, "TEL").DefaultIfEmpty(getString(rec, "TEL2")),
				Memo = getString(rec, "拡張メモ"),
				VTenpo = new() {
					Cd = getString(rec, "店舗CD"), // ToDo: 店舗マスタ(得意先マスタ)読み込み後再度残りの項目を設定する
				},
			};
			var meiList = ConverterGeneralMeisho(10, "K", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;
			return item;
		});
	}
	/// <summary>
	/// 顧客マスター変換(Option) 存在するマスタに追加で情報を加える
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvOptionMasterEndCustomer__NoUse() {
		const string sql = "select 顧客CD,拡張メモ from HC$master_kokyaku where 顧客CD>'.' and 拡張メモ>'.'  order by 顧客CD"; // 顧客分類 'K01'-'K10'
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql);
		if (rows.Count == 0)
			return 0;
		int cnt = 0;
		#region ロジック別の処理(1件ずつ取得および更新 / 一括取得後に更新 / 更新のみ逐次実行)
		// 1) 1件ずつ取得および更新
		/*
		foreach (var rec in rows) {
			var item = _toDb.FirstOrDefault<MasterEndCustomer>("where Code=@0", getString(rec, "顧客CD"));
			if(item !=null && item.Id > 0) {
				item.Memo = getString(rec, "拡張メモ");
				var ret = _toDb.Update<MasterEndCustomer>(item, c => c.Memo);
				if (ret > 0)
					cnt++;
			}
		}
		*/
		// 2) 一括取得後に更新
		/*
		var list = _toDb.Fetch<MasterEndCustomer>("where Code in (@0)", codes);
		foreach (var item in list) {
			var rec = rows.FirstOrDefault(r => getString(r, "顧客CD") == item.Code);
			if (rec != null) {
				item.Memo = getString(rec, "拡張メモ");
				var ret = _toDb.Update<MasterEndCustomer>(item, c => c.Memo);
				if (ret > 0)
					cnt++;
			}
		}
		*/
		// 3) 更新のみ逐次実行
		#endregion
		_toDb.BeginTransaction();
		foreach (var rec in rows) {
			var code = getString(rec, "顧客CD");
			var memo = getString(rec, "拡張メモ");
			if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(memo))
				continue;
			var ret = _toDb.Update<MasterEndCustomer>("set Memo=@0 where Code=@1", memo, code); // "Update MasterEndCustomer "
			if (ret > 0)
				cnt += ret;
		}
		_toDb.CompleteTransaction();
		return cnt;
	}
	/// <summary>
	/// 商品マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterShohin(bool isInit = true) {
		const string sql = "select * from HC$master_shohin where 商品CD>'.' order by 商品CD"; // 商品分類 'B01'-'B20'
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql);
		_toDb.CreateTable(typeof(MasterShohin), isInit);

		if (rows.Count == 0)
			return 0;

		var list = new List<MasterShohin>(rows.Count);
		foreach (var rec in rows) {
			var code = getString(rec, "商品CD");

			var janRows = _fromDb.Fetch<Dictionary<string, object>>(
				"select * from HC$MASTER_SHOHIN_JAN where 商品CD=@0", code);

			var genkaRows = _fromDb.Fetch<Dictionary<string, object>>(
				"select * from HC$MASTER_SHOHIN_GENKA where 商品CD=@0", code);

			var gradeRows = _fromDb.Fetch<Dictionary<string, object>>(
				"select * from HC$MASTER_SHOHIN_GRADE where 商品CD=@0", code);

			var colsiz = janRows
				.Select(r => {
					var col = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", ["COL", getString(r, "色CD")]);
					var siz = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", ["SIZ", getString(r, "サイズCD")]);
					return new MasterShohinColSiz() {
						Code_Col = getString(r, "色CD"),
						Id_Col = col?.Id ?? 0,
						Mei_Col = col?.Name ?? string.Empty,
						Code_Siz = getString(r, "サイズCD"),
						Id_Siz = siz?.Id ?? 0,
						Mei_Siz = siz?.Name ?? string.Empty,
						Jan1 = getString(r, "JANコード1"),
						Jan2 = getString(r, "JANコード2"),
						Jan3 = getString(r, "JANコード3"),
					};
				}).ToList();

			var genka = genkaRows
				.Select(r => new MasterShohinGenka() {
					No = getDataInt(r, "行NO"),
					TankaGenka = getDataInt(r, "原価"),
					TankaShiire = getDataInt(r, "仕入価格"),
				}).OrderBy(x => x.No).ToList();

			var grade = gradeRows
				.Select(r => new MasterShohinGrade() {
					No = getDataInt(r, "行NO"),
					Hinshitu = getString(r, "品質"),
					Percent = getDataInt(r, "パーセント"),
				}).OrderBy(x => x.No).ToList();
			var meisho = _toDb.Fetch<MasterMeisho>("""
where (Kubun ='BRD' and Code =@0) OR (Kubun ='ITM' and Code =@1) OR (Kubun ='TNJ' and Code =@2)
OR (Kubun ='SZN' and Code =@3) OR (Kubun ='SZI' and Code =@4) OR (Kubun ='GEN' and Code =@5) OR (Kubun ='MKR' and Code =@6)
"""
			, [getString(rec, "ブランドCD"),
				getString(rec, "アイテムCD"),
				getString(rec, "展示会CD"),
				getString(rec, "シーズンCD"),
				getString(rec, "素材CD"),
				getString(rec, "原産国CD"),
				getString(rec, "メーカーCD")]
			) ?? [];
			if (meisho.Count == 0 && colsiz.Count == 0)
				continue; // 1つもマスタがないのは正規商品ではない

			var item = new MasterShohin() {
				Code = code,
				Name = getString(rec, "商品名"),
				Ryaku = getString(rec, "略称"),
				TankaJodaiOrg = getDataInt(rec, "元上代"),
				TankaJodai = getDataInt(rec, "上代"),
				TankaGenka = getDataInt(rec, "原価"),
				TankaShiire = getDataInt(rec, "仕入価格"),
				DayShukka = getString(rec, "デリバリー日", "19010101"),
				DayNohin = getString(rec, "納品日", "19010101"),
				DayTento = getString(rec, "店頭投入日", "19010101"),
				Id_Tax = getDataInt(rec, "消費税CD"),
				IsZaiko = getDataInt(rec, "在庫管理FLG"),
				MakerHin = getString(rec, "メーカー品番"),
				SizeKu = getString(rec, "商品サイズ区分", "SIZ"),
				VSoko = new() {
					Cd = getString(rec, "基準倉庫CD"), // ToDo: 店舗マスタ(得意先マスタ)読み込み後再度残りの項目を設定する
				},
				Memo = getString(rec, "メモ"),
				Jcolsiz = colsiz.Count > 0 ? colsiz : null,
				Jgenka = genka.Count > 0 ? genka : null,
				Jgrade = grade.Count > 0 ? grade : null,
			};
			if (meisho.Count > 0) {
				item.VBrand = new(meisho.FirstOrDefault(c => c.Kubun == "BRD") ?? new());
				item.Id_Brand = item.VBrand.Sid;
				item.VItem = new(meisho.FirstOrDefault(c => c.Kubun == "ITM") ?? new());
				item.Id_Item = item.VItem.Sid;
				item.VTenji = new(meisho.FirstOrDefault(c => c.Kubun == "TNJ") ?? new());
				item.Id_Tenji = item.VTenji.Sid;
				item.VSeason = new(meisho.FirstOrDefault(c => c.Kubun == "SZN") ?? new());
				item.Id_Season = item.VSeason.Sid;
				item.VMaterial = new(meisho.FirstOrDefault(c => c.Kubun == "SZI") ?? new());
				item.Id_Material = item.VMaterial.Sid;
				item.VCountry = new(meisho.FirstOrDefault(c => c.Kubun == "GEN") ?? new());
				item.Id_Country = item.VCountry.Sid;
				item.VMaker = new(meisho.FirstOrDefault(c => c.Kubun == "MKR") ?? new());
				item.Id_Maker = item.VMaker.Sid;
			}
			var meiList = ConverterGeneralMeisho(10, "B", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;

			list.Add(item);
		}

		_toDb.BeginTransaction();
		_toDb.InsertBulk<MasterShohin>(list);
		_toDb.CompleteTransaction();

		return list.Count;
	}
	/// <summary>
	/// 得意先マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterTokui(bool isInit = true) {
		const string sql = "select * from HC$MASTER_TOKUI where 得意先CD>'.' order by 得意先CD";
		return ConvertMaster(sql, isInit, rec => {
			var shain = _toDb.FirstOrDefault<MasterShain>("where Code=@0", getString(rec, "営業担当CD"));
			var item = new MasterTokui() {
				Code = getString(rec, "得意先CD"),
				Name = getString(rec, "得意先名"),
				Ryaku = getString(rec, "略称"),
				Kana = getString(rec, "カナ"),
				PostalCode = getString(rec, "郵便番号"),
				Address1 = getString(rec, "住所1"),
				Address2 = getString(rec, "住所2"),
				Address3 = getString(rec, "住所3"),
				Tel = getString(rec, "TEL"),
				Id_Shain = shain?.Id ?? 0,
				VShain = new(shain?.Id ?? 0, shain?.Code ?? string.Empty, shain?.Name ?? string.Empty),
				RateProper = getDataInt(rec, "掛率"),
				RateSale = getDataInt(rec, "セール掛率"),
				//Shime1 = (EnumShime)getDataInt(rec, "締日"),
				//Shime2 = (EnumShime)getDataInt(rec, "締日2"),
				//Shime3 = (EnumShime)getDataInt(rec, "締日3"),
				Shime1 = getDataInt(rec, "締日"),
				Shime2 = getDataInt(rec, "締日2"),
				Shime3 = getDataInt(rec, "締日3"),
				PayMonth = getDataInt(rec, "入金予定月"),
				PayDay = getDataInt(rec, "入金予定日"),
				//PayDay = (EnumShime)getDataInt(rec, "入金予定日"),
				//TenType = (EnumTokui)getDataInt(rec, "店種区分"),
				//IsZaiko = (EnumYesNo)getDataInt(rec, "在庫管理FLG"),
				TenType = getDataInt(rec, "店種区分"),
				IsZaiko = getDataInt(rec, "在庫管理FLG"),
				IsPay = getDataInt(rec, "請求印刷"),
				Jdetail = new MasterToriDetail() {
					BankAccount1 = getString(rec, "振込先1"),
					BankAccount2 = getString(rec, "振込先2"),
					BankAccount3 = getString(rec, "振込先3"),
				},
			};
			return item;
		});
	}
	/// <summary>
	/// 仕入先マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterShiire(bool isInit = true) {
		const string sql = "select * from HC$MASTER_SIIRE where 仕入先CD>'.' order by 仕入先CD";
		return ConvertMaster(sql, isInit, rec => {
			var shain = _toDb.FirstOrDefault<MasterShain>("where Code=@0", getString(rec, "入力社員CD"));
			var item = new MasterShiire() {
				Code = getString(rec, "仕入先CD"),
				Name = getString(rec, "仕入先名"),
				Ryaku = getString(rec, "略称"),
				Kana = getString(rec, "カナ"),
				PostalCode = getString(rec, "郵便番号"),
				Address1 = getString(rec, "住所1"),
				Address2 = getString(rec, "住所2"),
				Address3 = getString(rec, "住所3"),
				Tel = getString(rec, "TEL"),
				Id_Shain = shain?.Id ?? 0,
				VShain = new(shain?.Id ?? 0, shain?.Code ?? string.Empty, shain?.Name ?? string.Empty),
				RateProper = getDataInt(rec, "掛率"),
				RateSale = getDataInt(rec, "掛率2"),
				Shime1 = getDataInt(rec, "締日"),
				Shime2 = getDataInt(rec, "締日2"),
				Shime3 = getDataInt(rec, "締日3"),
				PayMonth = getDataInt(rec, "入金予定月"),
				PayDay = getDataInt(rec, "入金予定日"),
				IsPay = getDataInt(rec, "支払印刷"),
				Jdetail = new MasterToriDetail() {
					BankAccount1 = $"{getString(rec, "振込銀行")} {getString(rec, "振込支店")} {getString(rec, "振込種別")} {getString(rec, "振込口座")}"
				},
			};
			var meiList = ConverterGeneralMeisho(10, "S", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;
			return item;
		});
	}
}
