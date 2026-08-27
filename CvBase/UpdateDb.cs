using Microsoft.Extensions.Logging;
using NPoco;
using static System.Net.Mime.MediaTypeNames;

namespace CvBase;

/// <summary>
/// DBバージョン情報クラス (SysUpdateDb を使用)
/// </summary>
public record InnerVersion(int DbVersion, string Sql, string Memo);

/// <summary>
/// SqlDepends: DBのテーブル変更を管理する versions配列を定義し、実稼働DBとプログラムの整合性をとる
/// </summary>
public class UpdateDb {
	private static InnerVersion[] versions = [ // バージョン番号8桁=年月日+連番
		new (26_04_01_01,"ALTER TABLE TranVulcanHht ADD COLUMN ErrorMsg TEXT;","SysUpdateDbテーブル 2026.04.08定義"),
		new (26_06_10_01,"ALTER TABLE MasterSysman ADD COLUMN TaxRegistrationNumber TEXT;","MasterSysman 列追加 2026.06.10定義"),
		// new (26_06_10_02,"ALTER TABLE SysUpdateDb RENAME COLUMN NewVersion To PreVersion;","SysUpdateDb 列名変更 2026.06.10定義"), SysUpdateDb のみ直接変更する
		new (26_06_10_02,"ALTER TABLE MasterTokui ADD COLUMN TaxRegistrationNumber TEXT;ALTER TABLE MasterShiire ADD COLUMN TaxRegistrationNumber TEXT;","MasterTokui MasterShiire 列追加 2026.06.10定義"),
		new (26_06_11_01,"ALTER TABLE DerivedShohinColSiz ADD COLUMN Vdc NUMBER not null default 0;ALTER TABLE DerivedShohinColSiz ADD COLUMN Vdu NUMBER not null default 0;update DerivedShohinColSiz set (vdc,vdu)=(select s.vdc,s.vdu from MasterShohin s where s.Id=Id_Shohin);","DerivedShohinColSiz 列追加 2026.06.11定義"),
		new (26_06_18_01,"ALTER TABLE TranHhtData ADD COLUMN Jan1 TEXT NOT NULL DEFAULT '';ALTER TABLE TranHhtData ADD COLUMN Jan2 TEXT NOT NULL DEFAULT '';ALTER TABLE TranHhtData DROP COLUMN TanaNo;ALTER TABLE TranHhtData ADD COLUMN TanaNo NUMBER not null default 0;","2026.06.18定義"),
		new (26_06_23_01,"ALTER TABLE MasterShain ADD COLUMN ExpireDate TEXT NOT NULL DEFAULT '';","2026.06.23定義"),
		new (26_07_06_01,"ALTER TABLE Tran00Uriage ADD COLUMN Tax NUMBER not null default 0;ALTER TABLE Tran00Uriage ADD COLUMN Total NUMBER not null default 0;ALTER TABLE Tran01Tenuri ADD COLUMN Tax NUMBER not null default 0;ALTER TABLE Tran01Tenuri ADD COLUMN Total NUMBER not null default 0;ALTER TABLE Tran03Shiire ADD COLUMN Tax NUMBER not null default 0;ALTER TABLE Tran03Shiire ADD COLUMN Total NUMBER not null default 0;ALTER TABLE Tran12Jyuchu ADD COLUMN Tax NUMBER not null default 0;ALTER TABLE Tran12Jyuchu ADD COLUMN Total NUMBER not null default 0;ALTER TABLE Tran13Hachu ADD COLUMN Tax NUMBER not null default 0;ALTER TABLE Tran13Hachu ADD COLUMN Total NUMBER not null default 0;","2026.07.06定義"),
		new (26_07_10_01,"ALTER TABLE MasterEndCustomer RENAME COLUMN Gendar to Gender;","2026.07.10定義 綴り間違いを訂正"),
		new (26_07_25_01,"ALTER TABLE MasterSysman ADD COLUMN Id_Soko NUMBER not null default 0;ALTER TABLE Tran03Shiire ADD COLUMN IsPrint NUMBER not null default 0;","2026.07.25定義"),
		new (26_07_27_01,"ALTER TABLE MasterSysman ADD COLUMN VSoko  TEXT NOT NULL DEFAULT '';ALTER TABLE MasterYosanBrand ADD COLUMN VTenpo TEXT NOT NULL DEFAULT '';ALTER TABLE MasterYosanBrand ADD COLUMN VBrand TEXT NOT NULL DEFAULT '';ALTER TABLE MasterYosanHanbai ADD COLUMN VShain TEXT NOT NULL DEFAULT '';","2026.07.25定義 V*系処理の統一のため"),
		new (26_07_27_02,"Update MasterSysman set VSoko='{}' where Id=1;Update MasterYosanBrand set VTenpo='{}',VBrand='{}';Update MasterYosanHanbai set VShain='{}';","  V*項目追加時の空データ処理"),
		new (26_07_27_03,"update MasterSysman set VSoko=ifnull((select json_object('Sid',ifnull(T.Id,0),'Cd',ifnull(T.Code,''),'Mei',ifnull(T.Name,'')) from MasterTokui T where T.Id=MasterSysman.Id_Soko),json_object('Sid',0,'Cd','','Mei',''));update MasterYosanBrand set VTenpo=ifnull((select json_object('Sid',ifnull(T.Id,0),'Cd',ifnull(T.Code,''),'Mei',ifnull(T.Name,'')) from MasterTokui T where T.Id=MasterYosanBrand.Id_Tenpo),json_object('Sid',0,'Cd','','Mei','')),VBrand=ifnull((select json_object('Sid',ifnull(M.Id,0),'Cd',ifnull(M.Code,''),'Mei',ifnull(M.Name,'')) from MasterMeisho M where M.Id=MasterYosanBrand.Id_Brand),json_object('Sid',0,'Cd','','Mei',''))","2026.07.27定義 Master系V*列の物理化 MasterSysman.VSoko/MasterYosanBrand.VTenpo,VBrand"),
		new (26_07_27_04,"update MasterYosanHanbai set VShain=ifnull((select json_object('Sid',ifnull(T.Id,0),'Cd',ifnull(T.Code,''),'Mei',ifnull(T.Name,'')) from MasterShain T where T.Id=MasterYosanHanbai.Id_Shain),json_object('Sid',0,'Cd','','Mei',''))","2026.07.27定義 Master系V*列の物理化 MasterSysman.VSoko/MasterYosanBrand.VTenpo,VBrand"),
		new (26_07_31_01,"ALTER TABLE Tran01Tenuri ADD COLUMN PosClientSaleId TEXT NOT NULL DEFAULT '';ALTER TABLE Tran01Tenuri ADD COLUMN JposPayment TEXT NOT NULL DEFAULT '{}';","POS売上の端末取引ID・決済内訳列追加"),
		new (26_07_31_02,"ALTER TABLE Tran00Uriage ADD COLUMN IsPrint NUMBER not null default 0;","納品書発行済フラグ追加 Tran03Shiire.IsPrint と同形"),
		new (26_08_04_01,"UPDATE Tran01Tenuri SET JposPayment = '{}' WHERE JposPayment = '';","店舗売上の明細照会時エラーの解消"),
		new (26_08_14_01,"ALTER TABLE Tran00Uriage ADD COLUMN EndFlag NUMBER not null default 0;ALTER TABLE Tran03Shiire ADD COLUMN EndFlag NUMBER not null default 0;","消込済フラグ追加 既存伝票は全て未消込(0)"),
		new (26_08_15_01,"ALTER TABLE SummaryRealStock ADD COLUMN ReserveQty NUMBER not null default 0;ALTER TABLE SummaryStock ADD COLUMN ReserveQty NUMBER not null default 0;ALTER TABLE TranHaibun ADD COLUMN EndFlag NUMBER not null default 0;","引当数・配分入庫済フラグ追加 TranHaibunが0件のため移行時の引当数は全て0"),
		new (26_08_16_01,"ALTER TABLE Tran06Nyukin RENAME COLUMN DenDay TO KakeDay;ALTER TABLE Tran07Shiharai RENAME COLUMN DenDay TO KakeDay;","入金・支払の日付列を掛計上日へ改名 値はそのまま引き継ぐ インデックスnk1はSQLiteが自動追随する"),
		new (26_08_17_01,"ALTER TABLE Tran13Hachu ADD COLUMN EndFlag NUMBER not null default 0;ALTER TABLE Tran12Jyuchu ADD COLUMN EndFlag NUMBER not null default 0;ALTER TABLE TranHaibun ADD COLUMN ShortSu NUMBER not null default 0;","発注・受注の完了フラグと配分の欠品数を追加 既存伝票は未完了(0)・欠品0"),
		new (26_08_17_02,"ALTER TABLE SummaryStock ADD COLUMN BookQty NUMBER not null default 0;","棚卸開始処理が保存する帳簿在庫スナップショット列を追加 Tran61Choseiは未作成テーブルとしてDefineDataTableが作る"),
		new (26_08_18_01,"ALTER TABLE Tran13Hachu ADD COLUMN NouhinDay TEXT NOT NULL DEFAULT '';ALTER TABLE Tran12Jyuchu ADD COLUMN NouhinDay TEXT NOT NULL DEFAULT '';","発注・受注へ納品予定日を追加 既存伝票は未設定('')"),
		new (26_08_18_02,"ALTER TABLE SummaryUriSei ADD COLUMN SeikyuNo TEXT NOT NULL DEFAULT '';ALTER TABLE SummaryUriSei ADD COLUMN Renban NUMBER NOT NULL DEFAULT 0;ALTER TABLE SummaryUriSei ADD COLUMN NyukinYoteiDay TEXT NOT NULL DEFAULT '';ALTER TABLE SummaryKaiShi ADD COLUMN ShiharaiYoteiDay TEXT NOT NULL DEFAULT '';","請求残へ請求書番号・連番・入金予定日、支払残へ支払予定日を追加 既存データは未設定"),
		new (26_08_19_01,"INSERT INTO MasterMeisho (Vdc,Vdu,Kubun,KubunName,Code,Name,Ryaku,Kana,Odr) VALUES (strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'IDX','名称区分','CHR','調整理由','','',0),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','10','入庫','','',10),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','20','紛失','','',20),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','21','盗難','','',21),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','22','破損','','',22),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','23','検品ミス','','',23),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'CHR','調整理由','29','その他','','',29);","調整理由区分(CHR)の名称マスタを追加 10入庫/20紛失/21盗難/22破損/23検品ミス/29その他 コード帯で在庫増減方向が決まる"),
		new (26_08_20_01,"ALTER TABLE SummaryUriSei ADD COLUMN Sonota NUMBER NOT NULL DEFAULT 0;","請求残へその他売上(区分99)列を追加 既存データは0 売上本体(Tran00Uriage)の畳み込みは変更せず請求一覧の算式でのみ切り出す(E11)"),
		new (26_08_20_02,"ALTER TABLE MasterTokui ADD COLUMN LeadTimeDays NUMBER not null default 0;ALTER TABLE MasterShiire ADD COLUMN LeadTimeDays NUMBER not null default 0;","MasterTokui MasterShiire へ納品リードタイム日数列を追加 既存データは0"),
		new (26_08_27_01,"INSERT INTO MasterMeisho (Vdc,Vdu,Kubun,KubunName,Code,Name,Ryaku,Kana,Odr) VALUES (strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'IDX','名称区分','KIJ','資材区分','Material','',0),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','01','布帛','Woven','',1),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','02','ニット','Knit','',2),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','03','裏地','Lining','',3),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','04','芯地','Interlining','',4),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','05','中綿','Padding','',5),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','06','ボタン','Button','',6),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','07','ファスナー','Zipper','',7),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','08','ホック・スナップ','Snap','',8),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','09','バックル・金具','Hardware','',9),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','10','ハトメ・リベット','Eyelet','',10),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','11','ゴム','Elastic','',11),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','12','テープ','Tape','',12),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','13','紐・コード','Cord','',13),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','14','レース','Lace','',14),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','15','リブ','Rib','',15),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','16','縫製糸','Thread','',16),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','17','装飾品','Decor','',17),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','18','ネーム・ラベル','Label','',18),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','19','下げ札','Tag','',19),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','20','包装資材','Packing','',20),(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'KIJ','資材区分','99','その他','Other','',99);","資材区分(KIJ)の名称マスタを追加 IDXにKIJ自体の行も追加 布帛/ニット/裏地等20区分+その他"),
		new (26_08_27_02,"ALTER TABLE MasterMaterial ADD COLUMN Id_Tax NUMBER not null default 1;","MasterMaterial 消費税区分列を追加 既定はMasterSysTax Id=1"),
	];

