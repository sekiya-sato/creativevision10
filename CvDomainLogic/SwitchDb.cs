using CvBase;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CvDomainLogic;

/// <summary>
/// SqlDepends: DB切替のデータ移行。移動元DBの全テーブルを移動先DBへ複写する。
/// <para>
/// 対象表は <see cref="DefineDataTable.TableTypes"/>。移動先の表は毎回 DROP → CREATE（現行定義・索引込み）してから
/// 全件を Id を保ったまま投入する（Id_* 参照を壊さないため）。値は移動先の列型へ合わせるだけで、
/// JSON列は文字列のまま無変換で移す（再シリアライズで既定値省略などの差を生まないため）。
/// </para>
/// <para>
/// 移動元へは一切書き込まない。マニュアル排他（SysSequence の行）は取らない：
/// SysSequence 自体が複写対象のため、実行中の排他行が移動先へ写り込むのを避ける。
/// 実行中は他の利用者が更新しない運用とし、表ごとの件数照合で取りこぼしを検出する。
/// </para>
/// </summary>
public sealed class SwitchDb {
	readonly ExDatabase _fromDb;
	readonly ExDatabase _toDb;
	readonly ILogger<SwitchDb> _logger = new NLogExtender<SwitchDb>();

	/// <summary>1回のINSERTで送る最大行数</summary>
	const int MaxRowsPerInsert = 1000;
	/// <summary>1回のINSERTの概算データ量上限（MariaDB の max_allowed_packet 既定16MBを超えないよう控えめにする）</summary>
	const long MaxBytesPerInsert = 4L * 1024 * 1024;
	/// <summary>1回のINSERTのパラメータ数上限（SQLite 32766 / PostgreSQL・MariaDB 65535 より小さく）</summary>
	const int MaxParamsPerInsert = 30000;

	public SwitchDb(ExDatabase fromDb, ExDatabase toDb) {
		_fromDb = fromDb;
		_toDb = toDb;
	}

	/// <summary>
	/// 複写の単位（ストリームの1ステップ）を組み立てる。
	/// システム → マスタ（派生を含む）→ Tran各表（件数が多いので1表1ステップ）→ 集計 の順。
	/// </summary>
	public static IReadOnlyList<(string Name, IReadOnlyList<Type> Tables)> BuildGroups() {
		var tables = DefineDataTable.TableTypes
			.Where(t => t.GetCustomAttribute<NoCreateAttribute>() == null && t.GetCustomAttribute<SubTableDefineAttribute>() == null)
			.ToList();
		var system = tables.Where(t => t.Name.StartsWith("Sys", StringComparison.Ordinal)).ToList();
		var summary = tables.Where(t => t.Name.StartsWith("Summary", StringComparison.Ordinal)).ToList();
		var trans = tables.Where(t => t.Name.StartsWith("Tran", StringComparison.Ordinal)).ToList();
		var master = tables.Except(system).Except(summary).Except(trans).ToList();

		var groups = new List<(string, IReadOnlyList<Type>)> {
			("システム", system),
			("マスタ", master),
		};
		groups.AddRange(trans.Select(t => ($"トランザクション {t.Name}", (IReadOnlyList<Type>)[t])));
		groups.Add(("集計データ", summary));
		return groups;
	}

	/// <summary>
	/// ストリーミングで全テーブルを複写する。
	/// </summary>
	public IAsyncEnumerable<StreamStepProgress> CopyAllAsyncStream() {
		var steps = BuildGroups()
			.Select(g => (g.Name, (Func<int, int>)(_ => CopyTables(g.Tables))))
			.ToArray();
		return StreamStepProgressRunner.Run(
			steps,
			0,
			_logger,
			"DB切替データ移行開始",
			"DB切替データ移行エラー: {StepName}",
			"DB切替データ移行終了");
	}

	int CopyTables(IReadOnlyList<Type> tables) {
		var total = 0;
		foreach (var table in tables) {
			try {
				total += CopyTable(table);
			}
			catch (Exception ex) when (ex is not InvalidOperationException) {
				// DBドライバの例外は表名を含まないため、どの表で失敗したかを付けて上げ直す
				throw new InvalidOperationException($"{_toDb.GetTableName(table)}: {ex.Message}", ex);
			}
		}
		return total;
	}

