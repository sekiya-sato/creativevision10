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
/// クエリI/F : ID指定の一括削除パラメータ。<see cref="DeleteByIdParam"/> の多件数版。
/// <para>
/// 洗い替え登録（既存行を全部消してから入れ直す）で <see cref="DeleteByIdParam"/> を行数ぶん
/// 往復させると、通信回数が行数に比例し、途中で失敗すると<b>一部だけ消えた状態</b>が残る。
/// この型は1往復・1トランザクションで消し、付随処理（在庫再集計・引当再計算）も
/// <see cref="InsertBulkParam"/> と同じくまとめて1回だけ走る。
/// </para>
/// <para>
/// 楽観排他は <see cref="DeleteByIdParam"/> と同じ行単位。<see cref="DeleteBulkRow.ExpectedVdu"/> が
/// 現在値と一致しない行（または既に削除済みの行）が1件でもあれば、サーバーは<b>何も削除せず</b>
/// <c>CvMsgErrorCode.ConcurrentUpdate</c> を返す（部分適用しない）。
/// </para>
/// </summary>
/// <param name="ItemType">対象テーブル型</param>
/// <param name="Rows">削除対象行の配列。空なら削除0件で成功にする</param>
public sealed record class DeleteBulkParam(Type ItemType, DeleteBulkRow[] Rows);

/// <summary>
/// 一括削除の1行分
/// </summary>
/// <param name="Id">削除する行のId</param>
/// <param name="ExpectedVdu">
/// 一覧取得時点の <c>Vdu</c>。サーバー側で現在値と照合し、不一致（または行が削除済み）なら
/// 削除全体をrollbackする。
/// </param>
public sealed record class DeleteBulkRow(long Id, long ExpectedVdu);

/// <summary>
/// 一括削除の結果
/// </summary>
/// <param name="DeletedCount">実際に削除された行数</param>
public sealed record class DeleteBulkResult(int DeletedCount);

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
/// 請求・支払計算のパラメータ
/// </summary>
/// <param name="BillingYyyymm">請求・支払月 yyyyMM</param>
/// <param name="Shime">締日（1から31または99）</param>
/// <param name="TorisakiCodeFrom">得意先・仕入先コードの開始。空なら下限なし</param>
/// <param name="TorisakiCodeTo">得意先・仕入先コードの終了。空なら上限なし</param>
/// <param name="IsReissue">請求書を明示的に再発行する場合だけtrue。支払計算では常にfalse</param>
public record BillingParameter(string BillingYyyymm, int Shime, string TorisakiCodeFrom, string TorisakiCodeTo, bool IsReissue = false);

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
/// 出荷指示確定のパラメータ。対象の配分行に <c>KakuteiDay</c> を立てる。
/// 有効在庫（実在庫 − 引当数）が1SKUでも負になる場合はサーバが1件も確定せず、
/// <c>CvMsgErrorCode.ShippingUnavailable</c> と <see cref="ShippingShortageDto"/> 配列を返す。
/// 仕様は `Doc/spec/2026-08-18_I2I3_出荷指示確定・出荷処理_詳細設計.md` を参照する。
/// </summary>
/// <param name="HaibunIds">確定する配分行のId</param>
/// <param name="KakuteiDay">確定日 yyyyMMdd</param>
public sealed record ShippingConfirmParam(long[] HaibunIds, string KakuteiDay);

/// <summary>
/// 出荷指示確定の取消パラメータ。まだ伝票を作っていない確定済み行(<c>RelateNo2=0</c>)の <c>KakuteiDay</c> を空へ戻す。
/// </summary>
/// <param name="HaibunIds">取り消す配分行のId</param>
public sealed record ShippingCancelParam(long[] HaibunIds);

