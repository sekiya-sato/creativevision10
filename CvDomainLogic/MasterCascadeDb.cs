using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

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
	/// <returns>更新された行数の合計</returns>
	public int CascadeFromMaster(Type sourceType, long id, string newCode, string newName, long vdate = 0) {
		if (!IsCascadeSource(sourceType) || id <= 0)
			return 0;
		if (vdate == 0)
			vdate = Common.GetVdate();
		var cnt = 0;
		foreach (var rule in VRules) {
			if (rule.Source != sourceType)
				continue;
			cnt += ExecuteVRule(rule, id, newCode ?? string.Empty, newName ?? string.Empty, vdate);
		}
		// ToDo(Phase4): Jsub / Jcolsiz / Kbname / KubunName のJSON内スナップショット伝播をここに追加する
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
		foreach (var rule in VRules) {
			try {
				var one = ResyncVRule(rule, vdate);
				cnt += one;
				if (one > 0)
					_logger.LogInformation("V*列再同期 {Table}.{Column} 更新行数={Count}", rule.Target.Name, rule.VColumn, one);
			}
			catch (Exception ex) {
				errors.Add($"{rule.Target.Name}.{rule.VColumn}: {ex.Message}");
				_logger.LogError(ex, "V*列再同期に失敗 {Table}.{Column}", rule.Target.Name, rule.VColumn);
			}
		}
		// ToDo(Phase4): JSON内スナップショット(Jsub / Jcolsiz / Kbname / KubunName)の再同期をここに追加する
		return cnt;
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

	/// <summary>
	/// json_extract に渡すための安全な列式を返す。
	/// SQLiteの json_extract は不正なJSON(ALTER TABLE の DEFAULT '' 直後の空文字など)に対して
	/// NULLではなく "malformed JSON" 例外を投げるため、json_valid で判定して '{}' に置き換える。
	/// CASE式はSQLiteで短絡評価が保証されるため、OR条件に並べるより確実。
	/// </summary>
	static string SafeJsonColumn(string column) => $"case when json_valid({column}) then {column} else '{{}}' end";

	/// <summary>
	/// 1ルール分の全件再同期SQLを実行する。
	/// SQLiteのUPDATEは対象テーブルに別名を付けられないため、外側の行はテーブル名で修飾する
	/// (自己参照ルールでは内側に別名Sを付けることで参照が区別される)。
	/// 参照先が存在しない行は更新しない(旧名称を温存する)。
	/// </summary>
	int ResyncVRule(CascadeVRule rule, long vdate) {
		var table = _db.GetTableName(rule.Target);
		var source = _db.GetTableName(rule.Source);
		var col = SafeJsonColumn($"{table}.{rule.VColumn}");
		var sql = $@"
update {table}
   set {rule.VColumn} = ( select json_object('Sid', ifnull(S.Id,0), 'Cd', ifnull(S.Code,''), 'Mei', ifnull(S.Name,''))
                            from {source} S where S.Id = {table}.{rule.IdColumn} ),
       Vdu = @0
 where {table}.{rule.IdColumn} > 0
   and exists ( select 1 from {source} S
                 where S.Id = {table}.{rule.IdColumn}
                   and ( ifnull(json_extract({col}, '$.Cd' ), '') <> ifnull(S.Code,'')
                      or ifnull(json_extract({col}, '$.Mei'), '') <> ifnull(S.Name,'')
                      or ifnull(json_extract({col}, '$.Sid'),  0) <> S.Id ) )";
		return _db.Execute(sql, [vdate]);
	}
}
