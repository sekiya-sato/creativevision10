using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// C-09 生地・付属仕入（<see cref="Tran02Material"/>）の買掛合算を支払計算画面から検証するための
/// データを投入する。
/// </summary>
/// <remarks>
/// <para>
/// `SummaryDb.CalcSummaryKaiShi`は`Tran03Shiire`と`Tran02Material`を合算するが、区分99（その他）の
/// 扱いが異なる。`Tran03Shiire`の区分99は仕入へ畳み込むのに対し、`Tran02Material`の区分99は
/// **仕入ではなく消費税へ全額を積む**（生地・付属の税調整目的の伝票として使うため）。
/// </para>
/// <para>
/// UAT専用の仕入先を1件だけ追加し、`Tran02Material`（仕入／仕入返品／値引／その他）を投入する。
/// 対象はこの仕入先のコード範囲に限定されるため、支払計算画面から実行しても
/// 他の仕入先の`SummaryKaiShi`には影響しない（`CalcSummaryKaiShi`のDELETE/INSERTはコード範囲で絞られる）。
/// </para>
/// </remarks>
public static class MaterialSeeder {
	/// <summary>検証用仕入先のコード。UAT専用と分かる値にする。</summary>
	public const string ShiireCode = "UATVM-MTL";
	/// <summary>検証用仕入先の締日（末日）。</summary>
	public const int Shime = 99;
	/// <summary>伝票日付（対象月202607、末締めの期間内）。</summary>
	public const string DenDay = "20260715";

	/// <summary>金額はすべて異なる値にして、混入時に判別できるようにする。</summary>
	public const int Shiire = 30_000;
	public const int ShiireTax = 3_000;
	public const int Henpin = 4_000;
	public const int HenpinTax = 400;
	public const int Nebiki = 1_000;
	public const int NebikiTax = 100;
	/// <summary>区分99（その他）。仕入へは畳み込まず、Total全額が消費税へ積まれる。</summary>
	public const int Other = 2_000;

	/// <summary>投入結果と期待値。</summary>
	public sealed record Result(
		long ShiireId, string ShiireCode, int Shime, string BillingMonth, string DayTo,
		int ExpectedShiire, int ExpectedHenpin, int ExpectedNebiki, int ExpectedTax, int ExpectedTotalShiire);

	public static Result Seed(string dbPath, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		if (!File.Exists(dbPath)) throw new FileNotFoundException("対象DBが見つかりません。", dbPath);

		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = dbPath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };

		var shiire = EnsureShiire(db, trace);
		Clean(db, shiire.Id, trace);
		InsertMaterial(db, shiire.Id, trace);

		// CalcSummaryKaiShiの支払期間(末締め)は「前月末日+1〜当月末日」。202607なら20260701〜20260731。
		var dayTo = "20260731";
		// 仕入=返品分を除いた正味、税=仕入税-返品税+値引税+区分99全額（設計どおりの符号規則）。
		var expectedShiire = Shiire; // Tran02Materialの区分99はShiireへ畳み込まない
		var expectedHenpin = Henpin;
		var expectedNebiki = Nebiki;
		var expectedTax = ShiireTax - HenpinTax + NebikiTax + Other;
		var expectedTotalShiire = expectedShiire - expectedHenpin - expectedNebiki + expectedTax;

		trace($"期待値 202607: 仕入={expectedShiire:N0} 返品={expectedHenpin:N0} 値引={expectedNebiki:N0}"
			+ $" 税={expectedTax:N0}（内訳: 仕入税{ShiireTax:N0}-返品税{HenpinTax:N0}+値引税{NebikiTax:N0}+区分99全額{Other:N0}）"
			+ $" 仕入額={expectedTotalShiire:N0}");

		return new Result(shiire.Id, ShiireCode, Shime, "202607", dayTo,
			expectedShiire, expectedHenpin, expectedNebiki, expectedTax, expectedTotalShiire);
	}

	private static MasterShiire EnsureShiire(ExDatabaseSqlite db, Action<string> trace) {
		var existing = db.Fetch<MasterShiire>("where Code=@0", ShiireCode).FirstOrDefault();
		if (existing != null) {
			trace($"仕入先 {ShiireCode} は既に存在 Id={existing.Id} 締日={existing.Shime1}");
			return existing;
		}

		var employee = db.Fetch<MasterShain>("order by Id").First();
		var shiire = new MasterShiire {
			Code = ShiireCode,
			Name = "UAT-VM 生地・付属仕入検証用",
			Ryaku = "UAT-VM MTL",
			Shime1 = Shime,
			PayMonth = 0,
			PayDay = 99,
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		};
		var vdate = Common.GetVdate();
		shiire.Vdc = vdate;
		shiire.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(shiire);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"仕入先 {ShiireCode} を追加 Id={shiire.Id} 締日={Shime}");
		return shiire;
	}

	private static void Clean(ExDatabaseSqlite db, long shiireId, Action<string> trace) {
		var material = db.Execute("DELETE FROM Tran02Material WHERE Id_Shiire=@0", shiireId);
		var kaiKake = db.Execute("DELETE FROM SummaryKaiKake WHERE Id_Shiire=@0", shiireId);
		var kaiShi = db.Execute("DELETE FROM SummaryKaiShi WHERE Id_Shiire=@0", shiireId);
		trace($"掃除 Tran02Material={material} SummaryKaiKake={kaiKake} SummaryKaiShi={kaiShi}");
	}

	private static void InsertMaterial(ExDatabaseSqlite db, long shiireId, Action<string> trace) {
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			Insert(db, shiireId, EnumShiire.Shiire, Shiire, ShiireTax);
			Insert(db, shiireId, EnumShiire.Henpin, Henpin, HenpinTax);
			Insert(db, shiireId, EnumShiire.Nebiki, Nebiki, NebikiTax);
			Insert(db, shiireId, EnumShiire.Other, Other, 0); // その他は税0で投入、Total全額が集計側でTaxへ積まれることを確認する
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"生地・付属仕入を4件投入（仕入={Shiire:N0} 返品={Henpin:N0} 値引={Nebiki:N0} その他={Other:N0}）");
	}

	private static void Insert(ExDatabaseSqlite db, long shiireId, EnumShiire kubun, int total, int tax) {
		// 集計SQL（CalcSummaryKaiShi）はShiire/Henpin/Nebiki/Sonota99をヘッダKingakuTotal(税抜)からSUMし、
		// 消費税(Tax1)は伝票単位(TaxCalcUnit=Slip)ぶんをヘッダTax1からそのまま合算する（仕様3.5）。
		// このシードは「税額は伝票が確定済み」ケース(伝票単位)を検証するため、任意の税額をTax1へそのまま入れ、
		// TaxRounding/請求単位側の再丸めを経由しないことで値がそのまま伝わることを確認する。
		// 区分99(その他)はTax1/TaxableAmount1を0のままにする(Sonota99としてKingakuTotalが直接消費税へ積まれるため、
		// 二重計上を避ける)。
		var tran = new Tran02Material {
			DenDay = DenDay,
			KakeDay = DenDay,
			Id_Shiire = shiireId,
			KingakuTotal = total,
			TaxCalcUnit = (int)EnumTaxCalcUnit.Slip,
			TaxableAmount1 = kubun == EnumShiire.Other ? 0 : total,
			Tax1 = tax,
			Total = total + tax,
			IsPay = 1,
		};
		tran.EnKubun = kubun;
		db.Insert(tran);
	}
}
