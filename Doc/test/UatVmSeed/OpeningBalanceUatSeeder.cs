using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>UAT-08の期首残高登録後通し確認用データを隔離SQLiteへ投入する。</summary>
public static class OpeningBalanceUatSeeder {
	public const string TokuiCode = "UATVM-U08-TK";
	public const string ShiireCode = "UATVM-U08-SI";
	public const string FiscalStartDate = "20260701";
	public const string OpeningMonth = "202606";
	public const string OpeningDay = "20260630";
	public const string TargetMonth = "202607";
	public const string TargetDay = "20260715";
	public const int Shime = 99;
	public const long UriOpening = 100_000;
	public const long KaiOpening = 80_000;
	public const long SalesAmount = 10_000;
	public const long SalesTax = 1_000;
	public const long ReceiptAmount = 1_000;
	public const long PurchaseAmount = 20_000;
	public const long PurchaseTax = 2_000;
	public const long PaymentAmount = 2_000;

	public sealed record Result(long TokuiId, long ShiireId, long SalesId, long PurchaseId);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		UpdateDb.WriteVersionInfoAsync(db).GetAwaiter().GetResult();

		var employee = db.Fetch<MasterShain>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("社員マスタがありません。");
		var kin = EnsureCashKind(db);
		var tokui = EnsureTokui(db, employee);
		var shiire = EnsureShiire(db, employee);
		SetFiscalStart(db);
		Clean(db, tokui.Id, shiire.Id);

