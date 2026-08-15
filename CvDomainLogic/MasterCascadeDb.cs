using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CvDomainLogic;

/// <summary>
/// V*列（CodeNameView 物理列）の伝播定義
/// </summary>
/// <param name="Target">更新対象の物理テーブル型</param>
/// <param name="IdColumn">参照キー列名(Id_*)</param>
/// <param name="VColumn">更新するV*列名 (規約: "V" + IdColumn の "Id_" を除いた名前)</param>
/// <param name="Source">参照先マスタ型</param>
public sealed record CascadeVRule(Type Target, string IdColumn, string VColumn, Type Source);

/// <summary>
/// JSON配列列（List&lt;MasterGeneralMeisho&gt;）内の名称スナップショットの伝播定義
/// </summary>
/// <param name="Target">更新対象の物理テーブル型</param>
/// <param name="JsonColumn">JSON配列を格納している列名（Jsub）</param>
/// <param name="Source">参照先マスタ型</param>
public sealed record CascadeJsonRule(Type Target, string JsonColumn, Type Source);

/// <summary>
/// マスタの Code/Name 変更を、参照側の V*列(CodeNameView)へ伝播する。
/// Master系のみが対象。Tran系のV*列は伝票作成時点の名称(監査値)なので伝播しない。
/// SQLite 3.46+ 前提(json_object/json_extract)。他DB移行時は CvBaseMariadb/CvBaseOracle 側での吸収が必要。
/// </summary>
public class MasterCascadeDb {
	ExDatabase _db;
	ILogger<MasterCascadeDb> _logger;
	public MasterCascadeDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<MasterCascadeDb>();
	}

	/// <summary>
	/// 伝播元となるマスタ型 (これ以外の型が更新されても伝播は不要)
	/// </summary>
	static readonly Type[] _sourceTypes = [
		typeof(MasterMeisho),
		typeof(MasterTokui),
		typeof(MasterShain),
		typeof(MasterShiire),
	];

	/// <summary>
	/// V*列の伝播定義(唯一の正)。列を追加した場合はここに追記する。
	/// MasterTorihiki は実テーブルではないため、継承列は MasterTokui / MasterShiire に分けて定義する。
	/// Id_Paysaki の参照先は宣言型ごとに異なる(MasterTokui→MasterTokui, MasterShiire→MasterShiire)ため、
	/// [ForeignKey]属性からの自動導出は行わずこの定義を正とする。
	/// </summary>
	public static readonly CascadeVRule[] VRules = [
		// 参照先: MasterShain
		new (typeof(SysLogin),           nameof(SysLogin.Id_Shain),               nameof(SysLogin.VShain),               typeof(MasterShain)),
		new (typeof(MasterTokui),        nameof(MasterTokui.Id_Shain),            nameof(MasterTokui.VShain),            typeof(MasterShain)),
		new (typeof(MasterShiire),       nameof(MasterShiire.Id_Shain),           nameof(MasterShiire.VShain),           typeof(MasterShain)),
		new (typeof(MasterYosanHanbai),  nameof(MasterYosanHanbai.Id_Shain),      nameof(MasterYosanHanbai.VShain),      typeof(MasterShain)),
		// 参照先: MasterTokui (倉庫 TenType=0 / 店舗 TenType=6 / 請求先)
		new (typeof(MasterSysman),       nameof(MasterSysman.Id_Soko),            nameof(MasterSysman.VSoko),            typeof(MasterTokui)),
		new (typeof(MasterShain),        nameof(MasterShain.Id_Tenpo),            nameof(MasterShain.VTenpo),            typeof(MasterTokui)),
		new (typeof(MasterEndCustomer),  nameof(MasterEndCustomer.Id_Tenpo),      nameof(MasterEndCustomer.VTenpo),      typeof(MasterTokui)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Soko),            nameof(MasterShohin.VSoko),            typeof(MasterTokui)),
		new (typeof(MasterTokui),        nameof(MasterTokui.Id_Paysaki),          nameof(MasterTokui.VPaysaki),          typeof(MasterTokui)),
		new (typeof(MasterYosanBrand),   nameof(MasterYosanBrand.Id_Tenpo),       nameof(MasterYosanBrand.VTenpo),       typeof(MasterTokui)),
		// 参照先: MasterShiire (仕入先の請求先は仕入先自身)
		new (typeof(MasterShiire),       nameof(MasterShiire.Id_Paysaki),         nameof(MasterShiire.VPaysaki),         typeof(MasterShiire)),
		// 参照先: MasterMeisho
		new (typeof(MasterShain),        nameof(MasterShain.Id_Bumon),            nameof(MasterShain.VBumon),            typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Brand),           nameof(MasterShohin.VBrand),           typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Item),            nameof(MasterShohin.VItem),            typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Tenji),           nameof(MasterShohin.VTenji),           typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Maker),           nameof(MasterShohin.VMaker),           typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Season),          nameof(MasterShohin.VSeason),          typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Material),        nameof(MasterShohin.VMaterial),        typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Id_Country),         nameof(MasterShohin.VCountry),         typeof(MasterMeisho)),
		new (typeof(MasterTokui),        nameof(MasterTokui.Id_PayMethod),        nameof(MasterTokui.VPayMethod),        typeof(MasterMeisho)),
		new (typeof(MasterShiire),       nameof(MasterShiire.Id_PayMethod),       nameof(MasterShiire.VPayMethod),       typeof(MasterMeisho)),
		new (typeof(MasterYosanBrand),   nameof(MasterYosanBrand.Id_Brand),       nameof(MasterYosanBrand.VBrand),       typeof(MasterMeisho)),
	];

	/// <summary>
	/// MasterMeisho の「区分そのものを定義する行」の区分。この区分の行の Name が区分名(KubunName/Kbname)の元になる。
	/// </summary>
	const string MeishoKubunIndex = "IDX";

	/// <summary>
	/// Jsub(List&lt;MasterGeneralMeisho&gt;)内の名称スナップショットの伝播定義。
	/// 各要素は Sid/Cd/Mei(選択された名称行)と Kb/Kbname(区分コードと区分名)を持つ。
	/// Kb は MasterMeisho.Kubun、Kbname は Kubun='IDX' かつ Code=Kb の行の Name に対応する
	/// (`MasterShohinMenteViewModel.DoGetKubun` が `Kubun='IDX' and Code between 'B01' and 'B10'` を取得している)。
	/// </summary>
	public static readonly CascadeJsonRule[] JsubRules = [
		new (typeof(MasterShain),        nameof(MasterShain.Jsub),        typeof(MasterMeisho)),
		new (typeof(MasterEndCustomer),  nameof(MasterEndCustomer.Jsub),  typeof(MasterMeisho)),
		new (typeof(MasterShohin),       nameof(MasterShohin.Jsub),       typeof(MasterMeisho)),
		new (typeof(MasterTokui),        nameof(MasterTokui.Jsub),        typeof(MasterMeisho)),
		new (typeof(MasterShiire),       nameof(MasterShiire.Jsub),       typeof(MasterMeisho)),
	];

	/// <summary>
	/// 伝播元となるマスタ型かどうか
	/// </summary>
	public static bool IsCascadeSource(Type type) => Array.IndexOf(_sourceTypes, type) >= 0;

	/// <summary>
	/// 更新前後のエンティティを比較し、V*列の伝播が必要かどうかを判定する。
	/// 伝播元マスタ以外、Code/Nameのどちらも変わっていない場合は false(無駄なUPDATEを流さない)。
	/// </summary>
	/// <param name="itemType">更新対象の型</param>
	/// <param name="newItem">更新後のエンティティ</param>
	/// <param name="orgItem">更新前のエンティティ(DBから取得したもの)</param>
	public static bool NeedsCascade(Type itemType, object? newItem, object? orgItem) {
		if (!IsCascadeSource(itemType))
			return false;
		if (newItem is not IBaseCodeName newCn || orgItem is not IBaseCodeName orgCn)
			return false;
		return !string.Equals(newCn.Code, orgCn.Code, StringComparison.Ordinal)
			|| !string.Equals(newCn.Name, orgCn.Name, StringComparison.Ordinal);
	}

	/// <summary>
	/// sourceType/id のマスタが newCode/newName へ変更されたことを参照側のV*列へ伝播する。
	/// 呼び出し側でトランザクションを開始済みであることを前提とする(マスタ更新と同一トランザクションで実行する)。
	/// SQLエラーは呼び出し側へ送出する(マスタ更新ごとロールバックさせるため)。
	/// </summary>
	/// <param name="sourceType">更新されたマスタの型</param>
	/// <param name="id">更新されたマスタのId</param>
	/// <param name="newCode">変更後のCode</param>
	/// <param name="newName">変更後のName</param>
	/// <param name="vdate">
	/// 更新側が使用した Vdu 値。0の場合は内部で採番する。
	/// 自己参照(MasterTokui.VPaysaki 等)で更新元の行自身が伝播対象になる場合に Vdu が二重更新されて
	/// クライアント保持値とずれるため、呼び出し側(HandleUpdate)の vdate を渡すこと。
	/// </param>
	/// <param name="kubun">
	/// 更新されたマスタが MasterMeisho の場合の Kubun。'IDX'(区分定義行)のときのみ区分名の伝播(R3)を行う。
	/// </param>
	/// <param name="oldCode">
	/// 変更前の Code。区分定義行で Code 自体が変更された場合は区分体系の変更であり、
	/// MasterMeisho.Kubun 等の参照が壊れるため R3 を実行せず警告ログを出す。
	/// </param>
	/// <returns>更新された行数の合計</returns>
	public int CascadeFromMaster(Type sourceType, long id, string newCode, string newName, long vdate = 0,
								string? kubun = null, string? oldCode = null) {
		if (!IsCascadeSource(sourceType) || id <= 0)
			return 0;
		if (vdate == 0)
			vdate = Common.GetVdate();
		var code = newCode ?? string.Empty;
		var name = newName ?? string.Empty;
		var cnt = 0;
		// R1: V*列(物理列)
		foreach (var rule in VRules) {
			if (rule.Source != sourceType)
				continue;
			cnt += ExecuteVRule(rule, id, code, name, vdate);
		}
		if (sourceType == typeof(MasterMeisho)) {
			// R2: Jsub 配列内の Cd/Mei
			foreach (var rule in JsubRules)
				cnt += ExecuteJsubCodeNameRule(rule, id, code, name, vdate);
			// R4/R5: MasterShohin.Jcolsiz と DerivedShohinColSiz
			cnt += ExecuteJcolsizRules(id, code, name, vdate);
			// R3: 区分名(MasterMeisho.KubunName と Jsub の Kbname)
			cnt += ExecuteKubunNameRules(code, name, kubun, oldCode, vdate);
		}
		return cnt;
	}

	/// <summary>
	/// 全V*列を参照先マスタの現在値で再同期する(保守用バッチ)。
	/// </summary>
	/// <returns>更新された行数の合計</returns>
	public int ResyncAll() => ResyncAll([]);

	/// <summary>
	/// 全V*列を参照先マスタの現在値で再同期する(保守用バッチ)。
	/// 1ルール単位で失敗しても他ルールの処理を継続する(バッチのため全体を止めない)。
	/// 失敗はログ出力に加えて errors へ積むので、呼び出し側で利用者へ提示すること
	/// (件数だけを見ると失敗が黙って無視されるため)。
	/// </summary>
	/// <param name="errors">ルール単位のエラー内容を受け取るリスト</param>
	/// <returns>更新された行数の合計</returns>
	public int ResyncAll(List<string> errors) {
		var vdate = Common.GetVdate();
		var cnt = 0;
		var total = Stopwatch.StartNew();
		// V*列: 対象テーブル単位に1文へまとめる。
		// 列ごとに1文ずつ実行すると同じテーブルを列数分だけ全走査することになり、
		// JSON列を含む幅の広い行(MasterShohinは8列=8回)では読み取りI/Oが支配的になるため。
		foreach (var group in VRules.GroupBy(r => r.Target)) {
			var rules = group.ToArray();
			cnt += RunResync(errors, group.Key.Name, $"V*列{rules.Length}列", () => ResyncVRulesByTable(group.Key, rules, vdate));
		}
		// Jsub: Cd/Mei と Kbname を1文にまとめる(別文にすると同じテーブルを2回走査することになる)
		foreach (var rule in JsubRules)
			cnt += RunResync(errors, rule.Target.Name, rule.JsonColumn, () => ResyncJsub(rule, vdate));
		// MasterMeisho.KubunName
		cnt += RunResync(errors, nameof(MasterMeisho), nameof(MasterMeisho.KubunName), () => ResyncMeishoKubunName(vdate));
		// MasterShohin.Jcolsiz と DerivedShohinColSiz
		cnt += RunResync(errors, nameof(MasterShohin), nameof(MasterShohin.Jcolsiz), () => ResyncJcolsiz(vdate));
		_logger.LogInformation("V*列再同期 完了 更新行数={Count} 失敗={ErrorCount} 所要={Elapsed}ms", cnt, errors.Count, total.ElapsedMilliseconds);
		return cnt;
	}

	/// <summary>
	/// 再同期の1単位を実行し、失敗はログとerrorsへ記録して0を返す。
	/// 所要時間を必ずログへ出す(どのルールが遅いかを実データで特定できるようにするため)。
	/// </summary>
	int RunResync(List<string> errors, string table, string column, Func<int> action) {
		var sw = Stopwatch.StartNew();
		try {
			var one = action();
			_logger.LogInformation("V*列再同期 {Table}.{Column} 更新行数={Count} 所要={Elapsed}ms", table, column, one, sw.ElapsedMilliseconds);
			return one;
		}
		catch (Exception ex) {
			errors.Add($"{table}.{column}: {ex.Message}");
			_logger.LogError(ex, "V*列再同期に失敗 {Table}.{Column} 所要={Elapsed}ms", table, column, sw.ElapsedMilliseconds);
			return 0;
		}
	}

	/// <summary>
	/// 参照先の存在しない(danglingな)V*列の件数をルール単位で返す(保守・調査用。更新はしない)
	/// </summary>
	public List<(string Target, string VColumn, int Count)> CountDanglingRefs() {
		var ret = new List<(string, string, int)>();
		foreach (var rule in VRules) {
			var table = _db.GetTableName(rule.Target);
			var source = _db.GetTableName(rule.Source);
			var sql = $@"
select count(*) from {table}
 where {table}.{rule.IdColumn} > 0
   and not exists (select 1 from {source} S where S.Id = {table}.{rule.IdColumn})";
			try {
				var cnt = _db.ExecuteScalar<int>(sql);
				if (cnt > 0)
					ret.Add((table, rule.VColumn, cnt));
			}
			catch (Exception ex) {
				_logger.LogError(ex, "dangling参照の集計に失敗 {Table}.{Column}", table, rule.VColumn);
			}
		}
		return ret;
	}

	/// <summary>
	/// 1ルール分の伝播SQLを実行する。
	/// Sid/Cd/Mei のいずれかが異なる行のみ更新するため、同じ内容で再実行しても0件となる(冪等)。
	/// Sidを条件に含めているため、Id_*だけがセットされV*列が空(''や{})の行も同時に修復される。
	/// </summary>
	int ExecuteVRule(CascadeVRule rule, long id, string code, string name, long vdate) {
		var table = _db.GetTableName(rule.Target);
		var col = SafeJsonColumn(rule.VColumn);
		var sql = $@"
update {table}
   set {rule.VColumn} = json_object('Sid', @0, 'Cd', @1, 'Mei', @2),
       Vdu = @3
 where {rule.IdColumn} = @0
   and ( ifnull(json_extract({col}, '$.Cd' ), '') <> @1
      or ifnull(json_extract({col}, '$.Mei'), '') <> @2
      or ifnull(json_extract({col}, '$.Sid'),  0) <> @0 )";
		return _db.Execute(sql, [id, code, name, vdate]);
	}

	// ============================================================
	// R2: Jsub 配列内の Cd/Mei
	// ============================================================

	/// <summary>
	/// Jsub 配列のうち $.Sid が一致する要素の $.Cd / $.Mei を更新する。
	/// json_group_array + json_set で配列を作り直すため、`order by cast(J.key as integer)` で
	/// 元の要素順を保つ必要がある(省略すると並びが変わる)。実装形は RebuildDb.cs:52-79 を踏襲。
	/// </summary>
	int ExecuteJsubCodeNameRule(CascadeJsonRule rule, long id, string code, string name, long vdate) {
		var table = _db.GetTableName(rule.Target);
		var col = rule.JsonColumn;
		var sql = $@"
update {table} as S
   set {col} = ( select json_group_array(json(X.value2))
                   from ( select J.key,
                                 case when json_extract(J.value, '$.Sid') = @0
                                      then json_set(J.value, '$.Cd', @1, '$.Mei', @2)
                                      else J.value end as value2
                            from json_each(S.{col}) as J
                           order by cast(J.key as integer) ) as X ),
       Vdu = @3
 where {JsonArrayReady($"S.{col}")}
   and exists ( select 1 from json_each(S.{col}) as J
                 where json_extract(J.value, '$.Sid') = @0
                   and ( ifnull(json_extract(J.value, '$.Cd' ), '') <> @1
                      or ifnull(json_extract(J.value, '$.Mei'), '') <> @2 ) )";
		return _db.Execute(sql, [id, code, name, vdate]);
	}

	/// <summary>
	/// Jsub 配列を参照先マスタの現在値で再同期する(全件版)。
	/// Cd/Mei(=$.Sid の参照先)と Kbname(=Kubun='IDX' かつ Code=$.Kb の行のName)を1文で更新する。
	/// 別文に分けると同じテーブルを2回全走査することになるため。
	/// 参照先が見つからない要素は ifnull で現在値を残す。
	/// </summary>
	int ResyncJsub(CascadeJsonRule rule, long vdate) {
		var table = _db.GetTableName(rule.Target);
		var source = _db.GetTableName(rule.Source);
		var col = rule.JsonColumn;
		// M=名称行($.Sid で参照) K=区分定義行($.Kb で参照)
		var joins = $@"left join {source} M on M.Id = json_extract(J.value, '$.Sid')
                            left join {source} K on K.Kubun = '{MeishoKubunIndex}' and K.Code = json_extract(J.value, '$.Kb')";
		var sql = $@"
update {table} as S
   set {col} = ( select json_group_array(json(X.value2))
                   from ( select J.key,
                                 json_set(J.value,
                                   '$.Cd',     ifnull(M.Code, ifnull(json_extract(J.value, '$.Cd'    ), '')),
                                   '$.Mei',    ifnull(M.Name, ifnull(json_extract(J.value, '$.Mei'   ), '')),
                                   '$.Kbname', ifnull(K.Name, ifnull(json_extract(J.value, '$.Kbname'), ''))) as value2
                            from json_each(S.{col}) as J
                            {joins}
                           order by cast(J.key as integer) ) as X ),
       Vdu = @0
 where {JsonArrayReady($"S.{col}")}
   and exists ( select 1 from json_each(S.{col}) as J
                            {joins}
                where ( M.Id is not null
                        and ( ifnull(json_extract(J.value, '$.Cd' ), '') <> ifnull(M.Code,'')
                           or ifnull(json_extract(J.value, '$.Mei'), '') <> ifnull(M.Name,'') ) )
                   or ( K.Id is not null
                        and ifnull(json_extract(J.value, '$.Kbname'), '') <> ifnull(K.Name,'') ) )";
		return _db.Execute(sql, [vdate]);
	}

	// ============================================================
	// R3: 区分名 (MasterMeisho.KubunName と Jsub の Kbname)
	// ============================================================

	/// <summary>
	/// 区分定義行(Kubun='IDX')の Name 変更を、同区分を持つ行の KubunName と Jsub の Kbname へ伝播する。
	/// </summary>
	int ExecuteKubunNameRules(string newCode, string newName, string? kubun, string? oldCode, long vdate) {
		// 区分定義行(IDX)以外の改名は区分名に影響しない
		if (!string.Equals(kubun, MeishoKubunIndex, StringComparison.Ordinal))
			return 0;
		// 区分コード自体の変更は区分体系の変更であり、MasterMeisho.Kubun / MasterShohin.SizeKu /
		// MasterGeneralMeisho.Kb の参照先が失われる。伝播では解決できないため実行しない(§7-R6)
		if (oldCode != null && !string.Equals(oldCode, newCode, StringComparison.Ordinal)) {
			_logger.LogWarning("区分コード変更({OldCode}→{NewCode})は区分名の伝播対象外。Kubun/SizeKu/Kb の参照が壊れている可能性があるため確認が必要", oldCode, newCode);
			return 0;
		}
		var cnt = ExecuteMeishoKubunNameRule(newCode, newName, vdate);
		foreach (var rule in JsubRules)
			cnt += ExecuteJsubKbnameRule(rule, newCode, newName, vdate);
		return cnt;
	}

	/// <summary>
	/// MasterMeisho.KubunName(自己参照の非正規化)を更新する。
	/// Kubun='IDX' の行自身は対象外: 区分定義行の KubunName を IDX/IDX 行の Name で上書きすると
	/// 運用上意図しない値になるため(初期データでは IDX 行の KubunName='名称区分'、IDX/IDX 行の Name='名称区分インデックス')。
	/// </summary>
	int ExecuteMeishoKubunNameRule(string kubunCode, string kubunName, long vdate) {
		var table = _db.GetTableName(typeof(MasterMeisho));
		var sql = $@"
update {table}
   set KubunName = @1,
       Vdu = @2
 where Kubun = @0
   and Kubun <> '{MeishoKubunIndex}'
   and ifnull(KubunName,'') <> @1";
		return _db.Execute(sql, [kubunCode, kubunName, vdate]);
	}

	/// <summary>MasterMeisho.KubunName を全件再同期する</summary>
	int ResyncMeishoKubunName(long vdate) {
		var table = _db.GetTableName(typeof(MasterMeisho));
		var sql = $@"
update {table} as T
   set KubunName = ( select ifnull(M.Name,'') from {table} M
                      where M.Kubun = '{MeishoKubunIndex}' and M.Code = T.Kubun ),
       Vdu = @0
 where T.Kubun <> '{MeishoKubunIndex}'
   and exists ( select 1 from {table} M
                 where M.Kubun = '{MeishoKubunIndex}' and M.Code = T.Kubun
                   and ifnull(T.KubunName,'') <> ifnull(M.Name,'') )";
		return _db.Execute(sql, [vdate]);
	}

	/// <summary>Jsub 配列のうち $.Kb が一致する要素の $.Kbname を更新する</summary>
	int ExecuteJsubKbnameRule(CascadeJsonRule rule, string kubunCode, string kubunName, long vdate) {
		var table = _db.GetTableName(rule.Target);
		var col = rule.JsonColumn;
		var sql = $@"
update {table} as S
   set {col} = ( select json_group_array(json(X.value2))
                   from ( select J.key,
                                 case when json_extract(J.value, '$.Kb') = @0
                                      then json_set(J.value, '$.Kbname', @1)
                                      else J.value end as value2
                            from json_each(S.{col}) as J
                           order by cast(J.key as integer) ) as X ),
       Vdu = @2
 where {JsonArrayReady($"S.{col}")}
   and exists ( select 1 from json_each(S.{col}) as J
                 where json_extract(J.value, '$.Kb') = @0
                   and ifnull(json_extract(J.value, '$.Kbname'), '') <> @1 )";
		return _db.Execute(sql, [kubunCode, kubunName, vdate]);
	}

	// ============================================================
	// R4/R5: MasterShohin.Jcolsiz と DerivedShohinColSiz
	// ============================================================

	/// <summary>Jcolsiz の色/サイズ名称を更新し、更新があれば DerivedShohinColSiz を再構築する</summary>
	int ExecuteJcolsizRules(long id, string code, string name, long vdate) {
		// 影響を受ける商品Idを先に確定させる(更新後は差分条件で抽出できなくなるため)
		var shohinIds = FetchJcolsizTargetIds(id);
		var cnt = ExecuteJcolsizRule("Id_Col", "Code_Col", "Mei_Col", id, code, name, vdate);
		cnt += ExecuteJcolsizRule("Id_Siz", "Code_Siz", "Mei_Siz", id, code, name, vdate);
		if (cnt > 0)
			RebuildDerivedShohinColSiz(shohinIds);
		return cnt;
	}

	/// <summary>指定の名称Idを色またはサイズとして参照している商品Idを返す</summary>
	List<long> FetchJcolsizTargetIds(long id) {
		var table = _db.GetTableName(typeof(MasterShohin));
		var sql = $@"
select distinct S.Id from {table} S, json_each(S.Jcolsiz) J
 where {JsonArrayReady("S.Jcolsiz")}
   and ( json_extract(J.value, '$.Id_Col') = @0 or json_extract(J.value, '$.Id_Siz') = @0 )";
		return _db.Fetch<long>(sql, id);
	}

	/// <summary>Jcolsiz 配列のうち idPath が一致する要素の コード/名称 を更新する</summary>
	int ExecuteJcolsizRule(string idPath, string codePath, string meiPath, long id, string code, string name, long vdate) {
		var table = _db.GetTableName(typeof(MasterShohin));
		var sql = $@"
update {table} as S
   set Jcolsiz = ( select json_group_array(json(X.value2))
                     from ( select J.key,
                                   case when json_extract(J.value, '$.{idPath}') = @0
                                        then json_set(J.value, '$.{codePath}', @1, '$.{meiPath}', @2)
                                        else J.value end as value2
                              from json_each(S.Jcolsiz) as J
                             order by cast(J.key as integer) ) as X ),
       Vdu = @3
 where {JsonArrayReady("S.Jcolsiz")}
   and exists ( select 1 from json_each(S.Jcolsiz) as J
                 where json_extract(J.value, '$.{idPath}') = @0
                   and ( ifnull(json_extract(J.value, '$.{codePath}'), '') <> @1
                      or ifnull(json_extract(J.value, '$.{meiPath}' ), '') <> @2 ) )";
		return _db.Execute(sql, [id, code, name, vdate]);
	}

	/// <summary>
	/// Jcolsiz の色/サイズ名称を全件再同期し、対象商品の DerivedShohinColSiz を再構築する。
	/// 差分条件は json_each を伴い重いため、対象Idを1回だけ抽出して UPDATE は Id 指定で行う
	/// (UPDATE の WHERE でも同じ差分条件を書くと同じ全走査を2回することになる)。
	/// </summary>
	int ResyncJcolsiz(long vdate) {
		var table = _db.GetTableName(typeof(MasterShohin));
		var source = _db.GetTableName(typeof(MasterMeisho));
		// 更新対象の商品Idを1回だけ抽出する(UPDATEとDerived再構築の両方でこの集合を使う)
		var shohinIds = _db.Fetch<long>($@"
select S.Id from {table} S
 where {JsonArrayReady("S.Jcolsiz")}
   and exists ( select 1 from json_each(S.Jcolsiz) as J
                 left join {source} C on C.Id = json_extract(J.value, '$.Id_Col')
                 left join {source} Z on Z.Id = json_extract(J.value, '$.Id_Siz')
                where ( C.Id is not null
                        and ( ifnull(json_extract(J.value, '$.Code_Col'), '') <> ifnull(C.Code,'')
                           or ifnull(json_extract(J.value, '$.Mei_Col' ), '') <> ifnull(C.Name,'') ) )
                   or ( Z.Id is not null
                        and ( ifnull(json_extract(J.value, '$.Code_Siz'), '') <> ifnull(Z.Code,'')
                           or ifnull(json_extract(J.value, '$.Mei_Siz' ), '') <> ifnull(Z.Name,'') ) ) )");
		if (shohinIds.Count == 0)
			return 0;
		var cnt = 0;
		foreach (var chunk in shohinIds.Chunk(IdChunkSize)) {
			var sql = $@"
update {table} as S
   set Jcolsiz = ( select json_group_array(json(X.value2))
                     from ( select J.key,
                                   json_set(J.value,
                                     '$.Code_Col', ifnull(C.Code, ifnull(json_extract(J.value, '$.Code_Col'), '')),
                                     '$.Mei_Col',  ifnull(C.Name, ifnull(json_extract(J.value, '$.Mei_Col' ), '')),
                                     '$.Code_Siz', ifnull(Z.Code, ifnull(json_extract(J.value, '$.Code_Siz'), '')),
                                     '$.Mei_Siz',  ifnull(Z.Name, ifnull(json_extract(J.value, '$.Mei_Siz' ), ''))) as value2
                              from json_each(S.Jcolsiz) as J
                              left join {source} C on C.Id = json_extract(J.value, '$.Id_Col')
                              left join {source} Z on Z.Id = json_extract(J.value, '$.Id_Siz')
                             order by cast(J.key as integer) ) as X ),
       Vdu = @0
 where S.Id in ({string.Join(",", chunk)})";
			cnt += _db.Execute(sql, [vdate]);
		}
		RebuildDerivedShohinColSiz(shohinIds);
		return cnt;
	}

	/// <summary>IN句へ展開するIdの分割サイズ(SQL文が長くなりすぎるのを防ぐ)</summary>
	const int IdChunkSize = 500;

	/// <summary>
	/// DerivedShohinColSiz を MasterShohin.Jcolsiz から作り直す。
	/// 当テーブルは Jcolsiz からの完全導出(BaseDbDerived.cs の CreateSql がその定義)なので、
	/// 個別のUPDATEは書かず DerivedDb と同じ Delete→Insert で再構築する(導出定義の二重管理を避ける)。
	/// 1件ずつではなくIN句でまとめて実行する(商品数が多いとSQL往復回数が支配的になるため)。
	/// Idはすべて long のDB由来値なので、IN句へ直接展開してもインジェクションの余地はない。
	/// </summary>
	void RebuildDerivedShohinColSiz(List<long> shohinIds) {
		if (shohinIds.Count == 0)
			return;
		var derived = _db.GetTableName(typeof(DerivedShohinColSiz));
		foreach (var chunk in shohinIds.Chunk(IdChunkSize)) {
			var ids = string.Join(",", chunk);
			_db.Execute($"delete from {derived} where Id_Shohin in ({ids})");
			_db.Execute($"{DerivedShohinColSiz.CreateSql} where M.Id in ({ids})");
		}
	}

	/// <summary>
	/// json_each に渡す前提条件。null/空文字/不正JSONの行で json_each が例外を投げるのを防ぐ。
	/// </summary>
	static string JsonArrayReady(string column) => $"{column} is not null and json_valid({column})";

	/// <summary>
	/// json_extract に渡すための安全な列式を返す。
	/// SQLiteの json_extract は不正なJSON(ALTER TABLE の DEFAULT '' 直後の空文字など)に対して
	/// NULLではなく "malformed JSON" 例外を投げるため、json_valid で判定して '{}' に置き換える。
	/// CASE式はSQLiteで短絡評価が保証されるため、OR条件に並べるより確実。
	/// </summary>
	static string SafeJsonColumn(string column) => $"case when json_valid({column}) then {column} else '{{}}' end";

	/// <summary>
	/// 1テーブル分のV*列をまとめて全件再同期する(テーブルを1回だけ全走査する)。
	/// 外側の行はテーブル名で修飾し、内側の参照先には別名を付けることで
	/// 自己参照ルール(MasterTokui.VPaysaki→MasterTokui)でも参照が区別される。
	/// 参照先が存在しない行(Id_*=0 やdangling)は ifnull で現在値を残す(旧名称を温存する)。
	/// 戻り値は「更新された行数」であり、列ごとの件数の合計ではない点に注意。
	/// </summary>
	int ResyncVRulesByTable(Type target, CascadeVRule[] rules, long vdate) {
		var table = _db.GetTableName(target);
		var sets = new List<string>(rules.Length);
		var diffs = new List<string>(rules.Length);
		for (var i = 0; i < rules.Length; i++) {
			var rule = rules[i];
			var source = _db.GetTableName(rule.Source);
			var alias = $"S{i}"; // 同一テーブルを複数回参照するため別名を分ける
			var col = SafeJsonColumn($"{table}.{rule.VColumn}");
			sets.Add($@"{rule.VColumn} = ifnull( ( select json_object('Sid', {alias}.Id, 'Cd', ifnull({alias}.Code,''), 'Mei', ifnull({alias}.Name,''))
                            from {source} {alias} where {alias}.Id = {table}.{rule.IdColumn} ), {table}.{rule.VColumn} )");
			diffs.Add($@"exists ( select 1 from {source} {alias}
                 where {alias}.Id = {table}.{rule.IdColumn}
                   and ( ifnull(json_extract({col}, '$.Cd' ), '') <> ifnull({alias}.Code,'')
                      or ifnull(json_extract({col}, '$.Mei'), '') <> ifnull({alias}.Name,'')
                      or ifnull(json_extract({col}, '$.Sid'),  0) <> {alias}.Id ) )");
		}
		var sql = $@"
update {table}
   set {string.Join(",\r\n       ", sets)},
       Vdu = @0
 where {string.Join("\r\n    or ", diffs)}";
		return _db.Execute(sql, [vdate]);
	}
}
