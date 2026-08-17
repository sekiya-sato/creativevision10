using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace CvBase;

public interface IJsonPayload {
	Type ItemType { get; }
	string Item { get; }
	object GetItemObject();
}


public sealed record class QueryOneParam {
	public string? Where { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public string AddWhere() {
		var retstr =
			(!string.IsNullOrWhiteSpace(Where) ? $" where {Where}" : string.Empty);
		return retstr;
	}
	public QueryOneParam(Type itemType, string? where = null, string[]? parameters = null) {
		Where = where;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
	}
}
public sealed record class QueryByIdParam {
	public long Id { get; }
	public Type ItemType { get; }
	/// <summary>
	/// 一覧取得時点の更新日時。0の場合は従来どおり更新日時を照合しない。
	/// </summary>
	public long ExpectedVdu { get; }

	public QueryByIdParam(Type itemType, long id, long expectedVdu = 0) {
		Id = id;
		ItemType = itemType;
		ExpectedVdu = expectedVdu;
	}
}

public record class QueryListParam {
	public string? Where { get; }
	public string? Order { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public int? MaxCount { get; }
	public string AddWhereOrder() {
		var retstr =
			(!string.IsNullOrWhiteSpace(Where) ? $" where {Where}" : string.Empty) +
		(!string.IsNullOrWhiteSpace(Order) ? $" order by {Order}" : string.Empty) +
		(MaxCount.HasValue && MaxCount.Value > 0 ? $" limit {MaxCount.Value}" : string.Empty);
		return retstr;
	}
	public QueryListParam(Type itemType, string? where = null, string? order = null, string[]? parameters = null, int? maxCount = null) {
		Where = where;
		Order = order;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
		MaxCount = maxCount;
	}
}
public sealed record class QueryListSimpleParam : QueryListParam {
	public QueryListSimpleParam(Type itemType, string? where = null, string? order = null, string[]? parameters = null, int? maxCount = null)
		: base(itemType, where, order, parameters, maxCount) {
	}
}


public sealed record class QueryListSqlParam {
	public string? Sql { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public QueryListSqlParam(Type itemType, string? sql = null, string[]? parameters = null) {
		Sql = sql;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
	}
}

/// <summary>
/// クエリI/F : Item指定挿入パラメータ
/// </summary>

public sealed class InsertParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public InsertParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定挿入パラメータ
/// </summary>

public sealed class InsertBulkParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public InsertBulkParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定修正パラメータ
/// </summary>
public sealed class UpdateParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public UpdateParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定削除パラメータ
/// </summary>
public sealed class DeleteParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public DeleteParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : ID指定削除パラメータ
/// </summary>
public sealed class DeleteByIdParam {
	public long Id { get; }
	public Type ItemType { get; }
	public long OriginalVdu { get; }
	public DeleteByIdParam(Type itemType, long id, long originalVdu) {
		Id = id;
		ItemType = itemType;
		OriginalVdu = originalVdu;
	}
}
/// <summary>
/// クエリI/F : 指定した列だけを更新するパラメータ
/// <para>
/// <see cref="UpdateParam"/> は行全体を置き換えるため、<see cref="ITranSoko"/> 実装型では
/// 1件ごとに在庫再集計(旧値反転＋新値加算)が走る。フラグ列のように在庫・掛集計へ影響しない列を
/// 多件数まとめて更新する用途では、この型で対象列だけを更新する。
/// </para>
/// <para>
/// <c>Vdu</c> はサーバー側で採番して更新する。<c>Id</c> / <c>Vdc</c> / <c>Vdu</c> は
/// <see cref="Columns"/> へ指定できない。付随処理(在庫再集計、V*列伝播、Derived更新)は実行しないため、
/// サーバー側でそれらに影響する列を拒否する。
/// </para>
/// <para>
/// 楽観排他は <see cref="UpdateParam"/> と同じ考え方で行単位に行う。
/// <see cref="PartialUpdateRow.ExpectedVdu"/> が現在値と一致しない行が1件でもあれば、
/// サーバーはトランザクション全体を戻して <c>CvMsgErrorCode.ConcurrentUpdate</c> を返す(部分適用しない)。
/// </para>
/// </summary>
/// <param name="ItemType">対象テーブル型</param>
/// <param name="Columns">更新する列名の配列</param>
/// <param name="Rows">更新対象行の配列</param>
public sealed record class PartialUpdateParam(Type ItemType, string[] Columns, PartialUpdateRow[] Rows);

/// <summary>
/// 部分更新の1行分
/// </summary>
/// <param name="Id">対象行のId</param>
/// <param name="ExpectedVdu">
/// 一覧取得時点の <c>Vdu</c>。サーバー側で現在値と照合し、不一致(または行が削除済み)なら
/// 更新全体をrollbackする。
/// </param>
/// <param name="Values">
/// <see cref="PartialUpdateParam.Columns"/> と同順・同数の値。
/// 他のクエリパラメータと同様に文字列で渡し、SQLiteの列アフィニティで変換させる。
/// </param>
public sealed record class PartialUpdateRow(long Id, long ExpectedVdu, string[] Values);

/// <summary>
/// 部分更新の結果
/// </summary>
/// <param name="UpdatedCount">実際に更新された行数</param>
public sealed record class PartialUpdateResult(int UpdatedCount);

/// <summary>
/// データ出力I/F : HHTマスタデータ作成パラメータ
/// </summary>
public sealed record OutDataHhtMasterParam {
	public bool IsFixedLengthFormat { get; set; }
	public int ReservedInt { get; set; }
	public OutDataHhtMasterParam(bool isFixedLengthFormat, int reservedInt) {
		IsFixedLengthFormat = isFixedLengthFormat;
		ReservedInt = reservedInt;
	}
}

/// <summary>
/// 計算する際の期間指定パラメータ
/// </summary>
/// <param name="DateYymmFrom"></param>
/// <param name="DateYymmTo"></param>
public record CalcDateTermParameter(string DateYymmFrom, string DateYymmTo);
/// <summary>
/// 計算する際の年月指定パラメータ
/// </summary>
/// <param name="DateYymm"></param>
public record CalcDateParameter(string DateYymm);

/// <summary>
/// 棚卸開始処理・棚卸確定処理のパラメータ
/// <para>
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 8.1 / 8.4 を参照する。
/// </para>
/// </summary>
/// <param name="TanaMonth">棚卸年月 yyyyMM</param>
/// <param name="DenDay">確定処理が作る在庫調整伝票の在庫計上日 yyyyMMdd。開始処理では使わない</param>
/// <param name="IdShain">入力社員Id。0 なら未設定</param>
/// <param name="SokoIds">対象倉庫Id。空なら全倉庫を対象にする</param>
public record StocktakeParameter(string TanaMonth, string DenDay, long IdShain, long[] SokoIds);

/// <summary>
/// クエリI/F : CSV出力パラメータ (Sql出力パラメータはQueryListSqlParamを使う)
/// </summary>
public sealed record PrintByCsvParam(string CsvData);

/// <summary>
/// ConvertDbのストリーミング処理パラメータ
/// </summary>
/// <param name="IsInit"></param>
public sealed record ConvertDbParam(bool IsInit);
/// <summary>
/// ConvertDbのストリーミング処理パラメータ
/// </summary>
/// <param name="IsInit"></param>
/// <param name="SelectedTask"></param>
public sealed record ConvertSelectedDbParam(bool IsInit, List<string> SelectedTask);