		long salesId;
		long purchaseId;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			var sale = CreateSale(tokui, employee);
			db.Insert(sale);
			salesId = sale.Id;
			var purchase = CreatePurchase(shiire, employee);
			db.Insert(purchase);
			purchaseId = purchase.Id;
			db.Insert(CreateKin<Tran06Nyukin>(tokui.Id, tokui.Code, tokui.Name, employee, kin, ReceiptAmount));
			db.Insert(CreateKin<Tran07Shiharai>(shiire.Id, shiire.Code, shiire.Name, employee, kin, PaymentAmount));
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}

		trace($"UAT-08専用データ: 期首={FiscalStartDate} 得意先={TokuiCode} 仕入先={ShiireCode}");
		return new Result(tokui.Id, shiire.Id, salesId, purchaseId);
	}

	private static void SetFiscalStart(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterSysman>("order by Id").FirstOrDefault()
			?? throw new InvalidOperationException("システム管理マスタがありません。");
		db.Execute("UPDATE MasterSysman SET FiscalStartDate=@0, ShimeBi=@1 WHERE Id=@2", FiscalStartDate, Shime, row.Id);
	}

	private static MasterTokui EnsureTokui(ExDatabaseSqlite db, MasterShain employee) {
		var row = db.Fetch<MasterTokui>("where Code=@0", TokuiCode).FirstOrDefault();
		if (row == null) {
			row = new MasterTokui {
				Code = TokuiCode,
				Name = "UAT-08期首得意先",
				Ryaku = TokuiCode,
				TenType = 1,
				Id_Shain = employee.Id,
				VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			};
			StampAndInsert(db, row);
		}
		row.TenType = 1;
		row.Shime1 = Shime;
		row.Shime2 = 0;
		row.Shime3 = 0;
		row.PayMonth = 0;
		row.PayDay = Shime;
		row.RateProper = 100;
		row.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		row.TaxRounding = (int)EnumRounding.Round;
		db.Update(row);
		return row;
	}

	private static MasterShiire EnsureShiire(ExDatabaseSqlite db, MasterShain employee) {
		var row = db.Fetch<MasterShiire>("where Code=@0", ShiireCode).FirstOrDefault();
		if (row == null) {
			row = new MasterShiire {
				Code = ShiireCode,
				Name = "UAT-08期首仕入先",
				Ryaku = ShiireCode,
				Id_Shain = employee.Id,
				VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			};
			StampAndInsert(db, row);
		}
		row.Shime1 = Shime;
		row.Shime2 = 0;
		row.Shime3 = 0;
		row.PayMonth = 0;
		row.PayDay = Shime;
		row.RateProper = 100;
		row.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
		row.TaxRounding = (int)EnumRounding.Round;
		db.Update(row);
		return row;
	}

	private static MasterMeisho EnsureCashKind(ExDatabaseSqlite db) {
		var row = db.Fetch<MasterMeisho>("where Kubun=@0 AND Code=@1", MasterMeisho.KubunKin, "01").FirstOrDefault();
		if (row != null) return row;
		row = new MasterMeisho { Kubun = MasterMeisho.KubunKin, KubunName = "入金・支払方法", Code = "01", Name = "現金", Ryaku = "現金" };
		StampAndInsert(db, row);
		return row;
	}

	private static void Clean(ExDatabaseSqlite db, long tokuiId, long shiireId) {
		db.Execute("DELETE FROM Tran00Uriage WHERE Id_Tokui=@0", tokuiId);
		db.Execute("DELETE FROM Tran06Nyukin WHERE Id_Torisaki=@0", tokuiId);
		db.Execute("DELETE FROM SummaryUriKake WHERE Id_Tokui=@0", tokuiId);
		db.Execute("DELETE FROM SummaryUriSei WHERE Id_Tokui=@0", tokuiId);
		db.Execute("DELETE FROM Tran03Shiire WHERE Id_Shiire=@0", shiireId);
		db.Execute("DELETE FROM Tran07Shiharai WHERE Id_Torisaki=@0", shiireId);
		db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0", shiireId);
		db.Execute("DELETE FROM SummaryKaiShi WHERE Id_Shiire=@0", shiireId);
	}

	private static Tran00Uriage CreateSale(MasterTokui tokui, MasterShain employee) {
		var row = new Tran00Uriage {
			DenDay = TargetDay,
			KakeDay = TargetDay,
			Id_Tokui = tokui.Id,
			VTokui = new CodeNameView(tokui.Id, tokui.Code, tokui.Name),
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			Rate = 100,
			KingakuTotal = SalesAmount,
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			TaxRounding = (int)EnumRounding.Round,
			TaxableAmount1 = SalesAmount,
			Tax1 = SalesTax,
			Total = SalesAmount + SalesTax,
			IsPay = 1,
			Jmeisai = [new Tran99Meisai { No = 1, Su = 1, Tanka = (int)SalesAmount, Kingaku = SalesAmount, Id_Tax = 1, TaxRate = 10, Tax = SalesTax }],
		};
		row.EnKubun = EnumUri00.Uriage;
		return row;
	}

	private static Tran03Shiire CreatePurchase(MasterShiire shiire, MasterShain employee) {
		var row = new Tran03Shiire {
			DenDay = TargetDay,
			KakeDay = TargetDay,
			Id_Shiire = shiire.Id,
			VShiire = new CodeNameView(shiire.Id, shiire.Code, shiire.Name),
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
			Rate = 100,
			KingakuTotal = PurchaseAmount,
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			TaxRounding = (int)EnumRounding.Round,
			TaxableAmount1 = PurchaseAmount,
			Tax1 = PurchaseTax,
			Total = PurchaseAmount + PurchaseTax,
			IsPay = 1,
			Jmeisai = [new Tran99Meisai { No = 1, Su = 1, Tanka = (int)PurchaseAmount, Kingaku = PurchaseAmount, Id_Tax = 1, TaxRate = 10, Tax = PurchaseTax }],
		};
		row.EnKubun = EnumShiire.Shiire;
		return row;
	}

	private static T CreateKin<T>(long ownerId, string code, string name, MasterShain employee, MasterMeisho kin, long amount)
		where T : TranKinHeader, new() => new() {
		KakeDay = TargetDay,
		Id_Torisaki = ownerId,
		VTori = new CodeNameView(ownerId, code, name),
		Id_Shain = employee.Id,
		VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		KingakuTotal = amount,
		Jmeisai = [new TranKinMeisai { No = 1, Id_Kin = kin.Id, Code_Kin = kin.Code, Mei_Kin = kin.Name, Kingaku = amount }],
	};

	private static void StampAndInsert<T>(ExDatabaseSqlite db, T row) where T : BaseDbClass {
		var vdate = Common.GetVdate();
		row.Vdc = vdate;
		row.Vdu = vdate;
		db.Insert(row);
	}
}