	public static async Task WriteVersionInfoAsync(IDatabase db, CancellationToken ct = default) {
		await WriteVersionInfoAsync(db, versions, ct);
	}
	/// <summary>
	/// バージョン情報を書き込む＆バージョンアップされた場合にテーブルの整合性を保つ
	/// </summary>
	public static async Task WriteVersionInfoAsync(IDatabase db, InnerVersion[] verupSql, CancellationToken ct = default) {
		if (verupSql.Length == 0) return;

		var latestDb = await db.FirstOrDefaultAsync<SysUpdateDb>("order by DbVersion desc", ct); // DB上の最新バージョン情報を取得
		var logger = new NLogExtender<UpdateDb>();
		// vreupSqlがあり、DBにバージョンレコードがない場合は、プログラム最新かつDBも新規の場合なので、verupSqlの最新バージョンをDBに書き込む
		var latestVersion = verupSql[^1]; // verupSqlの最新バージョンは、DBの最新バージョンとする
		if (latestDb == null) {
			var verNow = new SysUpdateDb {
				DbVersion = latestVersion.DbVersion,
				DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
				Sql = "",
				PreVersion = latestVersion.DbVersion,
				Memo = "新規レコード作成"
			};
			await db.InsertAsync(verNow, ct);
			logger.LogDebug($"UpdateDb: DBバージョン新規書込({latestVersion.DbVersion})");
			return;
		}
		if (latestDb.DbVersion >= latestVersion.DbVersion) { // DBに最新までレコードがある
			logger.LogInformation($"UpdateDb: DBバージョンは最新({latestVersion.DbVersion})");
			return;
		}
		foreach (var record in verupSql) { // 配列はforeachで必ず順番に処理される
			ct.ThrowIfCancellationRequested();
			if (record.DbVersion > latestDb.DbVersion) { // verupSqlのバージョンがDBのバージョンより新しい場合は、DBをverupSqlのバージョンに合わせるためのSQLを実行する
				var errorMsg = await SubInsertRecordAsync(db, record, latestDb.DbVersion, logger, ct);
				if (!string.IsNullOrEmpty(errorMsg)) { // 失敗したスキーマ変更は再度実行しても失敗するので、エラーをログに出力して処理を続行する
					logger.LogError($"UpdateDb: DBバージョンアップ時エラー rec={record.DbVersion}: {errorMsg} : SQL={record.Sql}");
				}
			}
		}
		logger.LogDebug($"DBバージョンアップ({latestDb.DbVersion} -> {latestVersion.DbVersion})");
	}

	/// <summary>
	/// 個別のバージョンアップレコードの処理
	/// </summary>
	static async Task<string> SubInsertRecordAsync(IDatabase db, InnerVersion verInfo, int orgVersion, NLogExtender<UpdateDb> logger, CancellationToken ct) {
		string? errorMsg = null;
		var sqls = verInfo.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (var oneSql in sqls) {
			if (string.IsNullOrWhiteSpace(oneSql)) continue;
			try {
				await db.ExecuteAsync(oneSql, ct);
			}
			catch (Exception ex) {
				errorMsg += $"{ex.Message};";
			}
		}
		var item = new SysUpdateDb {
			DbVersion = verInfo.DbVersion,
			DateStart = DateTime.Now.ToString("yyyyMMddHHmmss"),
			Sql = verInfo.Sql,
			Memo = errorMsg ?? verInfo.Memo,
			PreVersion = orgVersion,
		};
		await db.InsertAsync(item, ct);
		return errorMsg ?? "";
	}
}
