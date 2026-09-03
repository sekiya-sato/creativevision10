using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CvDomainLogic;

/// <summary>
/// 変換ステップの進捗情報
/// [Conversion step progress information]
/// </summary>
public record StreamStepProgress(string StepName, int Count, int Progress, bool IsCompleted = false, bool IsError = false, string? ErrorMessage = null);

/// <summary>
/// SqlDepends: データベースを変換するクラス
/// </summary>
public partial class ConvertDb {
	ExDatabase _fromDb;
	ExDatabase _toDb;
	ILogger<ConvertDb> _logger;

	public ConvertDb(ExDatabase fromDb, ExDatabase toDb) {
		_fromDb = fromDb;
		_toDb = toDb;
		_logger = new NLogExtender<ConvertDb>();
	}

	/// <summary>
	/// 変換タスクの定義（順序を維持）
	/// [Conversion task definitions in execution order]
	/// </summary>
	private static readonly (string Name, Func<ConvertDb, bool, int> Action)[] _stepDefinitions = [
		(nameof(CnvMasterConfig), static (db, isInit) => db.CnvMasterConfig(isInit)),
		(nameof(CnvMasterSys), static (db, isInit) => db.CnvMasterSys(isInit)),
		(nameof(CnvMasterMeisho), static (db, isInit) => db.CnvMasterMeisho(isInit)),
		(nameof(CnvMasterShain), static (db, isInit) => db.CnvMasterShain(isInit)),
		(nameof(CnvMasterEndCustomer), static (db, isInit) => db.CnvMasterEndCustomer(isInit)),
		(nameof(CnvMasterShohin), static (db, isInit) => db.CnvMasterShohin(isInit)),
		(nameof(CnvMasterTokui), static (db, isInit) => db.CnvMasterTokui(isInit)),
		(nameof(CnvMasterShiire), static (db, isInit) => db.CnvMasterShiire(isInit)),
		(nameof(CnvMasterMaterial), static (db, isInit) => db.CnvMasterMaterial(isInit)),
		(nameof(CnvMasterYosanBrand), static (db, isInit) => db.CnvMasterYosanBrand(isInit)),
		(nameof(CnvMasterYosanHanbai), static (db, isInit) => db.CnvMasterYosanHanbai(isInit)),
		(nameof(CnvAfterMaster), static (db, isInit) => db.CnvAfterMaster(isInit)),
		(nameof(CnvAfterMasterAddress), static (db, isInit) => db.CnvAfterMasterAddress(isInit)),
		// マスタの後付け項目取り込み。Tran変換が TaxRounding をスナップショットするので必ずTranより前に置く
		(nameof(CnvMasterAfter2), static (db, isInit) => db.CnvMasterAfter2(isInit)),
		(nameof(CnvTran00HonUri), static (db, isInit) => db.CnvTran00HonUri(isInit)),
		(nameof(CnvTran01TenUri), static (db, isInit) => db.CnvTran01TenUri(isInit)),
		(nameof(CnvTran02Material), static (db, isInit) => db.CnvTran02Material(isInit)),
		(nameof(CnvTran03Shiire), static (db, isInit) => db.CnvTran03Shiire(isInit)),
		(nameof(CnvTran05Ido), static (db, isInit) => db.CnvTran05Ido(isInit)),
		(nameof(CnvTran06Nyukin), static (db, isInit) => db.CnvTran06Nyukin(isInit)),
		(nameof(CnvTran07Shiharai), static (db, isInit) => db.CnvTran07Shiharai(isInit)),
		(nameof(CnvTran60Tana), static (db, isInit) => db.CnvTran60Tana(isInit)),
		(nameof(CnvTran61Chosei), static (db, isInit) => db.CnvTran61Chosei(isInit)),
		(nameof(CnvTran10Ido), static (db, isInit) => db.CnvTran10Ido(isInit)),
		(nameof(CnvTran11IdoIn), static (db, isInit) => db.CnvTran11IdoIn(isInit)),
		(nameof(CnvTran12Jyuchu), static (db, isInit) => db.CnvTran12Jyuchu(isInit)),
		(nameof(CnvTran13Hachu), static (db, isInit) => db.CnvTran13Hachu(isInit)),
		// 関連伝票の張替は全Tran変換の後に実行する必要があるため必ず最後に置く
		(nameof(CnvTranRelateFix), static (db, isInit) => db.CnvTranRelateFix(isInit)),
	];

	/// <summary>
	/// 全ての変換タスク名を取得
	/// </summary>
	/// <returns></returns>
	public List<string> GetAllTaskNames() => _stepDefinitions.Select(s => s.Name).ToList();

	/// <summary>
	/// 変換タスク名から実行用ステップを生成する
	/// [Build executable steps from task names]
	/// </summary>
	private (string Name, Func<bool, int> Action)[] BuildSteps(IEnumerable<string> selectedTask) {
		var selectedSet = new HashSet<string>(selectedTask);
		return _stepDefinitions
			.Where(s => selectedSet.Contains(s.Name))
			.Select(s => (s.Name, (Func<bool, int>)(isInit => s.Action(this, isInit))))
			.ToArray();
	}

	/// <summary>
	/// ストリーミングで全マスタおよびトランザクション変換を実行
	/// [Execute all master conversion for streaming]
	/// </summary>
	public IAsyncEnumerable<StreamStepProgress> ConvertAllAsyncStream(bool isInit = true) {
		var steps = _stepDefinitions
			.Select(s => (s.Name, (Func<bool, int>)(isInit => s.Action(this, isInit))))
			.ToArray();

		return StreamStepProgressRunner.Run(
			steps,
			isInit,
			_logger,
			"変換処理開始",
			"変換処理エラー: {StepName}",
			"変換処理終了");
	}

