using CvBase;
using CvBase.Share;
using CvDomainLogic;

namespace CvServer.Services;

/// <summary>
/// 副作用を起動する書き込み操作の種別。
/// </summary>
public enum WriteOp {
	/// <summary>追加</summary>
	Insert,
	/// <summary>更新</summary>
	Update,
	/// <summary>削除</summary>
	Delete
}

/// <summary>
/// 副作用の実行行数。呼び出し元がログへ出すために返す(ログはCvServerの責務)。
/// </summary>
/// <param name="Stock">在庫集計(SummaryStock / SummaryRealStock)の更新行数</param>
/// <param name="Reserve">引当数の更新行数</param>
/// <param name="Derived">派生テーブルの展開行数</param>
/// <param name="Cascade">V*列伝播の更新行数</param>
public readonly record struct WriteEffectResult(int Stock, int Reserve, int Derived, int Cascade) {
	/// <summary>副作用なし</summary>
	public static WriteEffectResult Empty => default;

	/// <summary>複数回の実行結果を足し合わせる(一括登録用)</summary>
	public WriteEffectResult Add(WriteEffectResult other) =>
		new(Stock + other.Stock, Reserve + other.Reserve, Derived + other.Derived, Cascade + other.Cascade);

	/// <summary>ログ用の要約。すべて0なら空文字</summary>
	public override string ToString() =>
		this == default ? string.Empty : $"在庫={Stock} 引当={Reserve} 派生={Derived} V*伝播={Cascade}";
}

