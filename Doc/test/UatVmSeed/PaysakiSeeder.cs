using System.Data;
using CvAsset;
using CvBase;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// E7（親子締日ワーニング）を請求計算画面から検証するためのデータを投入する。
/// </summary>
/// <remarks>
/// <para>
/// E7は「子（得意先）に請求先(`Id_Paysaki`)があり、親の締日が子と違う」ときに出る非ブロック警告である。
/// 開発DBは`MasterTokui`の`Id_Paysaki`が全件0のため、そのままでは発火しない。
/// </para>
/// <para>
/// 一致・不一致の両方を1回のシード（＝サーバー起動前の1回の書き込み）で用意するため、
/// **子と親を2組**作る。実行途中でDBを書き換えずに、コード範囲を変えるだけで
/// 「警告が出る」「出ない」を切り替えられる。
/// </para>
/// <list type="bullet">
/// <item>不一致の組: 子 <c>UATVM-C20</c>(締日20) → 親 <c>UATVM-P99</c>(締日99) … 警告が出る</item>
/// <item>一致の組: 子 <c>UATVM-C20M</c>(締日20) → 親 <c>UATVM-P20</c>(締日20) … 警告が出ない</item>
/// </list>
/// <para>
/// いずれもUAT専用に追加した得意先で、既存の実マスタの`Id_Paysaki`や締日には触らない。
/// </para>
/// </remarks>
public static class PaysakiSeeder {
	/// <summary>締日が親と食い違う子。E7が出るはず。</summary>
	public const string MismatchChildCode = "UATVM-C20";
	/// <summary>締日が親と一致する子。E7が出ないはず。</summary>
	public const string MatchChildCode = "UATVM-C20M";
	/// <summary>不一致側の親（請求先）。</summary>
	public const string MismatchParentCode = "UATVM-P99";
	/// <summary>一致側の親（請求先）。</summary>
	public const string MatchParentCode = "UATVM-P20";
	/// <summary>子の締日。請求計算画面で選ぶ締日でもある。</summary>
	public const int ChildShime = 20;

	/// <summary>投入結果。</summary>
	public sealed record Result(
		string MismatchChildCode, string MismatchParentCode, int MismatchParentShime,
		string MatchChildCode, string MatchParentCode, int MatchParentShime,
		int ChildShime);

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

		var mismatchParent = EnsureTokui(db, MismatchParentCode, "UAT-VM 請求先(締日99)", 99, trace);
		var matchParent = EnsureTokui(db, MatchParentCode, "UAT-VM 請求先(締日20)", ChildShime, trace);
		var mismatchChild = EnsureTokui(db, MismatchChildCode, "UAT-VM 得意先(親と締日不一致)", ChildShime, trace);
		var matchChild = EnsureTokui(db, MatchChildCode, "UAT-VM 得意先(親と締日一致)", ChildShime, trace);

		LinkPaysaki(db, mismatchChild, mismatchParent, trace);
		LinkPaysaki(db, matchChild, matchParent, trace);

		return new Result(
			MismatchChildCode, MismatchParentCode, 99,
			MatchChildCode, MatchParentCode, ChildShime,
			ChildShime);
	}

	/// <summary>得意先を用意する。既にあれば締日だけ揃える。</summary>
	private static MasterTokui EnsureTokui(ExDatabaseSqlite db, string code, string name, int shime, Action<string> trace) {
		var table = db.GetTableName(typeof(MasterTokui));
		var existing = db.Fetch<MasterTokui>("where Code=@0", code).FirstOrDefault();
		if (existing != null) {
			if (existing.Shime1 != shime) {
				db.Execute($"UPDATE {table} SET Shime1=@0 WHERE Id=@1", shime, existing.Id);
				existing.Shime1 = shime;
				trace($"得意先 {code} の締日を{shime}へ更新 Id={existing.Id}");
			}
			else {
				trace($"得意先 {code} は既に存在 Id={existing.Id} 締日={shime}");
			}
			return existing;
		}

		var employee = db.Fetch<MasterShain>("order by Id").First();
		var tokui = new MasterTokui {
			Code = code,
			Name = name,
			Ryaku = code,
			Shime1 = shime,
			PayMonth = 1,
			PayDay = 99,
			Id_Shain = employee.Id,
			VShain = new CodeNameView(employee.Id, employee.Code, employee.Name),
		};
		var vdate = Common.GetVdate();
		tokui.Vdc = vdate;
		tokui.Vdu = vdate;
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			db.Insert(tokui);
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		trace($"得意先 {code} を追加 Id={tokui.Id} 締日={shime}");
		return tokui;
	}

	/// <summary>子へ請求先（親）を設定する。</summary>
	private static void LinkPaysaki(ExDatabaseSqlite db, MasterTokui child, MasterTokui parent, Action<string> trace) {
		var table = db.GetTableName(typeof(MasterTokui));
		if (child.Id_Paysaki == parent.Id) {
			trace($"{child.Code} の請求先は既に {parent.Code}");
			return;
		}
		db.Execute($"UPDATE {table} SET Id_Paysaki=@0 WHERE Id=@1", parent.Id, child.Id);
		trace($"{child.Code}(締日{child.Shime1}) の請求先を {parent.Code}(締日{parent.Shime1}) に設定");
	}
}
