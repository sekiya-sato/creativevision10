using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// 大メニュー「受注・展示会」UAT（2026-09-26）用の受注網羅データを用意する。
/// 対象月は2026/07（既存受注・売上と衝突しない月であることを事前にDBで確認済み）。
/// 既存行の更新・削除は行わず、専用コード(UATJ-接頭辞)の行だけを対象にする。
/// 再実行しても重複しないよう、専用コードの受注・売上は都度削除してから作り直す。
/// </summary>
public static class JyuchuUatSeeder {
	public const string TokuiCode1 = "UATJ-TK1";
	public const string TokuiCode2 = "UATJ-TK2";
	public const string WarehouseCode = "UATVM-JS-SK"; // 既存(UAT-02)倉庫を読み取り専用で流用する
	public const string TenjiCode = "UATJ-TNJ1";
	public const string ShohinCode1 = "UATJ-P01"; // 展示会・ブランド・アイテム設定あり、色サイズ2種
	public const string ShohinCode2 = "UATJ-P02"; // 展示会未設定、色サイズ1種
	public const string Month = "202607";

	public sealed record Result(
		long Tokui1Id, string Tokui1Code, long Tokui2Id, string Tokui2Code,
		long WarehouseId, string WarehouseCode,
		long Employee1Id, string Employee1Code, long Employee2Id, string Employee2Code,
		long Shohin1Id, string Shohin1Code, long Shohin2Id, string Shohin2Code,
		long TenjiId, string TenjiCode,
		int OrderCount, int TotalSu, long TotalKingaku);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
		using var connection = new SqliteConnection(cs);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };

		var employees = db.Fetch<MasterShain>("order by Id").Take(2).ToList();
		if (employees.Count < 2) throw new InvalidOperationException("社員マスタが2件未満です。");
		var employee1 = employees[0];
		var employee2 = employees[1];
		var warehouse = db.Fetch<MasterTokui>("where Code=@0", WarehouseCode).FirstOrDefault()
			?? throw new InvalidOperationException($"倉庫 {WarehouseCode} がありません（先に juchushipping シナリオを一度実行してください）。");
		var colWhite = db.Fetch<MasterMeisho>("where Kubun='COL' order by Id").First();
		var colKinari = db.Fetch<MasterMeisho>("where Kubun='COL' order by Id").Skip(1).First();
		var sizS = db.Fetch<MasterMeisho>("where Kubun='SIZ' order by Id").First();
		var sizM = db.Fetch<MasterMeisho>("where Kubun='SIZ' order by Id").Skip(1).First();
		var brand = db.Fetch<MasterMeisho>("where Kubun='BRD' order by Id").First();
		var item = db.Fetch<MasterMeisho>("where Kubun='ITM' order by Id").First();

		var beforeCount = db.Fetch<Tran12Jyuchu>("where DenDay BETWEEN @0 AND @1", Month + "01", Month + "31").Count;
		trace($"投入前: 対象月{Month}の既存受注 {beforeCount} 件");

		var tokui1 = EnsureTokui(db, TokuiCode1, "UAT-J 受注UAT得意先1", employee1, trace);
		var tokui2 = EnsureTokui(db, TokuiCode2, "UAT-J 受注UAT得意先2", employee2, trace);
		var tenji = EnsureTenji(db, employee1, trace);
		var shohin1 = EnsureShohin(db, ShohinCode1, "UAT-J 受注UAT商品1(展示会)", warehouse, brand, item, tenji, colWhite, sizS, colKinari, sizM, employee1, trace);
		var shohin2 = EnsureShohin(db, ShohinCode2, "UAT-J 受注UAT商品2(展示会未設定)", warehouse, null, item, null, colWhite, sizS, null, null, employee1, trace);

		CleanExisting(db, tokui1.Id, tokui2.Id, trace);

		var col1 = ColSiz(shohin1, colWhite.Id, sizS.Id);
		var col2 = ColSiz(shohin1, colKinari.Id, sizM.Id);
		var col3 = ColSiz(shohin2, colWhite.Id, sizS.Id);

		var orders = new List<Tran12Jyuchu>();

		// J01: 通常/未完了/納品日あり(未来)/得意先1/担当1/色サイズ複数(展示会商品)
		orders.Add(BuildOrder(db, "20260705", tokui1, employee1, warehouse, (int)EnumJuchu.Juchu, 0, "20260901",
			[Line(1, shohin1, col1, 10, 1500, 3000, 1200), Line(2, shohin1, col2, 6, 1500, 3000, 1200)]));
		// J02: 通常/未完了/納品日あり(過去=納期遅れ)/得意先1/担当1/展示会未設定商品
		orders.Add(BuildOrder(db, "20260706", tokui1, employee1, warehouse, (int)EnumJuchu.Juchu, 0, "20260701",
			[Line(1, shohin2, col3, 8, 1000, 1500, 700)]));
		// J03: 通常/完了(EndFlag=1)/納品日あり(過去)/得意先2/担当2
		orders.Add(BuildOrder(db, "20260710", tokui2, employee2, warehouse, (int)EnumJuchu.Juchu, 1, "20260715",
			[Line(1, shohin1, col1, 6, 1500, 3000, 1200)]));
		// J04: 返品等(Kubun=20)/未完了/納品日なし/得意先2/担当2
		orders.Add(BuildOrder(db, "20260712", tokui2, employee2, warehouse, (int)EnumJuchu.Henpin, 0, "",
			[Line(1, shohin2, col3, 3, 1000, 1500, 700)]));
		// J05: 通常/未完了/納品日あり(過去=納期遅れ)/得意先1/担当2
		orders.Add(BuildOrder(db, "20260715", tokui1, employee2, warehouse, (int)EnumJuchu.Juchu, 0, "20260720",
			[Line(1, shohin1, col2, 4, 1500, 3000, 1200)]));
		// J06: 追加受注(Kubun=11、受注帯に含む)/未完了/納品日あり(未来)/得意先2/担当1
		orders.Add(BuildOrder(db, "20260718", tokui2, employee1, warehouse, (int)EnumJuchu.FollowUpJuchu, 0, "20260910",
			[Line(1, shohin2, col3, 5, 1000, 1500, 700)]));
		// J07: 通常/未完了/納品日なし/得意先1/担当1/売上一部あり(残計算用)
		var order7 = BuildOrder(db, "20260720", tokui1, employee1, warehouse, (int)EnumJuchu.Juchu, 0, "",
			[Line(1, shohin1, col1, 12, 1500, 3000, 1200)]);
		orders.Add(order7);
		// J08: 通常/未完了/納品日あり(過去)/得意先2/担当2/複数商品明細
		orders.Add(BuildOrder(db, "20260722", tokui2, employee2, warehouse, (int)EnumJuchu.Juchu, 0, "20260723",
			[Line(1, shohin2, col3, 2, 1000, 1500, 700), Line(2, shohin1, col2, 2, 1500, 3000, 1200)]));
		// J09: 通常/未完了/納品日あり(未来)/得意先1/担当1/展示会商品単品
		orders.Add(BuildOrder(db, "20260725", tokui1, employee1, warehouse, (int)EnumJuchu.Juchu, 0, "20260930",
			[Line(1, shohin1, col1, 1, 1500, 3000, 1200)]));

		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			foreach (var order in orders) db.Insert(order);
			db.CompleteTransaction();
		} catch { db.AbortTransaction(); throw; }

		// J07に紐づく一部出荷売上（受注残5件は自動計算されず、明示的にTran00Uriageで作る規約）。
		var partialSale = new Tran00Uriage {
			DenDay = "20260721", Id_Tokui = tokui1.Id, VTokui = new CodeNameView(tokui1.Id, tokui1.Code, tokui1.Name),
			Id_Soko = warehouse.Id, VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
			Id_Shain = employee1.Id, VShain = new CodeNameView(employee1.Id, employee1.Code, employee1.Name),
			Kubun = 10, Rate = 100, RelateNo1 = order7.Id,
			SuTotal = 5, KingakuTotal = 7500, JodaiTotal = 15000, GedaiTotal = 6000,
			Jmeisai = [Line(1, shohin1, col1, 5, 1500, 3000, 1200)],
		};
		var vdateSale = Common.GetVdate(); partialSale.Vdc = vdateSale; partialSale.Vdu = vdateSale;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(partialSale); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }

		var afterCount = db.Fetch<Tran12Jyuchu>("where DenDay BETWEEN @0 AND @1", Month + "01", Month + "31").Count;
		var totalSu = orders.Sum(x => x.SuTotal);
		var totalKingaku = orders.Sum(x => x.KingakuTotal);
		trace($"投入後: 対象月{Month}の受注 {afterCount} 件（投入{orders.Count}件）。数量計={totalSu} 金額計={totalKingaku} 売上(J07一部)=5/12");

		return new Result(tokui1.Id, tokui1.Code, tokui2.Id, tokui2.Code, warehouse.Id, warehouse.Code,
			employee1.Id, employee1.Code, employee2.Id, employee2.Code,
			shohin1.Id, shohin1.Code, shohin2.Id, shohin2.Code, tenji.Id, tenji.Code,
			orders.Count, totalSu, totalKingaku);
	}

	static (long Id_Col, long Id_Siz, string CCol, string NCol, string CSiz, string NSiz) ColSiz(MasterShohin shohin, long idCol, long idSiz) {
		var row = shohin.Jcolsiz!.Single(x => x.Id_Col == idCol && x.Id_Siz == idSiz);
		return (row.Id_Col, row.Id_Siz, row.Code_Col, row.Mei_Col, row.Code_Siz, row.Mei_Siz);
	}

	static Tran99Meisai Line(int no, MasterShohin shohin, (long Id_Col, long Id_Siz, string CCol, string NCol, string CSiz, string NSiz) cs,
		int su, int tanka, int jodai, int gedai) => new() {
		No = no, Id_Shohin = shohin.Id, Code_Shohin = shohin.Code, Mei_Shohin = shohin.Name,
		Id_Col = cs.Id_Col, Code_Col = cs.CCol, Mei_Col = cs.NCol, Id_Siz = cs.Id_Siz, Code_Siz = cs.CSiz, Mei_Siz = cs.NSiz,
		JanCode = string.Empty, Su = su, Tanka = tanka, Kingaku = (long)su * tanka, Jodai = jodai, Gedai = gedai, Id_Tax = 1,
	};

	static Tran12Jyuchu BuildOrder(ExDatabaseSqlite db, string denDay, MasterTokui tokui, MasterShain shain, MasterTokui warehouse,
		int kubun, int endFlag, string nouhinDay, List<Tran99Meisai> meisai) {
		var order = new Tran12Jyuchu {
			DenDay = denDay, NouhinDay = nouhinDay, Id_Tokui = tokui.Id,
			VTokui = new CodeNameView(tokui.Id, tokui.Code, tokui.Name),
			Id_Soko = warehouse.Id, VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
			Id_Shain = shain.Id, VShain = new CodeNameView(shain.Id, shain.Code, shain.Name),
			Kubun = kubun, EndFlag = endFlag, Rate = 100,
			SuTotal = meisai.Sum(x => x.Su), KingakuTotal = meisai.Sum(x => x.Kingaku),
			JodaiTotal = meisai.Sum(x => (long)x.Su * x.Jodai), GedaiTotal = meisai.Sum(x => (long)x.Su * x.Gedai),
			Jmeisai = meisai,
		};
		var vdate = Common.GetVdate(); order.Vdc = vdate; order.Vdu = vdate;
		return order;
	}

	static void CleanExisting(ExDatabaseSqlite db, long tokui1Id, long tokui2Id, Action<string> trace) {
		var orders = db.Fetch<Tran12Jyuchu>("where Id_Tokui IN (@0,@1) AND DenDay BETWEEN @2 AND @3", tokui1Id, tokui2Id, Month + "01", Month + "31");
		var ids = orders.Count == 0 ? string.Empty : string.Join(",", orders.Select(x => x.Id));
		var sales = ids.Length == 0 ? 0 : db.Execute($"DELETE FROM Tran00Uriage WHERE RelateNo1 IN ({ids})");
		var removed = ids.Length == 0 ? 0 : db.Execute($"DELETE FROM Tran12Jyuchu WHERE Id IN ({ids})");
		trace($"再実行のため既存UATJ受注を掃除: 受注={removed} 紐づく売上={sales}");
	}

	static MasterTokui EnsureTokui(ExDatabaseSqlite db, string code, string name, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterTokui>("where Code=@0", code).FirstOrDefault();
		if (row != null) return row;
		row = new MasterTokui {
			Code = code, Name = name, Ryaku = code, TenType = 1, IsZaiko = 0,
			Id_Shain = employee.Id, VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		};
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"得意先 {code} を追加 Id={row.Id}");
		return row;
	}

	static MasterMeisho EnsureTenji(ExDatabaseSqlite db, MasterShain employee, Action<string> trace) {
		var row = db.Fetch<MasterMeisho>("where Kubun='TNJ' AND Code=@0", TenjiCode).FirstOrDefault();
		if (row != null) return row;
		row = new MasterMeisho { Kubun = "TNJ", Code = TenjiCode, Name = "UAT-J 受注UAT展示会1" };
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		trace($"展示会 {TenjiCode} を追加 Id={row.Id}");
		return row;
	}

	static MasterShohin EnsureShohin(ExDatabaseSqlite db, string code, string name, MasterTokui warehouse,
		MasterMeisho? brand, MasterMeisho item, MasterMeisho? tenji,
		MasterMeisho col1, MasterMeisho siz1, MasterMeisho? col2, MasterMeisho? siz2, MasterShain employee, Action<string> trace) {
		var existing = db.Fetch<MasterShohin>("where Code=@0", code).FirstOrDefault();
		if (existing != null) return existing;
		List<MasterShohinColSiz> jcolsiz = [
			new() { Id_Col = col1.Id, Code_Col = col1.Code, Mei_Col = col1.Name, Id_Siz = siz1.Id, Code_Siz = siz1.Code, Mei_Siz = siz1.Name },
		];
		if (col2 != null && siz2 != null) {
			jcolsiz.Add(new MasterShohinColSiz { Id_Col = col2.Id, Code_Col = col2.Code, Mei_Col = col2.Name, Id_Siz = siz2.Id, Code_Siz = siz2.Code, Mei_Siz = siz2.Name });
		}
		var row = new MasterShohin {
			Code = code, Name = name, Id_Soko = warehouse.Id, VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
			Id_Tax = 1, IsZaiko = 1, TankaJodaiOrg = 1500, TankaJodai = 1500, TankaGenka = 1200, TankaShiire = 1200,
			Id_Brand = brand?.Id ?? 0, VBrand = brand == null ? new CodeNameView() : new CodeNameView(brand.Id, brand.Code, brand.Name),
			Id_Item = item.Id, VItem = new CodeNameView(item.Id, item.Code, item.Name),
			Id_Tenji = tenji?.Id ?? 0, VTenji = tenji == null ? new CodeNameView() : new CodeNameView(tenji.Id, tenji.Code, tenji.Name),
			Jcolsiz = jcolsiz,
		};
		var vdate = Common.GetVdate(); row.Vdc = vdate; row.Vdu = vdate;
		try { db.BeginTransaction(IsolationLevel.Serializable); db.Insert(row); db.CompleteTransaction(); }
		catch { db.AbortTransaction(); throw; }
		db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id_Shohin=@0", row.Id);
		db.Execute(DerivedShohinColSiz.InsertSql, row.Id);
		trace($"商品 {code} を追加 Id={row.Id}");
		return row;
	}
}