/// <summary>
/// 出荷処理のパラメータ。確定済み配分に実数量を入れ、出荷売上／移動伝票を作成し <c>EndFlag=1</c>（引当解除）にする。
/// </summary>
/// <param name="Rows">出荷処理する行（Id・楽観排他用Vdu・実数量）</param>
/// <param name="DenDay">生成する伝票の在庫計上日 yyyyMMdd</param>
/// <param name="IdShain">入力社員Id</param>
public sealed record ShippingCreateParam(ShippingCreateRow[] Rows, string DenDay, long IdShain);

/// <summary>出荷処理の1行分。実数量は 0〜指示数(Su) にサーバ側でクランプし、欠品は Su − 実数量。</summary>
/// <param name="Id">配分行のId</param>
/// <param name="ExpectedVdu">一覧取得時点のVdu。1件でも現在値と不一致なら全体を処理しない</param>
/// <param name="JitsuSu">実数量（出荷数）</param>
public sealed record ShippingCreateRow(long Id, long ExpectedVdu, int JitsuSu);

/// <summary>出荷指示確定の結果</summary>
/// <param name="ConfirmedCount">確定した配分行数</param>
public sealed record ShippingConfirmResult(int ConfirmedCount);

/// <summary>出荷指示確定取消の結果</summary>
/// <param name="CanceledCount">取り消した配分行数</param>
public sealed record ShippingCancelResult(int CanceledCount);

/// <summary>出荷処理の結果</summary>
/// <param name="CreatedSlipIds">作成した伝票Id（全量欠品の行は伝票を作らない）</param>
/// <param name="ReleasedCount">完了(EndFlag=1)にして引当解除した配分行数</param>
public sealed record ShippingCreateResult(long[] CreatedSlipIds, int ReleasedCount);

/// <summary>
/// 出荷指示確定で有効在庫を割った1SKU。画面へ返すワイヤ用DTO
/// （ドメインの <c>ShippingConfirmError</c> はサーバ専用のためここへ詰め替える）。
/// </summary>
/// <param name="Id_Soko">出庫元倉庫</param>
/// <param name="Id_Shohin">商品</param>
/// <param name="Id_Col">色</param>
/// <param name="Id_Siz">サイズ</param>
/// <param name="Shiji">確定しようとした指示数の合計</param>
/// <param name="Yuko">確定前の有効在庫（実在庫 − 引当数）</param>
public sealed record ShippingShortageDto(long Id_Soko, long Id_Shohin, long Id_Col, long Id_Siz, int Shiji, int Yuko);

/// <summary>
/// 期首残高（売掛・請求・買掛・支払）の登録パラメータ。
/// <para>
/// 対象日付の既存行を <paramref name="OwnerIds"/> の取引先ぶんだけ削除してから登録し直す（洗い替え）。
/// 削除と登録は1トランザクションで行う。<c>InsertBulkParam</c> は Insert のみで一意キー(uk1)違反になるため
/// 再取込に使えず、行単位の Delete では原子性が保てないので専用パラメータを設けている。
/// 仕様は `Doc/spec/2026-08-21_残高登録処理_詳細設計.md` を参照する。
/// </para>
/// </summary>
/// <param name="TableName">対象テーブル名。<c>OpeningBalanceCsv.AllowedTableNames</c> の4種のみ</param>
/// <param name="KeyDate">期首行のキー。売掛・買掛は DenMonth(yyyyMM)、請求・支払は DenDay(yyyyMMdd)</param>
/// <param name="OwnerIds">洗い替え対象の Id_Tokui / Id_Shiire。CSVに現れた取引先だけを対象にする</param>
/// <param name="ItemsJson">登録する行のJSON配列。残高0で削除だけの取引先は含まれない</param>
public sealed record OpeningBalanceImportParam(string TableName, string KeyDate, long[] OwnerIds, string ItemsJson);

/// <summary>期首残高登録の結果</summary>
/// <param name="Deleted">削除した既存行数</param>
/// <param name="Inserted">登録した行数</param>
public sealed record OpeningBalanceImportResult(int Deleted, int Inserted);

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