	/// <summary>
	/// 1表を複写する。移動先の表を作り直し、1トランザクションで全件投入して件数を照合する。
	/// </summary>
	int CopyTable(Type tableType) {
		var tableName = _toDb.GetTableName(tableType);
		// 表ごと削除してから作る。CreateTable(isForce:true) は索引を DROP INDEX IF EXISTS <名前> で消すが、
		// MariaDB は ON <表名> が必須で構文エラーになるため使わない（表を消せば索引も消える）。
		_toDb.DropTable(tableType);
		if (!_toDb.CreateTable(tableType, isForce: false)) {
			throw new InvalidOperationException($"{tableName}: 移動先の表作成に失敗しました。");
		}
		if (!_fromDb.IsExistTable(tableType)) {
			_logger.LogWarning("DB切替: 移動元に表がないため空の表だけ作成しました。 Table={Table}", tableName);
			return 0;
		}

		var fromConn = _fromDb.Connection ?? throw new InvalidOperationException("移動元DBに接続していません。");
		var toConn = _toDb.Connection ?? throw new InvalidOperationException("移動先DBに接続していません。");

		// 列は移動先の実表（現行定義）を基準に、移動元にもある列だけを移す。照合は大文字小文字を区別しない
		// （PostgreSQL は列名を小文字で持つ）。移動元にない列は移動先の既定値になる。
		var toColumns = GetColumnNames(toConn, tableName);
		var fromColumns = GetColumnNames(fromConn, tableName);
		var fromLookup = fromColumns.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);
		var columns = toColumns.Where(fromLookup.ContainsKey).ToList();
		var skipped = toColumns.Where(c => !fromLookup.ContainsKey(c)).ToList();
		if (skipped.Count > 0) {
			_logger.LogWarning("DB切替: 移動元にない列は既定値になります。 Table={Table} Columns={Columns}", tableName, string.Join(",", skipped));
		}
		if (columns.Count == 0) {
			throw new InvalidOperationException($"{tableName}: 移動元と共通の列がありません。");
		}
		var targetTypes = columns.Select(c => GetTargetType(tableType, c)).ToArray();
		var sourceCount = Convert.ToInt64(ExecuteScalar(fromConn, $"select count(*) from {tableName}"), CultureInfo.InvariantCulture);

