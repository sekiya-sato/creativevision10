using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NPoco;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tests.CvServer;

/// <summary>
/// MasterCascadeDb (Master系V*列の伝播) のテスト
/// </summary>
[TestClass]
public class MasterCascadeDbTests {
	private ExDatabaseSqlite? _db;

	[TestInitialize]
	public void Initialize() {
		var conn = new SqliteConnection("Data Source=:memory:");
		conn.Open();
		_db = new ExDatabaseSqlite(conn);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
	}

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");

	/// <summary>伝播定義に登場する全テーブルを作成する</summary>
	private void CreateAllTables() {
		var types = MasterCascadeDb.VRules
			.Select(r => r.Target)
			.Concat(MasterCascadeDb.VRules.Select(r => r.Source))
			.Distinct();
		foreach (var t in types) {
			Db.CreateTable(t, true, false);
		}
	}

	private static CodeNameView Cnv(long sid, string cd, string mei) => new() { Sid = sid, Cd = cd, Mei = mei };

	/// <summary>T1: MasterMeisho(BRD)の名称変更が MasterShohin.VBrand に反映される</summary>
	[TestMethod]
	public void CascadeFromMaster_MasterMeisho_UpdatesShohinVBrand() {
		CreateAllTables();
		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, KubunName = "ブランド", Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		var shohin = new MasterShohin {
			Code = "0001",
			Name = "サンプル商品",
			Id_Brand = brand.Id,
			VBrand = Cnv(brand.Id, "01", "旧ブランド"),
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);

		var cascade = new MasterCascadeDb(Db);
		var cnt = cascade.CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "新ブランド", vdate: 999);