	/// <summary>
	/// ストリーミングで指定されたタスクのみを順番通りに変換実行
	/// [Execute selected conversion tasks in defined order for streaming]
	/// </summary>
	/// <param name="selectedTask">実行するタスク名のリスト</param>
	/// <param name="isInit">初期化フラグ</param>
	public IAsyncEnumerable<StreamStepProgress> ConvertSelectAsyncStream(List<string> selectedTask, bool isInit = true) {
		var steps = BuildSteps(selectedTask);

		return StreamStepProgressRunner.Run(
			steps,
			isInit,
			_logger,
			"選択変換処理開始",
			"選択変換処理エラー: {StepName}",
			"選択変換処理終了");
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
		var ret = getString(rec, key);
		if(string.IsNullOrEmpty(ret))
			return defaultValue;
		return ret;
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
	private long getDataLong(Dictionary<string, object> rec, string key) {
		var data = getString(rec, key);
		if (data == null)
			return 0;
		if (long.TryParse(data, out long val))
			return val;
		if (decimal.TryParse(data, out var dec))
			return (long)decimal.Truncate(dec);
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

		// isInit=false（テーブルを作り直さない追記実行）では、既にあるCodeを二重登録しない。
		// これにより同じ変換を何度実行しても結果が変わらず、旧DB側で増えた分だけが
		// 末尾のIdで追加される。既存Idは動かないので伝票やSummaryのId参照は壊れない。
		list = ExcludeExistingCodes(list, isInit);
		if (list.Count == 0)
			return 0;

		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		_toDb.InsertBulk<T>(list);
		_toDb.CompleteTransaction();

		return list.Count;
	}
	/// <summary>
	/// マスタの後付け項目を旧DBから取り込む後処理（Idは振り直さない）。
	/// <para>
	/// マスタ変換は Id を AUTOINCREMENT に任せているため、全体を再変換すると旧DB側の
	/// 件数増減で Id が別値に振り直され、SummaryStock(371万件)・SummaryRealStock(153万件)や
	/// 再計算手段の無い TranJodai / MasterSysman.Id_Soko まで巻き添えで壊れる。
	/// そこで「列の追加や変換漏れで未移行のまま残っている項目」だけを、Code をキーに
	/// 既存行へ UPDATE する。Id は一切動かさない。
	/// </para>
	/// <para>
	/// 何度実行しても同じ結果になる（旧DBの値をそのまま上書きするだけで、
	/// 前回実行の結果に依存しない）。旧DBに無い Code の行は触らない（アプリで作られた
	/// マスタや旧DB側で削除されたものを消さないため）。
	/// </para>
	/// <para>
	/// 旧DBに有って cv10 に無い「取りこぼし」行の追加は、本メソッドではなく各
	/// <c>CnvMaster*</c> を <c>isInit=false</c> で実行することで行う（既存Codeを
	/// スキップして末尾のIdで追記するため、既存Idは動かない）。マッピングを二重に
	/// 持たないためにこの分担にしている。
	/// </para>
	/// </summary>
	public int CnvMasterAfter2(bool isInit = true) {
		var cnt = 0;
		cnt += backfillMasterTokui();
		cnt += backfillMasterShiire();
		cnt += backfillMasterEndCustomerRank();
		cnt += resolveShiirePaysaki();
		return cnt;
	}

	/// <summary>
	/// 得意先の後付け項目。<c>TaxRounding</c>(消費税端数)と <c>SlipFormType</c>(伝票発行区分)は
	/// 列が追加される前に変換されたcv10データでは全件0のまま残っている。
	/// </summary>
	int backfillMasterTokui() =>
		backfillByCode<MasterTokui>(
			"select 得意先CD as Code, 消費税端数 as V1, 伝票発行区分 as V2 from HC$MASTER_TOKUI where 得意先CD>'.'",
			["TaxRounding", "SlipFormType"],
			rec => [NormalizeTaxRounding(getDataInt(rec, "V1")), getDataInt(rec, "V2")]);

	/// <summary>
	/// 仕入先の後付け項目。<c>TaxRounding</c> のほか、<c>PayMonth</c>/<c>PayDay</c> は
	/// 得意先ブロックのコピーで存在しない列(入金予定月/日)を読んでいたため全件0だった。
	/// </summary>
	int backfillMasterShiire() =>
		backfillByCode<MasterShiire>(
			"select 仕入先CD as Code, 消費税端数 as V1, 支払予定月 as V2, 支払予定日 as V3 from HC$MASTER_SIIRE where 仕入先CD>'.'",
			["TaxRounding", "PayMonth", "PayDay"],
			rec => [NormalizeTaxRounding(getDataInt(rec, "V1")), getDataInt(rec, "V2"), getDataInt(rec, "V3")]);

	/// <summary>
	/// 顧客ランク。変換コードに代入自体が無く、cv10 は全件空文字だった。
	/// 件数が150万件超あるため一時テーブル経由の一括UPDATEで処理する。
	/// </summary>
	int backfillMasterEndCustomerRank() =>
		backfillByCode<MasterEndCustomer>(
			"select 顧客CD as Code, 顧客ランク as V1 from HC$MASTER_KOKYAKU where 顧客CD>'.'",
			["Rank"],
			rec => [getString(rec, "V1")]);

	/// <summary>
	/// 仕入先の支払先(<c>Id_Paysaki</c>/<c>VPaysaki</c>)を解決する。
	/// <para>
	/// 支払先は MasterShiire 自身への自己参照のため、変換中は参照先がまだ挿入されて
	/// いないことがある。全件そろった後でなければ引けないのでここで解決する。
	/// </para>
	/// </summary>
	int resolveShiirePaysaki() {
		var rows = _fromDb.Fetch<Dictionary<string, object>>(
			"select 仕入先CD as Code, 支払先CD as Paysaki from HC$MASTER_SIIRE where 仕入先CD>'.'");
		if (rows.Count == 0)
			return 0;

		var byCode = _toDb.Fetch<MasterShiire>("").ToDictionary(x => x.Code, StringComparer.Ordinal);
		var cnt = 0;
		foreach (var rec in rows) {
			var code = getString(rec, "Code");
			var paysakiCode = getString(rec, "Paysaki");
			if (!byCode.TryGetValue(code, out var target))
				continue; // cv10 に無い仕入先は触らない
			// 支払先が空、または cv10 に存在しないコードなら未設定(0)のままにする
			byCode.TryGetValue(paysakiCode, out var paysaki);
			var newId = paysaki?.Id ?? 0;
			var newView = new CodeNameView(newId, paysaki?.Code ?? string.Empty, paysaki?.Name ?? string.Empty);
			if (target.Id_Paysaki == newId && target.VPaysaki?.Sid == newId)
				continue; // 既に解決済み（再実行時はここで抜ける）
			target.Id_Paysaki = newId;
			target.VPaysaki = newView;
			_toDb.Update(target);
			cnt++;
		}
		_logger.LogInformation("MasterShiire.Id_Paysaki を解決: {Count}件", cnt);
		return cnt;
	}

	/// <summary>
	/// 旧DBの (Code, 値…) を一時テーブルへ bulk 投入し、Code の join で1回のUPDATEにまとめる。
	/// <para>
	/// 1件ずつ Update すると顧客150万件規模で数時間かかるため（既存の
	/// <c>subCnvTranHeaderSize</c> と同じ理由）、一時テーブル + 一括UPDATEにしている。
	/// 対象列の値が既に同じ行も書き換えるが、書く値は旧DBの値そのものなので
	/// 何度実行しても結果は変わらない。
	/// </para>
	/// </summary>
	int backfillByCode<T>(string oldSql, string[] columns, Func<Dictionary<string, object>, object[]> values) {
		var rows = _fromDb.Fetch<Dictionary<string, object>>(oldSql);
		if (rows.Count == 0)
			return 0;

		var table = _toDb.GetTableName(typeof(T));
		var tmp = $"_bf_{table}";
		var cols = string.Join(",", columns.Select((c, i) => $"C{i}"));
		_toDb.ExecuteDialect($"drop table if exists {tmp}");
		_toDb.ExecuteDialect($"create table {tmp} (Code TEXT not null primary key, {string.Join(",", columns.Select((c, i) => $"C{i}"))})");

		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		foreach (var rec in rows) {
			var code = getString(rec, "Code");
			if (string.IsNullOrEmpty(code))
				continue;
			var vals = values(rec);
			var ph = string.Join(",", Enumerable.Range(1, vals.Length).Select(i => $"@{i}"));
			_toDb.ExecuteDialect(
				$"insert or replace into {tmp} (Code,{cols}) values (@0,{ph})",
				[code, .. vals]);
		}
		_toDb.CompleteTransaction();

		var setClause = string.Join(", ", columns.Select((c, i) =>
			$"{c} = (select t.C{i} from {tmp} t where t.Code = {table}.Code)"));
		_toDb.ExecuteDialect($@"
UPDATE {table}
SET {setClause}
WHERE EXISTS (select 1 from {tmp} t where t.Code = {table}.Code)");
		var cnt = _toDb.FirstOrDefault<int>("SELECT changes() AS updated_count");
		_toDb.ExecuteDialect($"drop table if exists {tmp}");

		_logger.LogInformation("{Table} の後付け項目({Columns})を {Count}件へ反映",
			table, string.Join("/", columns), cnt);
		return cnt;
	}

	/// <summary>
	/// 追記実行(<paramref name="isInit"/>=false)のとき、cv10 に既に存在する <c>Code</c> の行を除く。
	/// <para>
	/// <paramref name="isInit"/>=true はテーブルを drop して作り直すため除外は不要（全件が新規）。
	/// <c>Code</c> を持たない型は判定できないためそのまま返す。
	/// </para>
	/// </summary>
	private List<T> ExcludeExistingCodes<T>(List<T> list, bool isInit) {
		if (isInit || list.Count == 0)
			return list;
		var codeProp = typeof(T).GetProperty("Code");
		if (codeProp == null || codeProp.PropertyType != typeof(string))
			return list;

		var existing = new HashSet<string>(
			_toDb.Fetch<string>($"select Code from {_toDb.GetTableName(typeof(T))}"),
			StringComparer.Ordinal);
		if (existing.Count == 0)
			return list;

		var added = list.Where(x => !existing.Contains(codeProp.GetValue(x) as string ?? string.Empty)).ToList();
		if (added.Count != list.Count) {
			_logger.LogInformation("{Table}: 既存Code {Skip}件をスキップし {Add}件を追加",
				typeof(T).Name, list.Count - added.Count, added.Count);
		}
		return added;
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
				Kbname = meisho.KubunName,
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
		var taxregno = _fromDb.Fetch<Dictionary<string, object>>("select 名称 from HC$master_meisho where 名称区分='IBS' and 名称CD='01'");
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
			TaxRounding = getDataInt(recSys, "売上端数区分"),
			Jsub = new List<MasterSysTax>(),
		};
		foreach (var rec in mstTax) {
			var tax = new MasterSysTax() {
				Id = getDataLong(rec, "消費税CD"),
				TaxRate = getDataInt(rec, "消費税率"),
				DateFrom = getString(rec, "新消費税開始日", "19010101"),
				TaxNewRate = getDataInt(rec, "新消費税率"),
			};
			newSys.Jsub.Add(tax);
		}
		if (taxregno.Count > 0)
			newSys.TaxRegistrationNumber = getString(taxregno[0], "名称");

		_toDb.CreateTable(typeof(MasterSysman), isInit);
		_toDb.Insert<MasterSysman>(newSys);
		return 1;
	}
	/// <summary>
	/// 名称マスタ変換 HC$master_meisho
	/// </summary>
	public int CnvMasterMeisho(bool isInit = true) {
		var sql = $"""
    SELECT
        T.*,
        m1.名称 AS KubunName
    FROM HC$master_meisho T
    LEFT OUTER JOIN HC$master_meisho m1
        ON m1.名称区分 = '{MasterMeisho.KubunIndex}'
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
			var bumonMeisho = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [MasterMeisho.KubunBumon, getString(rec, "部門", ".")]);
			var item = new MasterShain() {
				Code = getString(rec, "社員CD"),
				Name = getString(rec, "名前"),
				Kana = getString(rec, "フリガナ"),
				Mail = getString(rec, "メール"),
				VTenpo = new() {
					Cd = getString(rec, "店舗CD"), // 残りはCnvAfterMaster()でセット
				},
				Id_Bumon = bumonMeisho?.Id ?? 0,
				VBumon = new(bumonMeisho ?? new()),
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
	public int CnvMasterEndCustomer(bool isInit = true, int chunkSize = 20000) { // 顧客分割のデフォルトチャンクサイズは20000件
		var codes = _fromDb.Fetch<string>("select 顧客CD from HC$master_kokyaku where 顧客CD > '.' order by 顧客CD");

		// 親テーブルを再作成する前に子テーブルを削除し、外部キー関係を保つ。
		if (isInit)
			_toDb.DropTable(typeof(MasterEndCustomerAccount));
		_toDb.CreateTable(typeof(MasterEndCustomer), isInit);
		_toDb.CreateTable(typeof(MasterEndCustomerAccount));

		if (codes.Count == 0)
			return 0;

		int totalCount = 0;
		foreach (var (startCode, endCode) in SplitCodeRange(codes, chunkSize))
			totalCount += ConvertMasterEndCustomerChunk(startCode, endCode, isInit);

		return totalCount;
	}

	/// <summary>
	/// 顧客CDの範囲(startCode〜endCode)1チャンク分の顧客マスターおよび会員アカウントを変換する
	/// </summary>
	private int ConvertMasterEndCustomerChunk(string startCode, string endCode, bool isInit) {
		const string sql = """
select k.*, l.ログインID, l.PASS, p.REALポイント, m.名称 as ポイントランク名称
from HC$master_kokyaku k
left join HC$master_kokyaku_login l on l.顧客CD = k.顧客CD
left join HC$point_real p on p.顧客CD = k.顧客CD
left join HC$master_meisho m on m.名称区分 = 'PT1' and m.名称CD = k.ポイントランク
where k.顧客CD between @0 and @1
order by k.顧客CD
""";
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql, startCode, endCode);
		if (rows.Count == 0)
			return 0;

		var customerList = new List<MasterEndCustomer>(rows.Count);
		foreach (var rec in rows) {
			var item = new MasterEndCustomer() {
				Code = getString(rec, "顧客CD"),
				Name = getString(rec, "顧客名"),
				Kana = getString(rec, "カナ"),
				PostalCode = getString(rec, "郵便番号"),
				Address1 = getString(rec, "住所1"),
				Address2 = getString(rec, "住所2"),
				Address3 = getString(rec, "住所3"),
				Mail = getString(rec, "メール"),
				Tel = getString(rec, "TEL1").DefaultIfEmpty(getString(rec, "TEL2")),
				Memo = getString(rec, "拡張メモ").DefaultIfEmpty(getString(rec, "メモ")),
				// 顧客ランクは旧「顧客ランク」をそのまま持つ（EEE=未ランク等の3桁コード）。
				// ポイントランク(PointRank)とは別項目で、以前は移行漏れで全件空だった。
				Rank = getString(rec, "顧客ランク"),
				VTenpo = new() {
					Cd = getString(rec, "店舗CD"),  // 残りはCnvAfterMaster()でセット
				},
			};
			var meiList = ConverterGeneralMeisho(10, "K", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;
			customerList.Add(item);
		}

		// 追記実行では既存Codeを二重登録しない(顧客アカウントも同じ行だけを作る)
		customerList = ExcludeExistingCodes(customerList, isInit);
		if (customerList.Count == 0)
			return 0;
		var addedCodes = new HashSet<string>(customerList.Select(c => c.Code), StringComparer.Ordinal);

		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		_toDb.InsertBulk(customerList);

		var accountList = new List<MasterEndCustomerAccount>(customerList.Count);
		foreach (var rec in rows) {
			var code = getString(rec, "顧客CD");
			if (!addedCodes.Contains(code))
				continue; // 既存Codeでスキップした顧客はアカウントも作らない
			var myCustomer = customerList.Where(c => c.Code == code).FirstOrDefault();

			if (myCustomer == null || myCustomer.Id == 0)
				throw new InvalidOperationException($"顧客マスターの登録IDを取得できません。顧客CD: {code}");

			accountList.Add(new MasterEndCustomerAccount() {
				Id_Customer = myCustomer.Id,
				AccountId = getString(rec, "ログインID"),
				AccountPassword = getString(rec, "PASS"),
				IsWithdrawalFlag = getDataInt(rec, "退会FLG"),
				WithdrawnDate = getString(rec, "退会日"),
				Kubun = getDataInt(rec, "顧客区分"),
				PointRank = getString(rec, "ポイントランク名称"),
				Point = getDataInt(rec, "REALポイント"),
				SalesTotalKingaku = getDataInt(rec, "累計購入金額"),
				LastVisitDate = getString(rec, "最終来店日"),
				VisitCount = getDataInt(rec, "累計来店回数"),
				AnnualSales = getDataInt(rec, "年間累計購入金額"),
			});
		}
		_toDb.InsertBulk(accountList);
		_toDb.CompleteTransaction();

		return customerList.Count;
	}

	/// <summary>
	/// ソート済みコードリストをchunkSizeごとに区切り、各チャンクの先頭/末尾コードを範囲として返す
	/// </summary>
	private static List<(string StartCode, string EndCode)> SplitCodeRange(List<string> sortedCodes, int chunkSize) {
		if (chunkSize <= 0)
			throw new ArgumentException("chunkSize must be positive");

		var ranges = new List<(string, string)>();
		for (int i = 0; i < sortedCodes.Count; i += chunkSize) {
			int end = Math.Min(i + chunkSize, sortedCodes.Count) - 1;
			ranges.Add((sortedCodes[i], sortedCodes[end]));
		}
		return ranges;
	}
	/// <summary>
	/// 商品マスター変換
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvMasterShohin(bool isInit = true, int chunkSize = 5000) {
		var codes = _fromDb.Fetch<string>("select 商品CD from HC$master_shohin where 商品CD > '.' order by 商品CD"); // 商品分類 'B01'-'B20'

		_toDb.CreateTable(typeof(MasterShohin), isInit);

		if (codes.Count == 0)
			return 0;

		int totalCount = 0;
		foreach (var (startCode, endCode) in SplitCodeRange(codes, chunkSize))
			totalCount += ConvertMasterShohinChunk(startCode, endCode, isInit);

		return totalCount;
	}

	/// <summary>
	/// 商品CDの範囲(startCode〜endCode)1チャンク分の商品マスターを変換する
	/// </summary>
	private int ConvertMasterShohinChunk(string startCode, string endCode, bool isInit) {
		const string sql = "select * from HC$master_shohin where 商品CD between @0 and @1 order by 商品CD";
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql, startCode, endCode);
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
			var sizeKubun = getString(rec, "商品サイズ区分");
			if (string.IsNullOrEmpty(sizeKubun) || sizeKubun == ".") {
				sizeKubun = MasterMeisho.KubunSize;
			}
			var colsiz = janRows
				.Select(r => {
					var col = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [MasterMeisho.KubunColor, getString(r, "色CD")]);
					var siz = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [sizeKubun, getString(r, "サイズCD")]);
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
			var meisho = _toDb.Fetch<MasterMeisho>($"""
where (Kubun ='{MasterMeisho.KubunBrand}' and Code =@0) OR (Kubun ='{MasterMeisho.KubunItem}' and Code =@1) OR (Kubun ='{MasterMeisho.KubunTenji}' and Code =@2)
OR (Kubun ='{MasterMeisho.KubunSeason}' and Code =@3) OR (Kubun ='{MasterMeisho.KubunMaterial}' and Code =@4) OR (Kubun ='{MasterMeisho.KubunCountry}' and Code =@5) OR (Kubun ='{MasterMeisho.KubunMaker}' and Code =@6)
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
				Kana = getString(rec, "旧コード"),
				TankaJodaiOrg = getDataInt(rec, "元上代"),
				TankaJodai = getDataInt(rec, "上代"),
				TankaGenka = getDataInt(rec, "原価"),
				TankaShiire = getDataInt(rec, "仕入価格"),
				DayShukka = getString(rec, "デリバリー日", "19010101"),
				DayNohin = getString(rec, "納品日", "19010101"),
				DayTento = getString(rec, "店頭投入日", "19010101"),
				Id_Tax = getDataLong(rec, "消費税CD"),
				IsZaiko = getDataInt(rec, "在庫管理FLG"),
				MakerHin = getString(rec, "メーカー品番"),
				SizeKu = sizeKubun,
				VSoko = new() {
					Cd = getString(rec, "基準倉庫CD"), // // 残りはCnvAfterMaster()でセット
				},
				Memo = getString(rec, "メモ"),
				Jcolsiz = colsiz.Count > 0 ? colsiz : null,
				Jgenka = genka.Count > 0 ? genka : null,
				Jgrade = grade.Count > 0 ? grade : null,
			};
			if (meisho.Count > 0) {
				item.VBrand = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunBrand) ?? new());
				item.Id_Brand = item.VBrand.Sid;
				item.VItem = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunItem) ?? new());
				item.Id_Item = item.VItem.Sid;
				item.VTenji = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunTenji) ?? new());
				item.Id_Tenji = item.VTenji.Sid;
				item.VSeason = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunSeason) ?? new());
				item.Id_Season = item.VSeason.Sid;
				item.VMaterial = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunMaterial) ?? new());
				item.Id_Material = item.VMaterial.Sid;
				item.VCountry = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunCountry) ?? new());
				item.Id_Country = item.VCountry.Sid;
				item.VMaker = new(meisho.FirstOrDefault(c => c.Kubun == MasterMeisho.KubunMaker) ?? new());
				item.Id_Maker = item.VMaker.Sid;
			}
			var meiList = ConverterGeneralMeisho(10, "B", rec);
			if (meiList.Count > 0)
				item.Jsub = meiList;

			list.Add(item);
		}

		// 追記実行では既存Codeを二重登録しない
		list = ExcludeExistingCodes(list, isInit);
		if (list.Count == 0)
			return 0;

		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		_toDb.InsertBulk<MasterShohin>(list);
		_toDb.CompleteTransaction();

		return list.Count;
	}
	/// <summary>
	/// 旧CVnetの「消費税計算方法」(0:請求単位/1:伝票単位/2:明細単位)を、
	/// CV10の<see cref="EnumTaxCalcUnit"/>(0:請求単位/1:伝票単位の2値)へ丸める。
	/// <para>
	/// 設計書 `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` D1のとおり、
	/// CV10は明細単位を独立した値として持たないため、旧値2は最も近い1(伝票単位)へ丸める。
	/// 3以上の想定外値も同様に1へ丸める(0のみ請求単位として残し、それ以外は安全側の伝票単位とする)。
	/// 実データ調査(1.1章)では旧値は全件0であり実害は無いが、防御的に実装する。
	/// </para>
	/// </summary>
	private static int NormalizeTaxCalcUnit(int oldValue) =>
		oldValue == (int)EnumTaxCalcUnit.Billing ? (int)EnumTaxCalcUnit.Billing : (int)EnumTaxCalcUnit.Slip;

	/// <summary>
	/// 旧CVnetの「消費税端数」をCV10の<see cref="EnumRounding"/>へそのまま取り込む。
	/// 旧・新とも 0=四捨五入/1=切上/2=切捨 で値の意味は同じだが、
	/// 範囲外の値は <see cref="CvBase.TranCalcBase.RoundTax"/> が例外を投げるため、
	/// 既定値の0(四捨五入)へ丸める防御を入れる。
	/// </summary>
	private static int NormalizeTaxRounding(int oldValue) =>
		oldValue is (int)EnumRounding.Round or (int)EnumRounding.Ceiling or (int)EnumRounding.Floor
			? oldValue
			: (int)EnumRounding.Round;

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
				Shime1 = getDataInt(rec, "締日"),
				Shime2 = getDataInt(rec, "締日2"),
				Shime3 = getDataInt(rec, "締日3"),
				PayMonth = getDataInt(rec, "入金予定月"),
				PayDay = getDataInt(rec, "入金予定日"),
				TenType = getDataInt(rec, "店種区分"),
				IsZaiko = getDataInt(rec, "在庫管理FLG"),
				IsPay = getDataInt(rec, "請求印刷"),
				TaxRounding = NormalizeTaxRounding(getDataInt(rec, "消費税端数")),
				TaxCalcUnit = NormalizeTaxCalcUnit(getDataInt(rec, "消費税計算方法")),
				SlipFormType = getDataInt(rec, "伝票発行区分"),
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
			var payMethod = getMeisho(MasterMeisho.KubunKin, getString(rec, "支払方法"));
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
				// HC$MASTER_SIIRE に「締日2」「締日3」は無い(得意先だけが持つ)。
				// 以前は得意先ブロックをコピーして存在しない列を読んでおり、getString が
				// 存在しないキーを空文字で返すため黙って0が入っていた。読まずに既定値0とする。
				PayMonth = getDataInt(rec, "支払予定月"),
				PayDay = getDataInt(rec, "支払予定日"),
				TaxRounding = NormalizeTaxRounding(getDataInt(rec, "消費税端数")),
				TaxCalcUnit = NormalizeTaxCalcUnit(getDataInt(rec, "消費税計算方法")),
				IsPay = getDataInt(rec, "支払印刷"),
				// 支払方法は名称マスタ(KIN)を引く。名称マスタは本変換より前に投入済み。
				Id_PayMethod = payMethod?.Id ?? 0,
				VPayMethod = new(payMethod?.Id ?? 0, payMethod?.Code ?? string.Empty, payMethod?.Name ?? string.Empty),
				// Id_Paysaki(支払先)は MasterShiire 自身への自己参照で、変換中は参照先が
				// まだ挿入されていないことがある。CnvMasterAfter2 で解決する。
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
	/// <summary>
	/// 生地・付属マスター変換 HC$MASTER_SHKIJI
	/// <para>旧 区分CD(名称区分'D04') → 新 KIJ資材区分 の対応</para>
	/// <para>1:生地→01布帛, 2:付属品A群→06ボタン, 3:付属品B群→06ボタン, 6:プレス→99, 7:その他→99, 8:サンプル→99, 9:デザイン→99, 未知→99</para>
	/// </summary>
	private static readonly Dictionary<string, string> _kubunShkijiMap = new() {
		["1"] = "01",
		["2"] = "06",
		["3"] = "06",
		["6"] = "99",
		["7"] = "99",
		["8"] = "99",
		["9"] = "99",
	};
	public int CnvMasterMaterial(bool isInit = true) {
		const string sql = "select * from HC$MASTER_SHKIJI where 商品CD>'.' order by 商品CD";
		var cnt = ConvertMaster(sql, isInit, rec => {
			var oldKubun = getString(rec, "区分CD");
			var newKubunCode = _kubunShkijiMap.GetValueOrDefault(oldKubun, "99");
			var kubun = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [MasterMeisho.KubunKiji, newKubunCode]);
			var shiire = _toDb.FirstOrDefault<MasterShiire>("where Code=@0", getString(rec, "仕入先CD"));
			var item = new MasterMaterial() {
				Code = getString(rec, "商品CD"),
				Name = getString(rec, "商品名"),
				Ryaku = getString(rec, "略称"),
				Kana = getString(rec, "旧コード"),
				Id_Kubun = kubun?.Id ?? 0,
				VKubun = new(kubun ?? new()),
				Id_Shiire = shiire?.Id ?? 0,
				VShiire = new(shiire?.Id ?? 0, shiire?.Code ?? string.Empty, shiire?.Name ?? string.Empty),
				CodeShiire = getString(rec, "仕入先商品CD"),
				TankaShiire = getDataInt(rec, "単価"),
				Memo = getString(rec, "メモ"),
			};
			return item;
		});
		cnt += InsertMaterialPlaceholders();
		return cnt;
	}
	/// <summary>
	/// <see cref="CnvTran02Material"/>（旧伝票処理区分02）区分30(値引)/99(その他)向けのプレースホルダ資材を追加する。
	/// <para>
	/// 旧 <c>HC$tran_tori1</c> の該当明細は商品CDが常に空欄(".")のため、実マスタの代わりにこの2件へ紐付ける
	/// （ユーザー指定: Code=000030 値引き / Code=000099 消費税）。消費税区分は非課税(0)固定
	/// （これらは実在の資材ではなく、金額調整の明細行を表す仮想アイテムのため）。
	/// </para>
	/// </summary>
	private int InsertMaterialPlaceholders() {
		var kubunOther = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [MasterMeisho.KubunKiji, "99"]);
		var vKubun = new CodeNameView(kubunOther ?? new());
		var placeholders = new List<MasterMaterial> {
			new() { Code = "000030", Name = "値引き", Id_Kubun = kubunOther?.Id ?? 0, VKubun = vKubun, Id_Tax = 0 },
			new() { Code = "000099", Name = "消費税", Id_Kubun = kubunOther?.Id ?? 0, VKubun = vKubun, Id_Tax = 0 },
		};
		_toDb.InsertBulk<MasterMaterial>(placeholders);
		return placeholders.Count;
	}
	/// <summary>
	/// 大量件数の変換結果を、単一トランザクション内でchunkSizeごとに分割してInsertBulkする
	/// (予算マスタ変換のような数十万件規模の一括変換で、1回のInsertBulkに渡す件数を抑えるための共通ヘルパ)
	/// </summary>
	private void InsertBulkChunked<T>(List<T> list, int chunkSize) {
		if (list.Count == 0)
			return;
		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		foreach (var chunk in list.Chunk(chunkSize))
			_toDb.InsertBulk<T>(chunk);
		_toDb.CompleteTransaction();
	}
	/// <summary>
	/// 日付を持つマスタを、年月(yyyyMM)ごとの件数をもとに日付範囲(yyyyMMdd〜yyyyMMdd)へ分割する
	/// <para>
	/// ソート済みの(年月, 件数)リストを先頭から累計し、chunkRowsに達したところで1範囲を確定する。
	/// 件数の少ない年月は次の年月と同じ範囲にまとめられ、末尾の端数も最後の範囲へ含める。
	/// </para>
	/// <para>予算マスタは日付が集計キーに含まれるため、年月で区切っても1グループが複数チャンクに分かれることはない。</para>
	/// </summary>
	private static List<(string DayFrom, string DayTo)> SplitYearMonthRange(List<(string YearMonth, int Count)> sortedYearMonths, int chunkRows) {
		if (chunkRows <= 0)
			throw new ArgumentException("chunkRows must be positive");

		var ranges = new List<(string, string)>();
		string? startYm = null;
		string endYm = string.Empty;
		int sum = 0;
		foreach (var (yearMonth, count) in sortedYearMonths) {
			startYm ??= yearMonth;
			endYm = yearMonth;
			sum += count;
			if (sum < chunkRows)
				continue; // 件数が少ない年月は次の年月と同じ範囲へまとめる
			ranges.Add(($"{startYm}01", $"{endYm}99"));
			startYm = null;
			sum = 0;
		}
		if (startYm != null)
			ranges.Add(($"{startYm}01", $"{endYm}99")); // 末尾の端数を最後の範囲にまとめる
		return ranges;
	}
	/// <summary>
	/// 店舗ブランド予算マスター変換 HC$MASTER_YO_TENPO
	/// <para>
	/// 旧テーブルはアイテムCD単位で予算を保持しているが、新 <see cref="MasterYosanBrand"/> にアイテム次元がないため、
	/// 店舗CD・ブランドCD・日付単位で予算金額・粗利予算を合計(SUM)して畳み込んで取得する。
	/// </para>
	/// <para>
	/// 数十万件規模になるため、年月(yyyyMM)ごとの件数をもとに日付範囲へ分割して取得・登録する。
	/// また行ごとの<c>_toDb.FirstOrDefault</c>は使わず、MasterTokui・MasterMeisho(ブランド区分)を事前に全件取得して辞書化する。
	/// </para>
	/// </summary>
	/// <param name="isInit"></param>
	/// <param name="chunkRows">1チャンクの目安件数</param>
	/// <returns></returns>
	public int CnvMasterYosanBrand(bool isInit = true, int chunkRows = 50000) { // 予算分割のデフォルトチャンクサイズは50000件
		const string ymSql = "select substr(日付,1,6) as YM, count(*) as CNT from HC$MASTER_YO_TENPO where 店舗CD > '.' and 日付 > '.' group by substr(日付,1,6) order by 1";
		var yearMonths = _fromDb.Fetch<Dictionary<string, object>>(ymSql)
			.Select(rec => (YearMonth: getString(rec, "YM"), Count: getDataInt(rec, "CNT")))
			.ToList();
		_toDb.CreateTable(typeof(MasterYosanBrand), isInit);

		if (yearMonths.Count == 0)
			return 0;

		var tokuiDict = _toDb.Fetch<MasterTokui>().ToDictionary(t => t.Code);
		var brandDict = _toDb.Fetch<MasterMeisho>("where Kubun=@0", MasterMeisho.KubunBrand).ToDictionary(m => m.Code);

		int totalCount = 0;
		int skipCount = 0;
		foreach (var (dayFrom, dayTo) in SplitYearMonthRange(yearMonths, chunkRows)) {
			var (count, skip) = ConvertMasterYosanBrandChunk(dayFrom, dayTo, tokuiDict, brandDict);
			totalCount += count;
			skipCount += skip;
		}
		if (skipCount > 0)
			_logger.LogWarning("MasterYosanBrand変換: 店舗CD未解決のため {Count} 件をスキップしました", skipCount);

		return totalCount;
	}
	/// <summary>
	/// 日付範囲(dayFrom〜dayTo)1チャンク分の店舗ブランド予算を変換する
	/// </summary>
	/// <returns>登録件数と、店舗CD未解決でスキップした件数</returns>
	private (int Count, int Skip) ConvertMasterYosanBrandChunk(string dayFrom, string dayTo, Dictionary<string, MasterTokui> tokuiDict, Dictionary<string, MasterMeisho> brandDict) {
		const string sql = """
select 店舗CD, ブランドCD, 日付, sum(予算金額) as 予算金額, sum(粗利予算) as 粗利予算
from HC$MASTER_YO_TENPO
where 店舗CD > '.' and 日付 between @0 and @1
group by 店舗CD, ブランドCD, 日付
order by 店舗CD, ブランドCD, 日付
""";
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql, dayFrom, dayTo);
		if (rows.Count == 0)
			return (0, 0);

		var list = new List<MasterYosanBrand>(rows.Count);
		int skipCount = 0;
		foreach (var rec in rows) {
			var tenpoCode = getString(rec, "店舗CD");
			if (!tokuiDict.TryGetValue(tenpoCode, out var tokui)) {
				skipCount++;
				continue; // 店舗CDが未解決の行は対象店舗が存在しないため変換対象外
			}
			var brandCode = getString(rec, "ブランドCD");
			brandDict.TryGetValue(brandCode, out var brand); // ブランドIdは任意のため、未解決でもId=0で登録する

			list.Add(new MasterYosanBrand() {
				Id_Tenpo = tokui.Id,
				Id_Brand = brand?.Id ?? 0,
				DenDay = getString(rec, "日付"),
				UriYosan = getDataLong(rec, "予算金額"),
				ArariYosan = getDataLong(rec, "粗利予算"),
				VTenpo = new CodeNameView(tokui.Id, tokui.Code, tokui.Name),
				VBrand = brand != null ? new CodeNameView(brand) : new(),
			});
		}
		InsertBulkChunked(list, 20000);

		return (list.Count, skipCount);
	}
	/// <summary>
	/// 販売員予算マスター変換 HC$MASTER_YO_HANBAI
	/// <para>cv163スキーマでは0件だが、他スキーマには実データがあるため年月(yyyyMM)ごとの件数をもとに日付範囲へ分割して変換する。</para>
	/// </summary>
	/// <param name="isInit"></param>
	/// <param name="chunkRows">1チャンクの目安件数</param>
	/// <returns></returns>
	public int CnvMasterYosanHanbai(bool isInit = true, int chunkRows = 50000) { // 予算分割のデフォルトチャンクサイズは50000件
		const string ymSql = "select substr(日付,1,6) as YM, count(*) as CNT from HC$MASTER_YO_HANBAI where 販売員CD > '.' and 日付 > '.' group by substr(日付,1,6) order by 1";
		var yearMonths = _fromDb.Fetch<Dictionary<string, object>>(ymSql)
			.Select(rec => (YearMonth: getString(rec, "YM"), Count: getDataInt(rec, "CNT")))
			.ToList();
		_toDb.CreateTable(typeof(MasterYosanHanbai), isInit);

		if (yearMonths.Count == 0)
			return 0;

		var shainDict = _toDb.Fetch<MasterShain>().ToDictionary(s => s.Code);

		int totalCount = 0;
		int skipCount = 0;
		foreach (var (dayFrom, dayTo) in SplitYearMonthRange(yearMonths, chunkRows)) {
			var (count, skip) = ConvertMasterYosanHanbaiChunk(dayFrom, dayTo, shainDict);
			totalCount += count;
			skipCount += skip;
		}
		if (skipCount > 0)
			_logger.LogWarning("MasterYosanHanbai変換: 販売員CD未解決のため {Count} 件をスキップしました", skipCount);

		return totalCount;
	}
	/// <summary>
	/// 日付範囲(dayFrom〜dayTo)1チャンク分の販売員予算を変換する
	/// </summary>
	/// <returns>登録件数と、販売員CD未解決でスキップした件数</returns>
	private (int Count, int Skip) ConvertMasterYosanHanbaiChunk(string dayFrom, string dayTo, Dictionary<string, MasterShain> shainDict) {
		const string sql = """
select 販売員CD, 日付, sum(予算金額) as 予算金額, sum(粗利予算) as 粗利予算
from HC$MASTER_YO_HANBAI
where 販売員CD > '.' and 日付 between @0 and @1
group by 販売員CD, 日付
order by 販売員CD, 日付
""";
		var rows = _fromDb.Fetch<Dictionary<string, object>>(sql, dayFrom, dayTo);
		if (rows.Count == 0)
			return (0, 0);

		var list = new List<MasterYosanHanbai>(rows.Count);
		int skipCount = 0;
		foreach (var rec in rows) {
			var shainCode = getString(rec, "販売員CD");
			if (!shainDict.TryGetValue(shainCode, out var shain)) {
				skipCount++;
				continue; // 販売員CDが未解決の行は対象社員が存在しないため変換対象外
			}

			list.Add(new MasterYosanHanbai() {
				Id_Shain = shain.Id,
				DenDay = getString(rec, "日付"),
				UriYosan = getDataLong(rec, "予算金額"),
				ArariYosan = getDataLong(rec, "粗利予算"),
				VShain = new CodeNameView(shain.Id, shain.Code, shain.Name),
			});
		}
		InsertBulkChunked(list, 20000);

		return (list.Count, skipCount);
	}
	public int CnvMasterConfig(bool isInit = true) {
		const string sql = "select * from HC$MASTER_Config where フラグ名>'.' order by カテゴリ,フラグ名";
		return ConvertMaster(sql, isInit, rec => {
			var item = new MasterConfig() {
				Category = getString(rec, "カテゴリ"),
				Name = getString(rec, "フラグ名"),
				Val = getString(rec, "値"),
				Example = getString(rec, "リスト"),
				Memo = getString(rec, "MEMO")
			};
			return item;
		});
	}
	public int CnvAfterMaster(bool isInit = true) {
		int cnt = 0;
		// MasterShain の VTenpo.Cd をキーに MasterTokui を検索し、該当する場合は MasterShain の VTenpo と Id_Tenpo を更新する
		var shainList = _toDb.Fetch<MasterShain>("where json_extract(VTenpo, '$.Cd') IS NOT NULL AND json_extract(VTenpo, '$.Cd') <> ''");
		if (shainList != null && shainList.Count > 0) {
			foreach (var shain in shainList) {
				try {
					var code = shain?.VTenpo?.Cd ?? string.Empty;
					if (shain == null || string.IsNullOrWhiteSpace(code))
						continue;

					// 該当Codeを持つ MasterTokui を取得し、存在すれば shain.VTenpo と Id_Tenpo を設定する
					var tokui = _toDb.FirstOrDefault<MasterTokui>("where Code=@0", code);
					if (tokui != null) {
						shain.VTenpo = new CodeNameView(tokui.Id, tokui.Code, tokui.Name);
						shain.Id_Tenpo = tokui.Id;
						// 必要ならデータベース上の shain レコードを更新
						try {
							_toDb.Update(shain);
						}
						catch (Exception updEx) {
							_logger?.LogWarning(updEx, "CnvAfterMaster: Failed to update MasterShain Id={0}", shain.Id);
						}
					}
				}
				catch (Exception ex) {
					_logger?.LogWarning(ex, "CnvAfterMaster: Failed to resolve VTenpo for MasterShain Code={0}", shain?.Code);
				}
			}
			cnt += shainList.Count;
		}
		var customerList = _toDb.Fetch<MasterEndCustomer>("where json_extract(VTenpo, '$.Cd') IS NOT NULL AND json_extract(VTenpo, '$.Cd') <> ''");
		if (customerList != null && customerList.Count > 0) {
			foreach (var customer in customerList) {
				try {
					var code = customer?.VTenpo?.Cd ?? string.Empty;
					if (customer == null || string.IsNullOrWhiteSpace(code))
						continue;

					// 該当Codeを持つ MasterTokui を取得し、存在すれば customer.VTenpo と Id_Tenpo を設定する
					var tokui = _toDb.FirstOrDefault<MasterTokui>("where Code=@0", code);
					if (tokui != null) {
						customer.VTenpo = new CodeNameView(tokui.Id, tokui.Code, tokui.Name);
						customer.Id_Tenpo = tokui.Id;
						// 必要ならデータベース上の customer レコードを更新
						try {
							_toDb.Update(customer);
						}
						catch (Exception updEx) {
							_logger?.LogWarning(updEx, "CnvAfterMaster: Failed to update MasterEndCustomer Id={0}", customer.Id);
						}
					}
				}
				catch (Exception ex) {
					_logger?.LogWarning(ex, "CnvAfterMaster: Failed to resolve VTenpo for MasterEndCustomer Code={0}", customer?.Code);
				}
			}
			cnt += customerList.Count;
		}
		var shohinList = _toDb.Fetch<MasterShohin>("where json_extract(VSoko, '$.Cd') IS NOT NULL AND json_extract(VSoko, '$.Cd') <> ''");
		if (shohinList != null && shohinList.Count > 0) {
			foreach (var shohin in shohinList) {
				try {
					var code = shohin?.VSoko?.Cd ?? string.Empty;
					if (shohin == null || string.IsNullOrWhiteSpace(code))
						continue;

					// 該当Codeを持つ MasterTokui を取得し、存在すれば shohin.VTenpo と Id_Tenpo を設定する
					var tokui = _toDb.FirstOrDefault<MasterTokui>("where Code=@0", code);
					if (tokui != null) {
						shohin.VSoko = new CodeNameView(tokui.Id, tokui.Code, tokui.Name);
						shohin.Id_Soko = tokui.Id;
						// 必要ならデータベース上の shohin レコードを更新
						try {
							_toDb.Update(shohin);
						}
						catch (Exception updEx) {
							_logger?.LogWarning(updEx, "CnvAfterMaster: Failed to update MasterShohin Id={0}", shohin.Id);
						}
					}
				}
				catch (Exception ex) {
					_logger?.LogWarning(ex, "CnvAfterMaster: Failed to resolve VTenpo for MasterShohin Code={0}", shohin?.Code);
				}
			}
			cnt += shohinList.Count;
		}
		return cnt;
	}
	/// <summary>
	/// マスター変換後の住所の正規化
	/// </summary>
	/// <param name="isInit"></param>
	/// <returns></returns>
	public int CnvAfterMasterAddress(bool isInit = true) {
		int cnt = 0;
		cnt += ConvertItemAddress<MasterSysman>();
		cnt += ConvertItemAddress<MasterTokui>();
		cnt += ConvertItemAddress<MasterShiire>();
		cnt += ConvertItemAddress<MasterEndCustomer>();
		return cnt;
	}
	public int ConvertItemAddress<T>() where T : BaseDbHasAddress {
		int cnt = 0;
		var list = _toDb.Fetch<T>();
		long nowId = 0;
		try {
			if (list != null && list.Count > 0) {
				foreach (var item in list) {
					if (item == null) continue;
					nowId = item.Id;
					// 特殊ケースはスキップさせる(個人情報のため***化)
					if (string.IsNullOrEmpty(item.Address1) || item.Address1.Contains("**"))
						continue;
					// 住所をまとめたものから、都道府県をAddress1 に、市区町村をAddress2に、残りをAddress3に分割して保存する
					var all = $"{item.Address1?.Trim()}{item.Address2?.Trim()}{item.Address3?.Trim()}".Trim();
					if (string.IsNullOrWhiteSpace(all))
						continue;
					var retAddress = ConvertAddressString(all);
					item.Address1 = retAddress.Item1;
					item.Address2 = retAddress.Item2;
					item.Address3 = retAddress.Item3;
					_toDb.Update(item);
					cnt++;
				}
			}
		}
		catch (Exception ex) {
			_logger?.LogWarning(ex, $"ConvertItemAddress: Failed to convert address for {typeof(T).Name} Id={nowId}");
		}
		return cnt;
	}
	static Regex? prefRegex;
	static Regex? cityRegex;
	Tuple<string, string, string> ConvertAddressString(string address) {
		if (prefRegex == null)
			prefRegex = new Regex(@"^(東京都|北海道|京都府|大阪府|.{2,3}県)", RegexOptions.Compiled);
		if (cityRegex == null)
			cityRegex = new Regex(@"^(.+?郡.+?[町村]|.+?市.+?区|.+?[市区町村])", RegexOptions.Compiled);
		var normalizedAddress = address
			.Replace(" ", string.Empty)
			.Replace("　", string.Empty)
			.Trim();
		if (string.IsNullOrWhiteSpace(normalizedAddress))
			return new Tuple<string, string, string>(string.Empty, string.Empty, string.Empty);
		var prefMatch = prefRegex.Match(normalizedAddress);
		if (!prefMatch.Success)
			return new Tuple<string, string, string>(string.Empty, string.Empty, address);
		var newAddress1 = prefMatch.Value;
		var restAfterPref = normalizedAddress[newAddress1.Length..];
		var cityMatch = cityRegex.Match(restAfterPref);
		var newAddress2 = cityMatch.Success ? cityMatch.Value : string.Empty;
		var newAddress3 = cityMatch.Success ? restAfterPref[newAddress2.Length..] : restAfterPref;
		return new Tuple<string, string, string>(newAddress1, newAddress2, newAddress3);
	}
}