/// <summary>
/// テーブルの更新に伴う副作用の起動順序を1箇所に集約する。
/// <para>
/// 責務の分担は次のとおり。
/// 「どのテーブルがどの副作用を持つか」の宣言は CvBase のマーカーインターフェース
/// (<see cref="ITranSoko"/> / <see cref="ITranIdo"/> / <see cref="ITranReserve"/> / <see cref="IDerivedOrigin"/>)、
/// 個々の計算は CvDomainLogic (<see cref="SummaryDb"/> / <see cref="MasterCascadeDb"/> / <see cref="DerivedDb"/>)、
/// 起動順序とトランザクション・楽観排他・ログは CvServer が持つ。
/// このクラスは「順序」だけを担当する。
/// </para>
/// <para>
/// トランザクションは呼び出し元(<c>CoreService</c>)が張る前提で、ここでは張らない。
/// </para>
/// </summary>
public sealed class WriteEffectRunner(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private readonly SummaryDb _summaryDb = new(db);
	private readonly DerivedDb _derivedDb = new(db);

	/// <summary>
	/// <see cref="PartialUpdateParam.Columns"/> へ指定できない列。
	/// <para>
	/// <c>Id</c> / <c>Vdc</c> / <c>Vdu</c> はサーバーが管理する。
	/// それ以外は部分更新が <see cref="Before"/> / <see cref="After"/> を通らないため禁止する。
	/// 在庫系は <see cref="SummaryDb.CalcTran2SummaryStock"/> が読む倉庫・SKU・数量・区分・日付、
	/// 掛系は売掛買掛集計が読む金額・税・掛計上日、マスタ系は <see cref="MasterCascadeDb"/> のV*列伝播対象。
	/// これらを変えるときは行全体を <see cref="UpdateParam"/> で保存させる。
	/// </para>
	/// <para>
	/// 副作用を持つ列を増やしたときにここも直せるよう、副作用の実装と同じファイルに置いている。
	/// </para>
	/// </summary>
	public static readonly string[] PartialUpdateDeniedColumns = [
		nameof(BaseDbClass.Id), nameof(BaseDbClass.Vdc), nameof(BaseDbClass.Vdu),
		"Id_Soko", "Id_Ido", "Id_Shohin", "Id_Col", "Id_Siz", "Su", "CalcFlag", "Kubun",
		"DenDay", "KakeDay", "KingakuTotal", "Tax", "Total", "Jmeisai", "Code", "Name",
	];

	/// <summary>
	/// 更新・削除の「前」に在庫を反転して打ち消す。
	/// <para>
	/// <see cref="SummaryDb.CalcTran2SummaryStock"/> は差分の加減算で、対象行をDBから読んで計算する。
	/// 更新・削除でDB上の行が変わる前に旧値ぶんを反転しておかないと打ち消せないため、必ず実行前に呼ぶ。
	/// 追加(<see cref="WriteOp.Insert"/>)には打ち消す旧値が無いので何もしない。
	/// </para>
	/// </summary>
	/// <param name="op">書き込み操作の種別</param>
	/// <param name="itemType">対象テーブルの型</param>
	/// <param name="org">更新・削除の対象となるDB上の行</param>
	/// <returns>在庫集計の更新行数</returns>
	public int Before(WriteOp op, Type itemType, object org) {
		if (op == WriteOp.Insert || org is not BaseDbClass row) {
			return 0;
		}
		return CalcStock(itemType, row.Id, invertFlag: true);
	}

	/// <summary>
	/// 追加・更新・削除の「後」に、派生展開・在庫加算・V*列伝播・引当再計算を行う。
	/// <para>
	/// <paramref name="reserveKeys"/> を渡した場合は引当の再計算を行わずキーを貯めるだけにする。
	/// 一括登録で行数ぶん再計算が走るのを避けるためで、貯めたキーは <see cref="FlushReserve"/> でまとめて処理する。
	/// </para>
	/// </summary>
	/// <param name="op">書き込み操作の種別</param>
	/// <param name="itemType">対象テーブルの型</param>
	/// <param name="item">追加・更新した行。削除では削除した行</param>
	/// <param name="org">更新前のDB上の行。追加では null</param>
	/// <param name="vdate">更新日時。V*列伝播へ渡す(別採番すると楽観排他が誤作動する)</param>
	/// <param name="reserveKeys">引当キーの収集先。null なら即時に再計算する</param>
	public WriteEffectResult After(WriteOp op, Type itemType, object item, object? org, long vdate,
		HashSet<ReserveKey>? reserveKeys = null) {
		if (item is not BaseDbClass row) {
			return WriteEffectResult.Empty;
		}
		var stock = 0;
		var derived = 0;
		var cascade = 0;

		// マスタのCode/Name変更を参照側のV*列へ伝播する(Master系のみ。Tran系のV*列は伝票の時点名称なので対象外)
		// vdate は必ず渡す: 自己参照(MasterTokui.Id_Paysakiが自分自身など)で更新元の行自身が伝播対象になり、
		// 別採番するとクライアントへ返す Vdu とDB上の Vdu がずれて次回保存が楽観排他で弾かれる
		if (op == WriteOp.Update && item is IBaseCodeName codeName && MasterCascadeDb.NeedsCascade(itemType, item, org)) {
			cascade = new MasterCascadeDb(_db).CascadeFromMaster(itemType, row.Id, codeName.Code, codeName.Name, vdate,
				(item as MasterMeisho)?.Kubun, (org as IBaseCodeName)?.Code);
		}

		// 派生テーブルの展開。元テーブルと同じ操作を派生側へ反映する
		derived = op switch {
			WriteOp.Insert => _derivedDb.Insert(item, row.Id),
			WriteOp.Update => _derivedDb.Update(item, row.Id),
			WriteOp.Delete => _derivedDb.Delete(item, row.Id),
			_ => 0
		};

		// 在庫の加算。削除は Before の反転だけで完結するので後処理は無い
		if (op != WriteOp.Delete) {
			stock = CalcStock(itemType, row.Id, invertFlag: false);
		}

		// 引当数はキー単位の引き直しなので、倉庫・SKU・日付が変わった場合に備えて修正前後の両方を対象にする。
		// 削除では削除後の TranHaibun から引き直すため、削除された行のキーを渡す
		var keys = new HashSet<ReserveKey>();
		if (item is ITranReserve newReserve) {
			keys.Add(ReserveKey.From(newReserve));
		}
		if (org is ITranReserve orgReserve) {
			keys.Add(ReserveKey.From(orgReserve));
		}
		if (keys.Count == 0) {
			return new WriteEffectResult(stock, 0, derived, cascade);
		}
		if (reserveKeys != null) {
			reserveKeys.UnionWith(keys);
			return new WriteEffectResult(stock, 0, derived, cascade);
		}
		return new WriteEffectResult(stock, _summaryDb.CalcHaibun2Reserve(keys), derived, cascade);
	}

	/// <summary>
	/// <see cref="After"/> で貯めた引当キーをまとめて再計算する。キーが空なら何もしない。
	/// </summary>
	/// <returns>引当数の更新行数</returns>
	public int FlushReserve(HashSet<ReserveKey> keys) =>
		keys.Count == 0 ? 0 : _summaryDb.CalcHaibun2Reserve(keys);

	/// <summary>
	/// 部分更新で引当に影響する列(<see cref="ITranReserve.EndFlag"/>)が変わったときに引当を引き直す。
	/// <para>
	/// キー列(倉庫・SKU・<c>DenDay</c>・<c>Su</c>)は <see cref="PartialUpdateDeniedColumns"/> で部分更新できないので、
	/// 更新後の行をIdで読み直したキーだけで足りる。
	/// </para>
	/// </summary>
	/// <param name="itemType">対象テーブルの型</param>
	/// <param name="columns">実際に更新した列名</param>
	/// <param name="ids">更新した行のId。検証済み(0より大きいlong)であること</param>
	/// <returns>引当数の更新行数</returns>
	public int AfterPartialUpdate(Type itemType, IReadOnlyCollection<string> columns, IReadOnlyCollection<long> ids) {
		if (ids.Count == 0
			|| !typeof(ITranReserve).IsAssignableFrom(itemType)
			|| !columns.Contains(nameof(ITranReserve.EndFlag), StringComparer.OrdinalIgnoreCase)) {
			return 0;
		}
		// Idは検証済みなのでSQLへ直接埋め込んでよい
		var keys = _db.Fetch(itemType, $"where Id in ({string.Join(",", ids)})")
			.OfType<ITranReserve>()
			.Select(ReserveKey.From)
			.ToHashSet();
		return _summaryDb.CalcHaibun2Reserve(keys);
	}

	/// <summary>
	/// 在庫集計を更新する。移動系(<see cref="ITranIdo"/>)は倉庫軸と移動先軸の2回ぶん計算する。
	/// </summary>
	private int CalcStock(Type itemType, long id, bool invertFlag) {
		if (!typeof(ITranSoko).IsAssignableFrom(itemType)) {
			return 0;
		}
		var cnt = _summaryDb.CalcTran2SummaryStock(itemType.Name, nameof(ITranSoko.Id_Soko), id, invertFlag);
		if (typeof(ITranIdo).IsAssignableFrom(itemType)) {
			cnt += _summaryDb.CalcTran2SummaryStock(itemType.Name, nameof(ITranIdo.Id_Ido), id, invertFlag);
		}
		return cnt;
	}
}
