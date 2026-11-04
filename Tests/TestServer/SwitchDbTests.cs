using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Tests.CvServer;

/// <summary>
/// DB切替のデータ移行(SwitchDb)。SQLite→SQLiteで、Id保持・JSON無変換・件数照合・複写後の自動採番を確認する。
/// </summary>
[TestClass]
public sealed class SwitchDbTests {
	private ExDatabaseSqlite? _from;
	private ExDatabaseSqlite? _to;

	[TestInitialize]
	public void Initialize() {
		var fromConn = new SqliteConnection("Data Source=:memory:");
		fromConn.Open();
		_from = new ExDatabaseSqlite(fromConn);
		var toConn = new SqliteConnection("Data Source=:memory:");
		toConn.Open();
		_to = new ExDatabaseSqlite(toConn);
	}

	[TestCleanup]
	public void Cleanup() {
		_from?.Dispose();
		_to?.Dispose();
	}

	[TestMethod]
	public async Task CopyAll_Idと内容を保って全表を複写する() {
		var from = _from!;
		var to = _to!;
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(from, false));
		// Idの欠番を作り、移動先で振り直されないことを確かめる
		from.Execute("update MasterShohin set Id = 1000 where Code = '0001'");
		// 移動先には既存データ（初期データ）があっても作り直される
		Assert.IsTrue(await new DefineDataTable().InitializeAsync(to, false));
		to.Execute("insert into MasterShiire (Code, Name) values ('9999', '移動先だけの行')");

		var progress = new List<StreamStepProgress>();
		await foreach (var p in new SwitchDb(from, to).CopyAllAsyncStream()) {
			progress.Add(p);
		}

		var errors = progress.Where(p => p.IsError).Select(p => $"{p.StepName}: {p.ErrorMessage}").ToList();
		Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
		Assert.IsTrue(progress.Last().IsCompleted);
		var groups = SwitchDb.BuildGroups();
		Assert.AreEqual("システム", groups[0].Name);
		Assert.AreEqual("マスタ", groups[1].Name);
		Assert.AreEqual("集計データ", groups[^1].Name);
		Assert.IsTrue(groups.Where(g => g.Name.StartsWith("トランザクション")).All(g => g.Tables.Count == 1));

		foreach (var table in groups.SelectMany(g => g.Tables)) {
			var name = from.GetTableName(table);
			Assert.AreEqual(from.ExecuteScalar<long>($"select count(*) from {name}"), to.ExecuteScalar<long>($"select count(*) from {name}"), name);
		}
		Assert.AreEqual(0L, to.ExecuteScalar<long>("select count(*) from MasterShiire where Code = '9999'"), "移動先の既存行は残らない");
		Assert.AreEqual(
			from.ExecuteScalar<string>("select Jcolsiz from MasterShohin where Id = 1000"),
			to.ExecuteScalar<string>("select Jcolsiz from MasterShohin where Id = 1000"),
			"JSON列は文字列のまま移る");
		Assert.AreEqual(
			from.ExecuteScalar<string>("select Name from MasterSysman order by Id limit 1"),
			to.ExecuteScalar<string>("select Name from MasterSysman order by Id limit 1"));

		// 複写後の採番は最大Idの次から
		to.Execute("insert into MasterShohin (Code, Name) values ('0002', '複写後の追加')");
		Assert.AreEqual(1001L, to.ExecuteScalar<long>("select Id from MasterShohin where Code = '0002'"));
	}

	[TestMethod]
	public void ConvertValue_移動先の列型へ合わせる() {
		Assert.IsTrue((bool)SwitchDb.ConvertValue(1L, typeof(bool)));
		Assert.IsFalse((bool)SwitchDb.ConvertValue("0", typeof(bool)));
		Assert.AreEqual("1", SwitchDb.ConvertValue(true, typeof(string)));
		Assert.AreEqual(string.Empty, SwitchDb.ConvertValue(DBNull.Value, typeof(string)));
		Assert.AreEqual(0, SwitchDb.ConvertValue(null, typeof(int)));
		Assert.AreEqual(12.5m, SwitchDb.ConvertValue(12.5d, typeof(decimal)));
		Assert.AreEqual(3, SwitchDb.ConvertValue(3L, typeof(int)));
	}
}