		var hasId = columns.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase));
		// 列名は各DBの規則で引用する（SysUpdateDb.Sql のように MariaDB の予約語と重なる列があるため）
		var selectSql = $"select {string.Join(",", columns.Select(c => _fromDb.DatabaseType.EscapeSqlIdentifier(fromLookup[c])))} from {tableName}{(hasId ? " order by Id" : "")}";
		var rowsPerInsert = Math.Max(1, Math.Min(MaxRowsPerInsert, MaxParamsPerInsert / columns.Count));
		var insertHead = $"insert into {tableName} ({string.Join(",", columns.Select(_toDb.DatabaseType.EscapeSqlIdentifier))}) values ";

		long inserted = 0;
		using (var tx = toConn.BeginTransaction()) {
			using (var select = fromConn.CreateCommand()) {
				select.CommandText = selectSql;
				select.CommandTimeout = 0;
				using var reader = select.ExecuteReader();
				var batch = new List<object?[]>(rowsPerInsert);
				long batchBytes = 0;
				while (reader.Read()) {
					var row = new object?[columns.Count];
					for (var i = 0; i < columns.Count; i++) {
						var value = ConvertValue(reader.GetValue(i), targetTypes[i]);
						row[i] = value;
						// 文字列はUTF-8で最大3バイト/文字として概算する（JSON列の多い伝票で送信量が膨らむため）
						batchBytes += value is string s ? s.Length * 3L + 8 : 16;
					}
					batch.Add(row);
					if (batch.Count >= rowsPerInsert || batchBytes >= MaxBytesPerInsert) {
						inserted += InsertBatch(toConn, tx, insertHead, columns.Count, batch);
						batch.Clear();
						batchBytes = 0;
					}
				}
				if (batch.Count > 0) {
					inserted += InsertBatch(toConn, tx, insertHead, columns.Count, batch);
				}
			}
			tx.Commit();
		}

		_toDb.ResetIdentity(tableType);

		var targetCount = Convert.ToInt64(ExecuteScalar(toConn, $"select count(*) from {tableName}"), CultureInfo.InvariantCulture);
		if (targetCount != inserted || inserted != sourceCount) {
			throw new InvalidOperationException(
				$"{tableName}: 件数が一致しません。移動元={sourceCount:N0} 読込={inserted:N0} 移動先={targetCount:N0}（実行中に移動元が更新された可能性があります）");
		}
		_logger.LogInformation("DB切替: 複写完了 Table={Table} Count={Count}", tableName, inserted);
		return checked((int)inserted);
	}

	static int InsertBatch(DbConnection conn, DbTransaction tx, string insertHead, int columnCount, List<object?[]> batch) {
		using var cmd = conn.CreateCommand();
		cmd.Transaction = tx;
		cmd.CommandTimeout = 0;
		var sql = new StringBuilder(insertHead);
		var index = 0;
		for (var r = 0; r < batch.Count; r++) {
			sql.Append(r == 0 ? "(" : ",(");
			for (var c = 0; c < columnCount; c++) {
				var name = $"@p{index++}";
				if (c > 0) sql.Append(',');
				sql.Append(name);
				var p = cmd.CreateParameter();
				p.ParameterName = name;
				p.Value = batch[r][c] ?? DBNull.Value;
				cmd.Parameters.Add(p);
			}
			sql.Append(')');
		}
		cmd.CommandText = sql.ToString();
		cmd.ExecuteNonQuery();
		return batch.Count;
	}

	static List<string> GetColumnNames(DbConnection conn, string tableName) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"select * from {tableName} where 1=0";
		using var reader = cmd.ExecuteReader();
		return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
	}

	static object? ExecuteScalar(DbConnection conn, string sql) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		cmd.CommandTimeout = 0;
		return cmd.ExecuteScalar();
	}

	/// <summary>
	/// 列に対応するプロパティから、移動先へ渡す値の型を決める（DDL生成と同じ規則）。
	/// 対応するプロパティがない列は文字列として扱う。
	/// </summary>
	public static Type GetTargetType(Type tableType, string columnName) {
		var property = tableType.GetProperty(columnName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		if (property == null) {
			return typeof(string);
		}
		var size = property.GetCustomAttribute<ColumnSizeDmlAttribute>();
		if (size != null) {
			return size.ColType == ColumnType.Enum ? typeof(int) : typeof(string);
		}
		var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
		if (type.IsEnum) return typeof(int);
		if (type == typeof(bool)) return typeof(bool);
		if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)) return typeof(short);
		if (type == typeof(int) || type == typeof(ushort)) return typeof(int);
		if (type == typeof(long) || type == typeof(uint) || type == typeof(ulong)) return typeof(long);
		if (type == typeof(decimal)) return typeof(decimal);
		if (type == typeof(double)) return typeof(double);
		if (type == typeof(float)) return typeof(float);
		return typeof(string);
	}

	/// <summary>
	/// 移動元の値を移動先の列型へ合わせる。NULL は DDL の既定値（'' / 0 / false）に置き換える
	/// （全列が NOT NULL DEFAULT のため）。
	/// </summary>
	public static object ConvertValue(object? value, Type targetType) {
		if (value is null || value is DBNull) {
			return targetType == typeof(string) ? string.Empty
				: targetType == typeof(bool) ? false
				: Convert.ChangeType(0, targetType, CultureInfo.InvariantCulture);
		}
		if (targetType == typeof(string)) {
			return value switch {
				string s => s,
				byte[] bytes => Encoding.UTF8.GetString(bytes),
				bool b => b ? "1" : "0",
				IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
				_ => value.ToString() ?? string.Empty,
			};
		}
		if (targetType == typeof(bool)) {
			return value switch {
				bool b => b,
				string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
				_ => Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m,
			};
		}
		if (value is string text && string.IsNullOrWhiteSpace(text)) {
			return Convert.ChangeType(0, targetType, CultureInfo.InvariantCulture);
		}
		return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
	}
}
