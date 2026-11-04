using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CvBase;
using CvBaseMariadb;
using CvBasePostgre;
using CvBaseSqlite;
using CvDomainLogic;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;

namespace UatVm.Scenarios;

/// <summary>
/// DB切替処理（Msg043）のUAT。実View→ViewModel→gRPC→CvServer で SQLite（専用複製）から
/// 移動先（既定 MariaDb、<c>--switch-to Postgres</c> で PostgreSQL）へ全データを移し、
/// 画面のログ・進捗・完了ダイアログを確認したうえで、移動元と移動先を直接開いて表ごとに照合する。
/// <para>
/// 照合: 全対象表の件数・max(Id)・sum(Id)、各表の先頭/末尾20行の全共通列の値（数値は小数6桁で比較）。
/// 移動先の対象表は削除・再作成される。移動元は専用複製（<c>--sqlite</c>、CvServerフォルダ外）に限る。
/// </para>
/// </summary>
public static class SwitchDbScenario {
	/// <summary>移動先の接続定義名（appsettings.Development.json の ConnectionStrings のキー）</summary>
	public static string ToConnection { get; set; } = "MariaDb";

	/// <summary>移行は行わず、前回の移行結果を照合だけする（<c>--verify-only</c>）</summary>
	public static bool VerifyOnly { get; set; }

	const string FromConnection = "sqlite";
	const int SampleRows = 20;
	/// <summary>Id/Vdc/Vdu 不一致の例として証跡へ残す最大件数</summary>
	const int MaxDiffSamples = 5;
	const string TaskName = "DB切替データ移行";
	static string? _sqlitePath;

