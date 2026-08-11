using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 上代（販売価格）の解決と、<see cref="DerivedJodai"/> の再展開を担うクラス。
/// <para>
/// 商品マスタ(<see cref="MasterShohin"/>.TankaJodai)は定価として維持し、
/// 上代一括変更(<see cref="TranJodai"/>)は期間・対象つきのオーバーレイとして <see cref="DerivedJodai"/> に積む。
/// 該当行が無ければ従来どおりマスタの上代を返すので、<b>本クラスを経由しても既存の動作は変わらない</b>。
/// </para>
/// <para>
/// 通常の展開・取消は <see cref="TranJodai"/> が <c>IDerivedOrigin</c> を実装しているため
/// CvServer/Services/HandlerDerived が Insert/Update/Delete 時に自動実行する。
/// 本クラスの <see cref="Rebuild"/> / <see cref="RebuildAll"/> は取りこぼしの修復用。
/// </para>
/// <para>SQLite 3.46+ 前提（json_each / json_valid）。設計は `.omo/20260811_jodai_table_design_plan.md`。</para>
/// </summary>
public class JodaiDb {
	readonly ExDatabase _db;
	readonly ILogger<JodaiDb> _logger;

	public JodaiDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<JodaiDb>();
	}

	/// <summary>
	/// 指定日・指定対象における商品の上代を解決する。
	/// </summary>
	/// <param name="idShohin">商品Id</param>
	/// <param name="taisho">対象系統。店舗売上/POSは <see cref="EnumJodaiTaisho.Tenpo"/>、本部売上/受注と倉庫軸は <see cref="EnumJodaiTaisho.Honbu"/></param>
	/// <param name="idTenpo">店舗Id(TenType=6) または 得意先Id(TenType=1,3)。倉庫軸など対象が特定できない場合は 0（全件行のみ適用）</param>
	/// <param name="day">判定日 yyyyMMdd</param>
	/// <returns>適用上代。該当が無ければ商品マスタの上代</returns>
	public int ResolveJodai(long idShohin, EnumJodaiTaisho taisho, long idTenpo, string day) {
		var sql = @$"
SELECT {DerivedJodai.FinalJodaiSql("@0", "@1", "@2", "@3", "sh")}
  FROM {nameof(MasterShohin)} sh WHERE sh.Id = @0";
		return _db.FirstOrDefault<int>(sql, idShohin, (int)taisho, idTenpo, day);
	}

	/// <summary>
	/// 複数商品の上代をまとめて解決する（明細行ごとに1クエリ流すのを避けるため）。
	/// </summary>
	/// <returns>商品Id → 適用上代。<paramref name="idShohinList"/> に含まれる商品はすべて返る</returns>
	public Dictionary<long, int> ResolveJodaiList(IEnumerable<long> idShohinList, EnumJodaiTaisho taisho, long idTenpo, string day) {
		var ids = idShohinList.Distinct().ToList();
		if (ids.Count == 0)
			return [];
		var sql = @$"
SELECT sh.Id AS Id, {DerivedJodai.FinalJodaiSql("sh.Id", "@0", "@1", "@2", "sh")} AS Jodai
  FROM {nameof(MasterShohin)} sh
 WHERE sh.Id IN ({string.Join(",", ids)})";
		var rows = _db.Fetch<JodaiResolved>(sql, (int)taisho, idTenpo, day);
		return rows.ToDictionary(c => c.Id, c => c.Jodai);
	}

	/// <summary>
	/// 1伝票分の <see cref="DerivedJodai"/> を作り直す（削除→再展開）。
	/// <para>確定(<see cref="TranJodai.Status"/>=1)以外の伝票では削除だけが行われ、展開は0件になる。</para>
	/// </summary>
	/// <returns>展開した行数</returns>
	public int Rebuild(long idTran) {
		_db.Execute(DerivedJodai.DeleteSql, idTran);
		_db.Execute(DerivedJodai.InsertSql, idTran);
		var cnt = _db.FirstOrDefault<int>($"SELECT count(*) FROM {nameof(DerivedJodai)} WHERE Id_Tran = @0", idTran);
		_db.Execute($"UPDATE {nameof(TranJodai)} SET ExpandCnt = @1 WHERE Id = @0", idTran, cnt);
		_logger.LogInformation("DerivedJodai 再展開 伝票Id={IdTran} 展開行数={Count}", idTran, cnt);
		return cnt;
	}

	/// <summary>
	/// 全伝票の <see cref="DerivedJodai"/> を作り直す（修復用）。
	/// </summary>
	/// <returns>展開した行数の合計</returns>
	public int RebuildAll() {
		var ids = _db.Fetch<long>($"SELECT Id FROM {nameof(TranJodai)} ORDER BY Id");
		var total = 0;
		foreach (var id in ids)
			total += Rebuild(id);
		_logger.LogInformation("DerivedJodai 全再展開 伝票数={TranCount} 展開行数={Count}", ids.Count, total);
		return total;
	}

	/// <summary>
	/// 期限切れの適用上代を削除する。伝票は残るので必要なら <see cref="Rebuild"/> で復元できる。
	/// </summary>
	/// <param name="dayLimit">この日(yyyyMMdd)より前に終了した行を削除する</param>
	/// <returns>削除した行数</returns>
	public int PurgeExpired(string dayLimit) {
		var cnt = _db.Execute($"DELETE FROM {nameof(DerivedJodai)} WHERE DayTo < @0", dayLimit);
		_logger.LogInformation("DerivedJodai 期限切れ削除 基準日={DayLimit} 削除行数={Count}", dayLimit, cnt);
		return cnt;
	}

	/// <summary>
	/// <see cref="ResolveJodaiList"/> の受け取り用
	/// </summary>
	public class JodaiResolved {
		public long Id { get; set; }
		public int Jodai { get; set; }
	}
}