		Assert.AreEqual(1, cnt, "更新行数");
		var after = Db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual(brand.Id, after.VBrand.Sid, "VBrand.Sid");
		Assert.AreEqual("01", after.VBrand.Cd, "VBrand.Cd");
		Assert.AreEqual("新ブランド", after.VBrand.Mei, "VBrand.Mei");
		Assert.AreEqual(999, after.Vdu, "Vduが伝播時の値で更新される");
	}

	/// <summary>T1-2: Idが一致しない行は更新されない</summary>
	[TestMethod]
	public void CascadeFromMaster_LeavesOtherRowsUnchanged() {
		CreateAllTables();
		var brand1 = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "ブランド1", Vdc = 1, Vdu = 1 };
		var brand2 = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "02", Name = "ブランド2", Vdc = 1, Vdu = 1 };
		Db.Insert(brand1);
		Db.Insert(brand2);
		var target = new MasterShohin { Code = "0001", Id_Brand = brand1.Id, VBrand = Cnv(brand1.Id, "01", "ブランド1"), Vdc = 1, Vdu = 1 };
		var other = new MasterShohin { Code = "0002", Id_Brand = brand2.Id, VBrand = Cnv(brand2.Id, "02", "ブランド2"), Vdc = 1, Vdu = 1 };
		Db.Insert(target);
		Db.Insert(other);

		new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), brand1.Id, "01", "ブランド1改", vdate: 999);

		Assert.AreEqual("ブランド1改", Db.SingleById<MasterShohin>(target.Id).VBrand.Mei, "対象行");
		var otherAfter = Db.SingleById<MasterShohin>(other.Id);
		Assert.AreEqual("ブランド2", otherAfter.VBrand.Mei, "非対象行のVBrand");
		Assert.AreEqual(1, otherAfter.Vdu, "非対象行のVduは変わらない");
	}

	/// <summary>T2: 同じ内容で再実行しても0件(冪等)</summary>
	[TestMethod]
	public void CascadeFromMaster_IsIdempotent() {
		CreateAllTables();
		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "旧ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		Db.Insert(new MasterShohin { Code = "0001", Id_Brand = brand.Id, VBrand = Cnv(brand.Id, "01", "旧ブランド"), Vdc = 1, Vdu = 1 });

		var cascade = new MasterCascadeDb(Db);
		var first = cascade.CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "新ブランド", vdate: 999);
		var second = cascade.CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "新ブランド", vdate: 1000);

		Assert.AreEqual(1, first, "1回目");
		Assert.AreEqual(0, second, "2回目は差分なしで0件");
	}

	/// <summary>T2-2: Id_*のみセットされV*列が空の行が修復される(CSV取込・DB変換の取りこぼし対策)</summary>
	[TestMethod]
	public void CascadeFromMaster_RepairsEmptyVColumn() {
		CreateAllTables();
		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		var shohin = new MasterShohin { Code = "0001", Id_Brand = brand.Id, Vdc = 1, Vdu = 1 };
		Db.Insert(shohin);
		// V*列を空文字と空JSONにしておく(ALTER TABLE直後および旧データの状態を再現)
		Db.Execute("update MasterShohin set VBrand = '' where Id = @0", shohin.Id);

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "ブランド", vdate: 999);

		Assert.AreEqual(1, cnt, "空文字の行が修復される");
		var after = Db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("01", after.VBrand.Cd);
		Assert.AreEqual("ブランド", after.VBrand.Mei);

		Db.Execute("update MasterShohin set VBrand = '{}' where Id = @0", shohin.Id);
		var cnt2 = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "ブランド", vdate: 1000);
		Assert.AreEqual(1, cnt2, "空JSONの行が修復される");
		Assert.AreEqual("ブランド", Db.SingleById<MasterShohin>(shohin.Id).VBrand.Mei);
	}

	/// <summary>T3: MasterTokui改名が全6箇所(自己参照含む)へ伝播する</summary>
	[TestMethod]
	public void CascadeFromMaster_MasterTokui_UpdatesAllReferrers() {
		CreateAllTables();
		var tokui = new MasterTokui { Code = "0101", Name = "旧店舗", TenType = 6, Vdc = 1, Vdu = 1 };
		Db.Insert(tokui);
		var old = Cnv(tokui.Id, "0101", "旧店舗");

		Db.Insert(new MasterSysman { Name = "自社", Id_Soko = tokui.Id, VSoko = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShain { Code = "0001", Name = "社員", Id_Tenpo = tokui.Id, VTenpo = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterEndCustomer { Code = "0001", Name = "顧客", Id_Tenpo = tokui.Id, VTenpo = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShohin { Code = "0001", Name = "商品", Id_Soko = tokui.Id, VSoko = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterYosanBrand { DenDay = "20260701", Id_Tenpo = tokui.Id, VTenpo = old, Vdc = 1, Vdu = 1 });
		// 自己参照: 請求先が自分自身
		Db.Execute("update MasterTokui set Id_Paysaki = @0, VPaysaki = @1 where Id = @0", tokui.Id, "{\"Sid\":" + tokui.Id + ",\"Cd\":\"0101\",\"Mei\":\"旧店舗\"}");

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterTokui), tokui.Id, "0101", "新店舗", vdate: 999);

		Assert.AreEqual(6, cnt, "更新行数(6箇所)");
		Assert.AreEqual("新店舗", Db.First<MasterSysman>("where Id_Soko = @0", tokui.Id).VSoko.Mei, "MasterSysman.VSoko");
		Assert.AreEqual("新店舗", Db.First<MasterShain>("where Id_Tenpo = @0", tokui.Id).VTenpo.Mei, "MasterShain.VTenpo");
		Assert.AreEqual("新店舗", Db.First<MasterEndCustomer>("where Id_Tenpo = @0", tokui.Id).VTenpo.Mei, "MasterEndCustomer.VTenpo");
		Assert.AreEqual("新店舗", Db.First<MasterShohin>("where Id_Soko = @0", tokui.Id).VSoko.Mei, "MasterShohin.VSoko");
		Assert.AreEqual("新店舗", Db.First<MasterYosanBrand>("where Id_Tenpo = @0", tokui.Id).VTenpo.Mei, "MasterYosanBrand.VTenpo");
		Assert.AreEqual("新店舗", Db.SingleById<MasterTokui>(tokui.Id).VPaysaki.Mei, "MasterTokui.VPaysaki(自己参照)");
	}

	/// <summary>T3-2: MasterShain改名が SysLogin / MasterTokui / MasterShiire / MasterYosanHanbai へ伝播する</summary>
	[TestMethod]
	public void CascadeFromMaster_MasterShain_UpdatesAllReferrers() {
		CreateAllTables();
		var shain = new MasterShain { Code = "0001", Name = "旧社員", Vdc = 1, Vdu = 1 };
		Db.Insert(shain);
		var old = Cnv(shain.Id, "0001", "旧社員");

		Db.Insert(new SysLogin { LoginId = "test", Id_Shain = shain.Id, VShain = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterTokui { Code = "0001", Name = "得意先", Id_Shain = shain.Id, VShain = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShiire { Code = "0001", Name = "仕入先", Id_Shain = shain.Id, VShain = old, Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterYosanHanbai { DenDay = "20260701", Id_Shain = shain.Id, VShain = old, Vdc = 1, Vdu = 1 });

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterShain), shain.Id, "0001", "新社員", vdate: 999);

		Assert.AreEqual(4, cnt, "更新行数(4箇所)");
		Assert.AreEqual("新社員", Db.First<SysLogin>("where Id_Shain = @0", shain.Id).VShain.Mei, "SysLogin.VShain");
		Assert.AreEqual("新社員", Db.First<MasterTokui>("where Id_Shain = @0", shain.Id).VShain.Mei, "MasterTokui.VShain");
		Assert.AreEqual("新社員", Db.First<MasterShiire>("where Id_Shain = @0", shain.Id).VShain.Mei, "MasterShiire.VShain");
		Assert.AreEqual("新社員", Db.First<MasterYosanHanbai>("where Id_Shain = @0", shain.Id).VShain.Mei, "MasterYosanHanbai.VShain");
	}

	/// <summary>T3-3: MasterShiire改名は自己参照(VPaysaki)のみへ伝播し、MasterTokui.VPaysakiは対象外</summary>
	[TestMethod]
	public void CascadeFromMaster_MasterShiire_UpdatesOnlyOwnPaysaki() {
		CreateAllTables();
		var shiire = new MasterShiire { Code = "0001", Name = "旧仕入先", Vdc = 1, Vdu = 1 };
		Db.Insert(shiire);
		Db.Execute("update MasterShiire set Id_Paysaki = @0, VPaysaki = @1 where Id = @0", shiire.Id, "{\"Sid\":" + shiire.Id + ",\"Cd\":\"0001\",\"Mei\":\"旧仕入先\"}");
		// MasterTokui 側に同じIdの請求先参照があっても対象にしない(参照先型が異なる)
		Db.Insert(new MasterTokui { Code = "0001", Name = "得意先", Id_Paysaki = shiire.Id, VPaysaki = Cnv(shiire.Id, "TOK", "別マスタ"), Vdc = 1, Vdu = 1 });

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterShiire), shiire.Id, "0001", "新仕入先", vdate: 999);

		Assert.AreEqual(1, cnt, "MasterShiire.VPaysakiのみ更新される");
		Assert.AreEqual("新仕入先", Db.SingleById<MasterShiire>(shiire.Id).VPaysaki.Mei, "MasterShiire.VPaysaki");
		Assert.AreEqual("別マスタ", Db.First<MasterTokui>("where Id_Paysaki = @0", shiire.Id).VPaysaki.Mei, "MasterTokui.VPaysakiは変更されない");
	}

	/// <summary>T9: 伝播元でない型・Id<=0 は何もしない</summary>
	[TestMethod]
	public void CascadeFromMaster_IgnoresNonSourceTypeAndInvalidId() {
		CreateAllTables();
		Assert.IsFalse(MasterCascadeDb.IsCascadeSource(typeof(MasterShohin)), "MasterShohinは伝播元ではない");
		Assert.IsFalse(MasterCascadeDb.IsCascadeSource(typeof(Tran00Uriage)), "Tran系は伝播元ではない");
		var cascade = new MasterCascadeDb(Db);
		Assert.AreEqual(0, cascade.CascadeFromMaster(typeof(MasterShohin), 1, "01", "名前"), "非伝播元型");
		Assert.AreEqual(0, cascade.CascadeFromMaster(typeof(MasterMeisho), 0, "01", "名前"), "Id=0");
	}

	/// <summary>T9-2: 参照先が存在しない(dangling)行はResyncAllで変更されない</summary>
	[TestMethod]
	public void ResyncAll_LeavesDanglingReferenceUnchanged() {
		CreateAllTables();
		Db.Insert(new MasterShohin { Code = "0001", Id_Brand = 999, VBrand = Cnv(999, "99", "削除済ブランド"), Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShohin { Code = "0002", Id_Brand = 0, Vdc = 1, Vdu = 1 });

		var errors = new List<string>();
		var cnt = new MasterCascadeDb(Db).ResyncAll(errors);

		Assert.AreEqual(0, errors.Count, "SQLエラーが発生していない: " + string.Join(" / ", errors));
		Assert.AreEqual(0, cnt, "参照先なし・Id=0の行は更新しない");
		Assert.AreEqual("削除済ブランド", Db.First<MasterShohin>("where Code = '0001'").VBrand.Mei, "旧名称が温存される");
	}

	/// <summary>ResyncAll: マスタの現在値で一括再同期され、2回目は0件になる</summary>
	[TestMethod]
	public void ResyncAll_SyncsToCurrentMasterValuesAndIsIdempotent() {
		CreateAllTables();
		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "現ブランド", Vdc = 1, Vdu = 1 };
		Db.Insert(brand);
		var tokui = new MasterTokui { Code = "0101", Name = "現店舗", TenType = 6, Vdc = 1, Vdu = 1 };
		Db.Insert(tokui);
		// V*列が古い/空の状態を作る
		Db.Insert(new MasterShohin { Code = "0001", Id_Brand = brand.Id, Id_Soko = tokui.Id, VBrand = Cnv(brand.Id, "01", "旧名称"), Vdc = 1, Vdu = 1 });
		Db.Execute("update MasterShohin set VSoko = ''");
		// 自己参照(MasterTokui.VPaysaki)も再同期対象に含める
		Db.Execute("update MasterTokui set Id_Paysaki = @0, VPaysaki = '' where Id = @0", tokui.Id);

		var cascade = new MasterCascadeDb(Db);
		var errors = new List<string>();
		var first = cascade.ResyncAll(errors);
		var second = cascade.ResyncAll(errors);

		Assert.AreEqual(0, errors.Count, "SQLエラーが発生していない: " + string.Join(" / ", errors));
		// V*列の再同期は対象テーブル単位に1文へまとめているため、件数は「更新された行数」
		// (MasterShohinの1行でVBrandとVSokoの2列を同時に更新するため2ではなく1と数える)
		Assert.AreEqual(2, first, "MasterShohin1行 + MasterTokui1行 = 2行が更新される");
		Assert.AreEqual(0, second, "2回目は0件(冪等)");
		var shohin = Db.First<MasterShohin>("where Code = '0001'");
		Assert.AreEqual("現ブランド", shohin.VBrand.Mei, "MasterShohin.VBrand");
		Assert.AreEqual("現店舗", shohin.VSoko.Mei, "MasterShohin.VSoko");
		Assert.AreEqual(tokui.Id, shohin.VSoko.Sid, "MasterShohin.VSoko.Sid");
		Assert.AreEqual("現店舗", Db.SingleById<MasterTokui>(tokui.Id).VPaysaki.Mei, "MasterTokui.VPaysaki(自己参照)");
	}

	/// <summary>T4: Jsub 配列内の該当要素のみ Cd/Mei が更新され、要素数・順序・非対象要素が変わらない</summary>
	[TestMethod]
	public void CascadeFromMaster_UpdatesJsubElementKeepingOrder() {
		CreateAllTables();
		var m1 = new MasterMeisho { Kubun = "B01", KubunName = "区分1", Code = "01", Name = "旧名称1", Vdc = 1, Vdu = 1 };
		var m2 = new MasterMeisho { Kubun = "B02", KubunName = "区分2", Code = "02", Name = "名称2", Vdc = 1, Vdu = 1 };
		var m3 = new MasterMeisho { Kubun = "B03", KubunName = "区分3", Code = "03", Name = "名称3", Vdc = 1, Vdu = 1 };
		Db.Insert(m1);
		Db.Insert(m2);
		Db.Insert(m3);
		var shohin = new MasterShohin {
			Code = "0001",
			Jsub = [
				new MasterGeneralMeisho { Kb = "B01", Kbname = "区分1", Sid = m1.Id, Cd = "01", Mei = "旧名称1" },
				new MasterGeneralMeisho { Kb = "B02", Kbname = "区分2", Sid = m2.Id, Cd = "02", Mei = "名称2" },
				new MasterGeneralMeisho { Kb = "B03", Kbname = "区分3", Sid = m3.Id, Cd = "03", Mei = "名称3" },
			],
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), m1.Id, "01", "新名称1", vdate: 999);

		Assert.AreEqual(1, cnt, "更新行数");
		var after = Db.SingleById<MasterShohin>(shohin.Id);
		Assert.IsNotNull(after.Jsub);
		Assert.AreEqual(3, after.Jsub!.Count, "要素数が変わらない");
		Assert.AreEqual("B01", after.Jsub[0].Kb, "順序が保たれる(1件目)");
		Assert.AreEqual("新名称1", after.Jsub[0].Mei, "対象要素のMeiが更新される");
		Assert.AreEqual("01", after.Jsub[0].Cd, "対象要素のCd");
		Assert.AreEqual("区分1", after.Jsub[0].Kbname, "Kbnameは変わらない");
		Assert.AreEqual("B02", after.Jsub[1].Kb, "順序が保たれる(2件目)");
		Assert.AreEqual("名称2", after.Jsub[1].Mei, "非対象要素は変わらない");
		Assert.AreEqual("B03", after.Jsub[2].Kb, "順序が保たれる(3件目)");
		Assert.AreEqual("名称3", after.Jsub[2].Mei, "非対象要素は変わらない");

		// 2回目は差分なしで0件
		Assert.AreEqual(0, new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), m1.Id, "01", "新名称1", vdate: 1000), "冪等");
	}

	/// <summary>T4-2: Jsub を持つ5テーブルすべてに伝播する</summary>
	[TestMethod]
	public void CascadeFromMaster_UpdatesJsubOfAllFiveTables() {
		CreateAllTables();
		var meisho = new MasterMeisho { Kubun = "B01", Code = "01", Name = "旧", Vdc = 1, Vdu = 1 };
		Db.Insert(meisho);
		List<MasterGeneralMeisho> Sub() => [new MasterGeneralMeisho { Kb = "B01", Kbname = "区分", Sid = meisho.Id, Cd = "01", Mei = "旧" }];
		Db.Insert(new MasterShain { Code = "0001", Name = "社員", Jsub = Sub(), Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterEndCustomer { Code = "0001", Name = "顧客", Jsub = Sub(), Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShohin { Code = "0001", Name = "商品", Jsub = Sub(), Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterTokui { Code = "0001", Name = "得意先", Jsub = Sub(), Vdc = 1, Vdu = 1 });
		Db.Insert(new MasterShiire { Code = "0001", Name = "仕入先", Jsub = Sub(), Vdc = 1, Vdu = 1 });

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), meisho.Id, "01", "新", vdate: 999);

		Assert.AreEqual(5, cnt, "5テーブル分が更新される");
		Assert.AreEqual("新", Db.First<MasterShain>("where Code='0001'").Jsub![0].Mei, "MasterShain.Jsub");
		Assert.AreEqual("新", Db.First<MasterEndCustomer>("where Code='0001'").Jsub![0].Mei, "MasterEndCustomer.Jsub");
		Assert.AreEqual("新", Db.First<MasterShohin>("where Code='0001'").Jsub![0].Mei, "MasterShohin.Jsub");
		Assert.AreEqual("新", Db.First<MasterTokui>("where Code='0001'").Jsub![0].Mei, "MasterTokui.Jsub");
		Assert.AreEqual("新", Db.First<MasterShiire>("where Code='0001'").Jsub![0].Mei, "MasterShiire.Jsub");
	}

	/// <summary>T5: 色名称の変更が Jcolsiz と DerivedShohinColSiz へ伝播する</summary>
	[TestMethod]
	public void CascadeFromMaster_UpdatesJcolsizAndDerivedTable() {
		CreateAllTables();
		Db.CreateTable(typeof(DerivedShohinColSiz), true, false);
		var col = new MasterMeisho { Kubun = MasterMeisho.KubunColor, Code = "01", Name = "旧色", Vdc = 1, Vdu = 1 };
		var siz = new MasterMeisho { Kubun = MasterMeisho.KubunSize, Code = "M", Name = "旧サイズ", Vdc = 1, Vdu = 1 };
		Db.Insert(col);
		Db.Insert(siz);
		var shohin = new MasterShohin {
			Code = "0001",
			SizeKu = MasterMeisho.KubunSize,
			Jcolsiz = [
				new MasterShohinColSiz { Id_Col = col.Id, Code_Col = "01", Mei_Col = "旧色", Id_Siz = siz.Id, Code_Siz = "M", Mei_Siz = "旧サイズ", Jan1 = "J1" },
				new MasterShohinColSiz { Id_Col = col.Id, Code_Col = "01", Mei_Col = "旧色", Id_Siz = 0,      Code_Siz = "L", Mei_Siz = "L表記",    Jan1 = "J2" },
			],
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);
		Db.Execute(DerivedShohinColSiz.InsertSql, shohin.Id);
		Assert.AreEqual(2, Db.ExecuteScalar<int>("select count(*) from DerivedShohinColSiz"), "前提: Derived2行");

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), col.Id, "01", "新色", vdate: 999);

		Assert.AreEqual(1, cnt, "MasterShohin1行が更新される");
		var after = Db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("新色", after.Jcolsiz![0].Mei_Col, "Jcolsiz[0].Mei_Col");
		Assert.AreEqual("新色", after.Jcolsiz[1].Mei_Col, "Jcolsiz[1].Mei_Col");
		Assert.AreEqual("旧サイズ", after.Jcolsiz[0].Mei_Siz, "サイズ名は変わらない");
		Assert.AreEqual("L表記", after.Jcolsiz[1].Mei_Siz, "Id_Siz=0の要素は変わらない");
		Assert.AreEqual("J1", after.Jcolsiz[0].Jan1, "JANなど他項目が保たれる");
		Assert.AreEqual("J2", after.Jcolsiz[1].Jan1, "JANなど他項目が保たれる");
		// DerivedShohinColSiz が再構築されている
		Assert.AreEqual(2, Db.ExecuteScalar<int>("select count(*) from DerivedShohinColSiz"), "Derived行数が変わらない");
		Assert.AreEqual(2, Db.ExecuteScalar<int>("select count(*) from DerivedShohinColSiz where Mei_Col = '新色'"), "Derivedへ伝播");

		// サイズ名称の変更も同様
		var cnt2 = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), siz.Id, "M", "新サイズ", vdate: 1000);
		Assert.AreEqual(1, cnt2, "サイズ変更でも1行更新");
		Assert.AreEqual("新サイズ", Db.SingleById<MasterShohin>(shohin.Id).Jcolsiz![0].Mei_Siz, "Jcolsiz[0].Mei_Siz");
		Assert.AreEqual(1, Db.ExecuteScalar<int>("select count(*) from DerivedShohinColSiz where Mei_Siz = '新サイズ'"), "Derivedのサイズ名");
	}

	/// <summary>T6: 区分定義行(Kubun='IDX')の名称変更が KubunName と Jsub.Kbname へ伝播する</summary>
	[TestMethod]
	public void CascadeFromMaster_IdxRowRename_UpdatesKubunNameAndKbname() {
		CreateAllTables();
		var idx = new MasterMeisho { Kubun = MasterMeisho.KubunIndex, KubunName = "名称区分", Code = "B01", Name = "旧区分名", Vdc = 1, Vdu = 1 };
		Db.Insert(idx);
		var member = new MasterMeisho { Kubun = "B01", KubunName = "旧区分名", Code = "01", Name = "名称", Vdc = 1, Vdu = 1 };
		var other = new MasterMeisho { Kubun = "B02", KubunName = "別区分", Code = "01", Name = "名称", Vdc = 1, Vdu = 1 };
		Db.Insert(member);
		Db.Insert(other);
		Db.Insert(new MasterShohin {
			Code = "0001",
			Jsub = [
				new MasterGeneralMeisho { Kb = "B01", Kbname = "旧区分名", Sid = member.Id, Cd = "01", Mei = "名称" },
				new MasterGeneralMeisho { Kb = "B02", Kbname = "別区分", Sid = other.Id, Cd = "01", Mei = "名称" },
			],
			Vdc = 1,
			Vdu = 1,
		});

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), idx.Id, "B01", "新区分名", vdate: 999, kubun: MasterMeisho.KubunIndex, oldCode: "B01");

		Assert.IsTrue(cnt >= 2, $"MasterMeisho.KubunNameとJsub.Kbnameが更新される (実際={cnt})");
		Assert.AreEqual("新区分名", Db.SingleById<MasterMeisho>(member.Id).KubunName, "同区分のKubunName");
		Assert.AreEqual("別区分", Db.SingleById<MasterMeisho>(other.Id).KubunName, "別区分のKubunNameは変わらない");
		Assert.AreEqual("名称区分", Db.SingleById<MasterMeisho>(idx.Id).KubunName, "IDX行自身のKubunNameは変わらない");
		var shohin = Db.First<MasterShohin>("where Code='0001'");
		Assert.AreEqual("新区分名", shohin.Jsub![0].Kbname, "Jsub[0].Kbname");
		Assert.AreEqual("別区分", shohin.Jsub[1].Kbname, "Jsub[1].Kbnameは変わらない");

		// 2回目は0件(冪等)
		Assert.AreEqual(0, new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), idx.Id, "B01", "新区分名", vdate: 1000, kubun: MasterMeisho.KubunIndex, oldCode: "B01"), "冪等");
	}

	/// <summary>T6-2: 区分定義行でも Code 自体が変わった場合は区分名を伝播しない(§7-R6)</summary>
	[TestMethod]
	public void CascadeFromMaster_IdxRowCodeChanged_SkipsKubunNamePropagation() {
		CreateAllTables();
		var idx = new MasterMeisho { Kubun = MasterMeisho.KubunIndex, KubunName = "名称区分", Code = "B99", Name = "新区分名", Vdc = 1, Vdu = 1 };
		Db.Insert(idx);
		var member = new MasterMeisho { Kubun = "B01", KubunName = "旧区分名", Code = "01", Name = "名称", Vdc = 1, Vdu = 1 };
		Db.Insert(member);

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), idx.Id, "B99", "新区分名", vdate: 999, kubun: MasterMeisho.KubunIndex, oldCode: "B01");

		Assert.AreEqual(0, cnt, "区分コード変更時は伝播しない");
		Assert.AreEqual("旧区分名", Db.SingleById<MasterMeisho>(member.Id).KubunName, "KubunNameは変わらない");
	}

	/// <summary>T6-3: IDX以外の行の改名では区分名を伝播しない</summary>
	[TestMethod]
	public void CascadeFromMaster_NonIdxRow_DoesNotTouchKubunName() {
		CreateAllTables();
		var member = new MasterMeisho { Kubun = "B01", KubunName = "区分名", Code = "01", Name = "新名称", Vdc = 1, Vdu = 1 };
		Db.Insert(member);
		var sibling = new MasterMeisho { Kubun = "B01", KubunName = "区分名", Code = "02", Name = "別名称", Vdc = 1, Vdu = 1 };
		Db.Insert(sibling);

		new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), member.Id, "01", "新名称", vdate: 999, kubun: "B01", oldCode: "01");

		Assert.AreEqual("区分名", Db.SingleById<MasterMeisho>(sibling.Id).KubunName, "他行のKubunNameは変わらない");
		Assert.AreEqual(1, Db.SingleById<MasterMeisho>(sibling.Id).Vdu, "他行のVduも変わらない");
	}

	/// <summary>T8: Jsub/Jcolsiz が null / 空文字 / 不正JSON の行が混在しても例外にならない</summary>
	[TestMethod]
	public void CascadeFromMaster_ToleratesNullAndInvalidJsonArrays() {
		CreateAllTables();
		Db.CreateTable(typeof(DerivedShohinColSiz), true, false);
		var meisho = new MasterMeisho { Kubun = "B01", Code = "01", Name = "旧", Vdc = 1, Vdu = 1 };
		Db.Insert(meisho);
		// 正常な行
		Db.Insert(new MasterShohin {
			Code = "0001",
			Jsub = [new MasterGeneralMeisho { Kb = "B01", Kbname = "区分", Sid = meisho.Id, Cd = "01", Mei = "旧" }],
			Jcolsiz = [new MasterShohinColSiz { Id_Col = meisho.Id, Code_Col = "01", Mei_Col = "旧" }],
			Vdc = 1,
			Vdu = 1,
		});
		// Jsub/Jcolsiz が null の行
		Db.Insert(new MasterShohin { Code = "0002", Vdc = 1, Vdu = 1 });
		// 空文字・不正JSON・JSONだが配列でない行
		Db.Insert(new MasterShohin { Code = "0003", Vdc = 1, Vdu = 1 });
		Db.Execute("update MasterShohin set Jsub = '', Jcolsiz = 'not-json' where Code = '0003'");
		Db.Insert(new MasterShohin { Code = "0004", Vdc = 1, Vdu = 1 });
		Db.Execute("update MasterShohin set Jsub = '{}', Jcolsiz = '[]' where Code = '0004'");

		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), meisho.Id, "01", "新", vdate: 999);

		Assert.IsTrue(cnt >= 2, $"正常な行のJsubとJcolsizが更新される (実際={cnt})");
		var ok = Db.First<MasterShohin>("where Code='0001'");
		Assert.AreEqual("新", ok.Jsub![0].Mei, "正常行のJsub");
		Assert.AreEqual("新", ok.Jcolsiz![0].Mei_Col, "正常行のJcolsiz");
		// 異常データの行は変更されない
		Assert.AreEqual("", Db.ExecuteScalar<string>("select Jsub from MasterShohin where Code='0003'"), "空文字の行は変更されない");
		Assert.AreEqual("not-json", Db.ExecuteScalar<string>("select Jcolsiz from MasterShohin where Code='0003'"), "不正JSONの行は変更されない");
	}

	/// <summary>ResyncAll: JSON内スナップショット(Jsub/Kbname/KubunName/Jcolsiz)も再同期され、2回目は0件</summary>
	[TestMethod]
	public void ResyncAll_SyncsJsonSnapshotsAndIsIdempotent() {
		CreateAllTables();
		Db.CreateTable(typeof(DerivedShohinColSiz), true, false);
		var idx = new MasterMeisho { Kubun = MasterMeisho.KubunIndex, KubunName = "名称区分", Code = "B01", Name = "現区分名", Vdc = 1, Vdu = 1 };
		Db.Insert(idx);
		var member = new MasterMeisho { Kubun = "B01", KubunName = "旧区分名", Code = "01", Name = "現名称", Vdc = 1, Vdu = 1 };
		Db.Insert(member);
		var col = new MasterMeisho { Kubun = MasterMeisho.KubunColor, Code = "01", Name = "現色", Vdc = 1, Vdu = 1 };
		Db.Insert(col);
		var shohin = new MasterShohin {
			Code = "0001",
			Jsub = [new MasterGeneralMeisho { Kb = "B01", Kbname = "旧区分名", Sid = member.Id, Cd = "01", Mei = "旧名称" }],
			Jcolsiz = [new MasterShohinColSiz { Id_Col = col.Id, Code_Col = "01", Mei_Col = "旧色" }],
			Vdc = 1,
			Vdu = 1,
		};
		Db.Insert(shohin);
		Db.Execute(DerivedShohinColSiz.InsertSql, shohin.Id);

		var cascade = new MasterCascadeDb(Db);
		var errors = new List<string>();
		var first = cascade.ResyncAll(errors);
		var second = cascade.ResyncAll(errors);

		Assert.AreEqual(0, errors.Count, "SQLエラーが発生していない: " + string.Join(" / ", errors));
		Assert.IsTrue(first > 0, "初回は更新が発生する");
		Assert.AreEqual(0, second, "2回目は0件(冪等)");
		var after = Db.SingleById<MasterShohin>(shohin.Id);
		Assert.AreEqual("現名称", after.Jsub![0].Mei, "Jsub.Mei");
		Assert.AreEqual("現区分名", after.Jsub[0].Kbname, "Jsub.Kbname");
		Assert.AreEqual("現色", after.Jcolsiz![0].Mei_Col, "Jcolsiz.Mei_Col");
		Assert.AreEqual("現区分名", Db.SingleById<MasterMeisho>(member.Id).KubunName, "MasterMeisho.KubunName");
		Assert.AreEqual("名称区分", Db.SingleById<MasterMeisho>(idx.Id).KubunName, "IDX行自身のKubunNameは変わらない");
		Assert.AreEqual(1, Db.ExecuteScalar<int>("select count(*) from DerivedShohinColSiz where Mei_Col='現色'"), "Derivedも再構築される");
	}

	/// <summary>Phase3: 伝播が必要かの判定(NeedsCascade)</summary>
	[TestMethod]
	public void NeedsCascade_ReturnsTrueOnlyWhenCodeOrNameChanged() {
		var org = new MasterMeisho { Id = 1, Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "旧名称" };

		Assert.IsTrue(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho),
			new MasterMeisho { Id = 1, Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "新名称" }, org), "Name変更");
		Assert.IsTrue(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho),
			new MasterMeisho { Id = 1, Kubun = MasterMeisho.KubunBrand, Code = "02", Name = "旧名称" }, org), "Code変更");
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho),
			new MasterMeisho { Id = 1, Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "旧名称", Ryaku = "略" }, org), "Code/Name以外の変更では伝播しない");
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho),
			new MasterMeisho { Id = 1, Kubun = MasterMeisho.KubunIndex, Code = "01", Name = "旧名称" }, org), "Kubun変更のみでは伝播しない");
		// 伝播元でない型 / IBaseCodeName でない型 / null
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterShohin),
			new MasterShohin { Id = 1, Code = "01", Name = "新" }, new MasterShohin { Id = 1, Code = "01", Name = "旧" }), "伝播元でない型");
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterYosanBrand), new MasterYosanBrand(), new MasterYosanBrand()), "IBaseCodeNameでない型");
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho), null, org), "newItemがnull");
		Assert.IsFalse(MasterCascadeDb.NeedsCascade(typeof(MasterMeisho), org, null), "orgItemがnull");
	}

	/// <summary>
	/// Phase3: HandleUpdate と同じ順序(旧行取得 → Vdu設定 → Update → 伝播)を再現し、
	/// 自己参照行でも Vdu がずれないことを確認する
	/// </summary>
	[TestMethod]
	public void HandleUpdateSequence_KeepsVduConsistentForSelfReference() {
		CreateAllTables();
		var tokui = new MasterTokui { Code = "0101", Name = "旧店舗", TenType = 6, Vdc = 1, Vdu = 100 };
		Db.Insert(tokui);
		// 請求先が自分自身(実務上あり得る)
		Db.Execute("update MasterTokui set Id_Paysaki = @0, VPaysaki = @1 where Id = @0",
			tokui.Id, "{\"Sid\":" + tokui.Id + ",\"Cd\":\"0101\",\"Mei\":\"旧店舗\"}");

		// --- HandleUpdate と同じ流れ ---
		var org = Db.SingleById<MasterTokui>(tokui.Id);
		var item = Db.SingleById<MasterTokui>(tokui.Id);
		item.Name = "新店舗";
		const long vdate = 20260727123000;
		Assert.IsTrue(MasterCascadeDb.NeedsCascade(typeof(MasterTokui), item, org), "伝播が必要と判定される");
		item.Vdu = vdate;
		Db.Update(item);
		var cnt = new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterTokui), item.Id, item.Code, item.Name, vdate);
		// --- ここまで ---

		Assert.AreEqual(1, cnt, "自己参照のVPaysakiが更新される");
		var after = Db.SingleById<MasterTokui>(tokui.Id);
		Assert.AreEqual("新店舗", after.VPaysaki.Mei, "VPaysakiが現行名称になる");
		Assert.AreEqual(vdate, after.Vdu, "DB上のVduがクライアントへ返すVduと一致する(楽観排他が誤作動しない)");
	}

	/// <summary>Phase3: vdateを渡さない場合はVduが別採番され、クライアント保持値とずれることの記録</summary>
	[TestMethod]
	public void CascadeFromMaster_WithoutVdate_GeneratesOwnVdu() {
		CreateAllTables();
		var brand = new MasterMeisho { Kubun = MasterMeisho.KubunBrand, Code = "01", Name = "旧", Vdc = 1, Vdu = 100 };
		Db.Insert(brand);
		Db.Insert(new MasterShohin { Code = "0001", Id_Brand = brand.Id, VBrand = Cnv(brand.Id, "01", "旧"), Vdc = 1, Vdu = 100 });

		new MasterCascadeDb(Db).CascadeFromMaster(typeof(MasterMeisho), brand.Id, "01", "新");

		Assert.AreNotEqual(100, Db.First<MasterShohin>("where Code = '0001'").Vdu, "vdate未指定時は内部採番されVduが更新される");
	}

	/// <summary>
	/// T7: 伝播定義マップとクラス定義の整合性を検証する(定義の腐り検出)
	/// </summary>
	[TestMethod]
	public void VRules_AreConsistentWithEntityDefinitions() {
		var errors = new List<string>();
		var seen = new HashSet<string>();

		foreach (var rule in MasterCascadeDb.VRules) {
			var where = $"{rule.Target.Name}.{rule.IdColumn}";

			// 実テーブルであること(SubTableDefine/NoCreate は対象外)
			if (rule.Target.GetCustomAttribute<PrimaryKeyAttribute>() == null)
				errors.Add($"{where}: Targetに[PrimaryKey]がない(実テーブルではない)");
			if (rule.Source.GetCustomAttribute<PrimaryKeyAttribute>() == null)
				errors.Add($"{where}: Sourceに[PrimaryKey]がない(実テーブルではない)");

			// 重複定義がないこと
			if (!seen.Add(where))
				errors.Add($"{where}: 定義が重複している");

			// 命名規約 Id_X <-> VX
			if (!rule.IdColumn.StartsWith("Id_", StringComparison.Ordinal))
				errors.Add($"{where}: IdColumnが Id_ で始まっていない");
			else if (rule.VColumn != "V" + rule.IdColumn[3..])
				errors.Add($"{where}: VColumn({rule.VColumn})が命名規約 V{rule.IdColumn[3..]} と一致しない");

			// プロパティが実在し、型が正しいこと
			var idProp = rule.Target.GetProperty(rule.IdColumn);
			if (idProp == null)
				errors.Add($"{where}: IdColumnのプロパティが存在しない");
			else if (idProp.PropertyType != typeof(long))
				errors.Add($"{where}: IdColumnがlongではない({idProp.PropertyType.Name})");

			var vProp = rule.Target.GetProperty(rule.VColumn);
			if (vProp == null)
				errors.Add($"{where}: VColumn({rule.VColumn})のプロパティが存在しない");
			else {
				if (vProp.PropertyType != typeof(CodeNameView))
					errors.Add($"{where}: VColumnがCodeNameViewではない({vProp.PropertyType.Name})");
				// 物理列であること(Master系は物理保持が方針。ComputedColumnだとDB列が無くSQLが失敗する)
				if (vProp.GetCustomAttribute<ComputedColumnAttribute>() != null)
					errors.Add($"{where}: VColumnに[ComputedColumn]が付いている(物理列でなければ伝播できない)");
				if (vProp.GetCustomAttribute<NPoco.IgnoreAttribute>() != null)
					errors.Add($"{where}: VColumnに[Ignore]が付いている");
				if (vProp.GetCustomAttribute<ResultColumnAttribute>() != null)
					errors.Add($"{where}: VColumnに[ResultColumn]が付いている");
				if (vProp.GetCustomAttribute<SerializedColumnAttribute>() == null)
					errors.Add($"{where}: VColumnに[SerializedColumn]が付いていない");
			}

			// 参照先が Code/Name を持ち、伝播元として登録されていること
			if (!typeof(IBaseCodeName).IsAssignableFrom(rule.Source))
				errors.Add($"{where}: SourceがIBaseCodeNameを実装していない");
			if (!MasterCascadeDb.IsCascadeSource(rule.Source))
				errors.Add($"{where}: Source({rule.Source.Name})が伝播元として登録されていない");
		}

		Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
	}

	/// <summary>
	/// T7-2: CvBase内のMaster系V*列がすべて伝播定義に登録されていること(定義漏れ検出)
	/// Tran系(Tran*)は伝票の時点名称を保持する方針のため対象外。
	/// </summary>
	[TestMethod]
	public void VRules_CoverAllMasterVColumns() {
		var missing = new List<string>();
		var registered = MasterCascadeDb.VRules
			.Select(r => $"{r.Target.Name}.{r.VColumn}")
			.ToHashSet();
		// 実テーブル(=[PrimaryKey]を持つ)かつTran系でない型を対象にする
		var targets = typeof(MasterShain).Assembly.GetTypes()
			.Where(t => t.IsClass && !t.IsAbstract)
			.Where(t => t.GetCustomAttribute<PrimaryKeyAttribute>() != null)
			.Where(t => !t.Name.StartsWith("Tran", StringComparison.Ordinal))
			.Where(t => !t.Name.StartsWith("Summary", StringComparison.Ordinal));

		foreach (var t in targets) {
			foreach (var p in t.GetProperties()) {
				if (p.PropertyType != typeof(CodeNameView))
					continue;
				if (p.GetCustomAttribute<NPoco.IgnoreAttribute>() != null || p.GetCustomAttribute<ResultColumnAttribute>() != null)
					continue;
				if (!registered.Contains($"{t.Name}.{p.Name}"))
					missing.Add($"{t.Name}.{p.Name} が MasterCascadeDb.VRules に未登録");
			}
		}

		Assert.AreEqual(0, missing.Count, string.Join(Environment.NewLine, missing));
	}
}