	/// <summary>シードは行わず、移動元DBが専用複製であることだけ確かめて記録する。</summary>
	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase)))
			throw new InvalidOperationException("移動元は --sqlite で CvServer フォルダ外の専用複製を指定してください。");
		if (!File.Exists(path))
			throw new FileNotFoundException("移動元のSQLiteがありません。", path);
		_sqlitePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (_sqlitePath == null) {
			session.Fail("前提", "--sqlite で専用複製を指定し、--no-seed を付けずに実行してください。");
			return;
		}
		session.Note("条件", new { from = FromConnection, to = ToConnection, sqlite = _sqlitePath, verifyOnly = VerifyOnly });
		if (VerifyOnly) {
			try {
				await Task.Run(() => Reconcile(session, _sqlitePath, ToConnection, null));
			}
			finally {
				Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
			}
			return;
		}

		// 実行確認(Question)だけ本文を検証してYes、完了(Information)はOK。エラーは既定のまま記録させる
		session.SetDialogResponder(request => request.Button switch {
			MessageBoxButton.YesNo when request.Message.Contains("移動先") && request.Message.Contains(ToConnection, StringComparison.OrdinalIgnoreCase) => MessageBoxResult.Yes,
			MessageBoxButton.YesNo => MessageBoxResult.No,
			_ => MessageBoxResult.OK,
		});

		var d = session.OpenView<SwitchDbView, SwitchDbViewModel>();
		await d.WaitAsync("接続定義の取得", vm => vm.ConnectionNames.Count >= 2 && vm.ToConnection != null);
		session.Check("接続定義にoracleを含まない", d.Vm.ConnectionNames.All(c => !c.Contains("oracle", StringComparison.OrdinalIgnoreCase)), d.Vm.ConnectionNames);
		session.Check("移動元の既定=sqlite", string.Equals(d.Vm.FromConnection, FromConnection, StringComparison.OrdinalIgnoreCase), d.Vm.FromConnection);
		session.Check("移動先の既定=mariadb", string.Equals(d.Vm.ToConnection, "mariadb", StringComparison.OrdinalIgnoreCase), d.Vm.ToConnection);

		// 同じ接続は実行不可
		d.Input("移動先=移動元", vm => vm.ToConnection = vm.FromConnection, FromConnection);
		session.Check("同一接続は実行ボタン無効", !d.Vm.ExecuteCommand.CanExecute(null));

		var target = d.Vm.ConnectionNames.FirstOrDefault(c => c.Equals(ToConnection, StringComparison.OrdinalIgnoreCase));
		if (target == null) {
			session.Fail("移動先", $"接続定義 {ToConnection} がサーバ設定にありません。");
			return;
		}
		d.Input("移動先", vm => vm.ToConnection = target, target);
		session.Check("実行ボタン有効", d.Vm.ExecuteCommand.CanExecute(null));

		var screens = Path.Combine(Path.GetDirectoryName(session.Evidence.FilePath) ?? ".", $"switchdb-{DateTime.Now:yyyyMMdd-HHmmss}");
		Directory.CreateDirectory(screens);
		session.Note("画面(実行前)", ScreenLayoutCheck.SaveJpeg(d.View, screens, "01-before"));
		session.Note("画面検査(実行前)", ScreenLayoutCheck.Inspect(d.View));

		session.ClearDialogs();
		var startedTicks = DateTime.UtcNow.Ticks;
		await d.RunAsync("DB切替実行", vm => vm.ExecuteCommand);

		var messages = d.Vm.StreamMessages.Reverse().ToList();
		session.Note("ストリームログ", messages);
		session.Note("画面(実行後)", ScreenLayoutCheck.SaveJpeg(d.View, screens, "02-after"));
		session.Check("エラー行なし", !messages.Any(m => m.StartsWith("エラー") || m.Contains("===== エラー")),
			messages.Where(m => m.Contains("エラー")).ToList());
		session.CheckEqual("進捗100", 100, d.Vm.ProgressValue);
		var expectedSteps = SwitchDb.BuildGroups().Count;
		session.CheckEqual("完了ステップ数", expectedSteps, messages.Count(m => m.StartsWith("完了: ")));
		session.Check("全処理完了の行", messages.Any(m => m.StartsWith("全処理完了")));
		session.Check("切替手順の表示", messages.Any(m => m.Contains("Database:Provider")));
		var dialogs = session.Dialogs.ToList();
		session.Check("完了ダイアログ", dialogs.Any(x => x.Request.Message.Contains("データ移行が完了")), dialogs.Select(x => x.Request.Message).ToList());
		session.Check("実行後は再実行不可", !d.Vm.ExecuteCommand.CanExecute(null));

		if (!messages.Any(m => m.StartsWith("全処理完了")) || messages.Any(m => m.StartsWith("エラー"))) {
			session.Fail("照合", "移行が正常に完了していないため、移動元と移動先の照合を省略しました。");
			return;
		}
		try {
			await Task.Run(() => Reconcile(session, _sqlitePath, ToConnection, startedTicks));
		}
		finally {
			// 照合で開いたプール接続を残すと、サーバ終了時のSQLite後始末が database is locked になる
			Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		}
	}

	/// <summary>
	/// 移動元と移動先を直接開き、表ごとに照合する。
	/// <list type="bullet">
	/// <item>件数・max(Id)・sum(Id)</item>
	/// <item>全行の Id・Vdc・Vdu（両方を Id 順に読み、1行ずつ突き合わせる）</item>
	/// <item>先頭/末尾20行の全共通列の値</item>
	/// </list>
	/// 移動元は試験サーバのメインDBでもあるため、移行中にサーバ自身が書く行（実行履歴・操作履歴）がある。
	/// Vdu が移行開始以降の行は両側で除外し、除外件数を別に記録する。
	/// </summary>
	static void Reconcile(VmSession session, string sqlitePath, string toConnection, long? startedTicks) {
		using var from = ExDatabaseSqlite.GetDbConn(sqlitePath);
		using ExDatabase to = OpenTarget(toConnection);
		var since = startedTicks ?? FindLastStartTicks(from);
		session.Note("照合の基準時刻(UTC ticks、これ以降に更新された行は除外)", new { since, local = new DateTime(since, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") });
		var summary = new List<object>();
		foreach (var table in SwitchDb.BuildGroups().SelectMany(g => g.Tables)) {
			var name = from.GetTableName(table);
			try {
				var toColumns = ColumnNames(to.Connection!, name);
				var fromColumns = ColumnNames(from.Connection!, name);
				var columns = toColumns.Where(c => fromColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
				var hasVdu = columns.Contains("Vdu", StringComparer.OrdinalIgnoreCase);
				string Where(ExDatabase db) => hasVdu ? $" where {db.DatabaseType.EscapeSqlIdentifier("Vdu")} < {since}" : "";

				var a = Aggregate(from.Connection!, name, Where(from));
				var b = Aggregate(to.Connection!, name, Where(to));
				session.Check($"照合:{name} 件数/maxId/sumId", a == b, new { from = a, to = b });
				var excluded = hasVdu
					? new { from = Count(from.Connection!, name, $" where {from.DatabaseType.EscapeSqlIdentifier("Vdu")} >= {since}"), to = Count(to.Connection!, name, $" where {to.DatabaseType.EscapeSqlIdentifier("Vdu")} >= {since}") }
					: null;
				if (excluded is { } ex1 && (ex1.from > 0 || ex1.to > 0)) {
					session.Note($"照合:{name} 移行開始以降に更新された行(除外)", ex1);
				}

				var keys = CompareKeys(from, to, name, Where(from), Where(to), columns);
				session.Check($"照合:{name} 全行Id/Vdc/Vdu", keys.IsMatch, keys);
				summary.Add(new { table = name, rows = keys.Rows, keys.Missing, keys.Extra, keys.VdcDiff, keys.VduDiff });

				foreach (var order in new[] { "asc", "desc" }) {
					string Sql(ExDatabase db) => $"select {string.Join(",", columns.Select(db.DatabaseType.EscapeSqlIdentifier))} from {name}{Where(db)} order by Id {order} limit {SampleRows}";
					var rowsA = ReadRows(from.Connection!, Sql(from), table, columns);
					var rowsB = ReadRows(to.Connection!, Sql(to), table, columns);
					var diff = FirstDiff(rowsA, rowsB, columns);
					session.Check($"照合:{name} 行内容({order})", diff == null, diff);
				}
			}
			catch (Exception ex) {
				session.Fail($"照合:{name}", ex.Message);
			}
		}
		session.Note("照合:全行Id/Vdc/Vdu 集計", summary);
	}

	/// <summary>前回のDB切替の開始時刻（移動元の実行履歴）。--verify-only で使う</summary>
	static long FindLastStartTicks(ExDatabase from) {
		var start = from.ExecuteScalar<string>("select StartTime from SysHistAutoexec where TaskName = @0 order by Id desc limit 1", TaskName);
		if (string.IsNullOrEmpty(start) || !DateTime.TryParseExact(start, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)) {
			throw new InvalidOperationException("移動元の実行履歴にDB切替の開始時刻がありません。");
		}
		return local.ToUniversalTime().Ticks;
	}

	/// <summary>Id/Vdc/Vdu の全行照合結果</summary>
	sealed record KeyCompareResult(long Rows, long Missing, long Extra, long VdcDiff, long VduDiff, List<string> Samples) {
		public bool IsMatch => Missing == 0 && Extra == 0 && VdcDiff == 0 && VduDiff == 0;
	}

	/// <summary>
	/// 両DBを Id 順に1行ずつ読み、Id の欠落・余剰と Vdc・Vdu の不一致を数える（全件・メモリに溜めない）。
	/// </summary>
	static KeyCompareResult CompareKeys(ExDatabase from, ExDatabase to, string table, string whereFrom, string whereTo, List<string> columns) {
		var hasVdc = columns.Contains("Vdc", StringComparer.OrdinalIgnoreCase);
		var hasVdu = columns.Contains("Vdu", StringComparer.OrdinalIgnoreCase);
		string Sql(ExDatabase db) {
			var e = db.DatabaseType.EscapeSqlIdentifier;
			return $"select {e("Id")}, {(hasVdc ? e("Vdc") : "0")}, {(hasVdu ? e("Vdu") : "0")} from {table}{(db == from ? whereFrom : whereTo)} order by {e("Id")}";
		}
		using var ca = from.Connection!.CreateCommand();
		ca.CommandText = Sql(from);
		ca.CommandTimeout = 0;
		using var cb = to.Connection!.CreateCommand();
		cb.CommandText = Sql(to);
		cb.CommandTimeout = 0;
		using var ra = ca.ExecuteReader();
		using var rb = cb.ExecuteReader();
		long rows = 0, missing = 0, extra = 0, vdcDiff = 0, vduDiff = 0;
		var samples = new List<string>();
		void Sample(string s) { if (samples.Count < MaxDiffSamples) samples.Add(s); }
		static long L(DbDataReader r, int i) => Convert.ToInt64(r.GetValue(i), CultureInfo.InvariantCulture);
		var hasA = ra.Read();
		var hasB = rb.Read();
		while (hasA || hasB) {
			var idA = hasA ? L(ra, 0) : long.MaxValue;
			var idB = hasB ? L(rb, 0) : long.MaxValue;
			if (idA == idB) {
				rows++;
				if (L(ra, 1) != L(rb, 1)) { vdcDiff++; Sample($"Id={idA} Vdc {L(ra, 1)} != {L(rb, 1)}"); }
				if (L(ra, 2) != L(rb, 2)) { vduDiff++; Sample($"Id={idA} Vdu {L(ra, 2)} != {L(rb, 2)}"); }
				hasA = ra.Read();
				hasB = rb.Read();
			}
			else if (idA < idB) {
				missing++;
				Sample($"Id={idA} 移動先になし");
				hasA = ra.Read();
			}
			else {
				extra++;
				Sample($"Id={idB} 移動元になし");
				hasB = rb.Read();
			}
		}
		return new KeyCompareResult(rows, missing, extra, vdcDiff, vduDiff, samples);
	}

	static long Count(DbConnection conn, string table, string where) {
		using var cmd = conn.CreateCommand();
		cmd.CommandTimeout = 0;
		cmd.CommandText = $"select count(*) from {table}{where}";
		return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	static ExDatabase OpenTarget(string connectionName) {
		var settings = File.ReadAllText(Path.GetFullPath(Path.Combine("..", "CvServer", "appsettings.Development.json")));
		var match = Regex.Match(settings, $"\"{Regex.Escape(connectionName)}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
		if (!match.Success) throw new InvalidOperationException($"appsettings.Development.json に {connectionName} がありません。");
		var connStr = match.Groups[1].Value;
		var key = connectionName.ToLowerInvariant();
		if (key.Contains("maria") || key.Contains("mysql")) return ExDatabaseMaria.GetDbConn(connStr);
		if (key.Contains("postgre")) return ExDatabasePostgre.GetDbConn(connStr);
		throw new InvalidOperationException($"移動先 {connectionName} の種別を判別できません。");
	}

	static string Aggregate(DbConnection conn, string table, string where) {
		using var cmd = conn.CreateCommand();
		cmd.CommandTimeout = 0;
		cmd.CommandText = $"select count(*), coalesce(max(Id),0), coalesce(sum(Id),0) from {table}{where}";
		using var r = cmd.ExecuteReader();
		r.Read();
		return string.Join("/", Enumerable.Range(0, 3).Select(i => Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture)));
	}

	static List<string> ColumnNames(DbConnection conn, string table) {
		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"select * from {table} where 1=0";
		using var r = cmd.ExecuteReader();
		return Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
	}

	static List<string[]> ReadRows(DbConnection conn, string sql, Type table, List<string> columns) {
		var types = columns.Select(c => SwitchDb.GetTargetType(table, c)).ToArray();
		using var cmd = conn.CreateCommand();
		cmd.CommandTimeout = 0;
		cmd.CommandText = sql;
		using var r = cmd.ExecuteReader();
		var rows = new List<string[]>();
		while (r.Read()) {
			rows.Add([.. Enumerable.Range(0, columns.Count).Select(i => Normalize(SwitchDb.ConvertValue(r.GetValue(i), types[i])))]);
		}
		return rows;
	}

	static string Normalize(object value) => value switch {
		decimal or double or float => Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), 6).ToString("0.######", CultureInfo.InvariantCulture),
		bool b => b ? "true" : "false",
		IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "",
	};

	static object? FirstDiff(List<string[]> a, List<string[]> b, List<string> columns) {
		if (a.Count != b.Count) return new { rows = $"{a.Count} != {b.Count}" };
		for (var r = 0; r < a.Count; r++) {
			for (var c = 0; c < columns.Count; c++) {
				if (a[r][c] != b[r][c]) {
					return new { row = r, column = columns[c], from = Cut(a[r][c]), to = Cut(b[r][c]) };
				}
			}
		}
		return null;
	}

	static string Cut(string s) => s.Length <= 200 ? s : s[..200] + "…";
}
