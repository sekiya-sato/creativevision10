using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CodeShare;
using CvBaseSqlite;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// 原価4処理・評価替えのgRPC公開(Step 9)を最低限固定するテスト。
/// 正典は `Doc/spec/2026-09-05_原価4項目_詳細設計.md` §9.3、`Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §2.4。
/// <para>
/// ハンドラ単位の業務テストは`CostUpdateDb`側(<see cref="CostUpdateDbConsumptionTests"/>等)で既に
/// カバーしているため、ここでは最低限
/// (1) 追加した11個の<see cref="CvFlag"/>値(80〜90)が重複しないこと、
/// (2) <c>QueryMsgAsync</c>側に登録すべき7つが<see cref="CoreService"/>の<c>_handlers</c>に登録されていること、
/// (3) <c>BatchId</c>空文字時のサーバー採番(<see cref="CoreService.ResolveBatchId"/>)の3点だけを固定する。
/// </para>
/// </summary>
[TestClass]
public class CoreServiceCostHandlersTests {
	/// <summary>
	/// 追加した11個の<see cref="CvFlag"/>(80〜90)の値が重複しないこと。
	/// enum定義そのものが重複値を許してしまうミスを防ぐ(63〜69・74〜79は将来用途のため未使用のまま)。
	/// </summary>
	[TestMethod]
	public void CvFlag_80から90の値が重複しない() {
		var values = Enum.GetValues<CvFlag>()
			.Cast<int>()
			.Where(v => v is >= 80 and <= 90)
			.ToList();

		Assert.AreEqual(11, values.Count, "80〜90の11個が定義されていること");
		Assert.AreEqual(values.Count, values.Distinct().Count(), "80〜90の値に重複が無いこと");
		CollectionAssert.AreEquivalent(Enumerable.Range(80, 11).ToList(), values, "80〜90が連続して1つずつ定義されていること");
	}

	/// <summary>
	/// Msg080/081/083/084/086/088/090の7つが<c>QueryMsgAsync</c>側(<c>_handlers</c>)に登録されていること。
	/// 更新実行(Msg082/085/087/089)はストリーム側(<c>QueryMsgStreamAsync</c>)の担当のため
	/// <c>_handlers</c>には含めない(登録すると誤って同期経路からも呼べてしまう)。
	/// </summary>
	[TestMethod]
	public void QueryMsgAsync側の7ハンドラがすべて登録されている() {
		using var db = CreateInMemoryDb();
		using var scopeFactoryProvider = new ServiceCollection().BuildServiceProvider();
		var coreService = new CoreService(
			NullLogger<CoreService>.Instance,
			new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
			new FakeWebHostEnvironment(),
			new HttpContextAccessor(),
			db,
			scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(db, NullLogger<PointOfSaleService>.Instance));

		var handlersField = typeof(CoreService).GetField("_handlers", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new AssertFailedException("CoreService._handlers フィールドが見つかりません");
		var handlers = handlersField.GetValue(coreService) as IDictionary
			?? throw new AssertFailedException("_handlers を辞書として取得できません");

		CvFlag[] expected = [
			CvFlag.Msg080_CostMonthStatus,
			CvFlag.Msg081_CostConsumptionPreview,
			CvFlag.Msg083_CostSundryPreview,
			CvFlag.Msg084_CostLastPurchasePreview,
			CvFlag.Msg086_CostTotalAveragePreview,
			CvFlag.Msg088_CostRevaluationPreview,
			CvFlag.Msg090_CostRevaluationCancel,
		];
		foreach (var flag in expected) {
			Assert.IsTrue(handlers.Contains(flag), $"{flag} が _handlers に登録されていません");
		}

		// 更新実行系(ストリーム側)は同期経路(_handlers)に登録しないこと。
		CvFlag[] streamOnly = [
			CvFlag.Msg082_CostConsumptionApply,
			CvFlag.Msg085_CostLastPurchaseApply,
			CvFlag.Msg087_CostTotalAverageApply,
			CvFlag.Msg089_CostRevaluationApply,
		];
		foreach (var flag in streamOnly) {
			Assert.IsFalse(handlers.Contains(flag), $"{flag} はストリーム側専用のため _handlers に登録してはならない");
		}
	}

	/// <summary>
	/// <c>BatchId</c>が空文字の場合、サーバー側でGUIDのD形式(36文字)を採番すること
	/// (原価4項目 詳細設計 §2.5.2)。空でない場合はクライアント指定値をそのまま使うこと
	/// (確認と更新で同一値を使う運用のため)。
	/// </summary>
	[TestMethod]
	public void ResolveBatchId_空文字はGUIDのD形式を採番しそれ以外はそのまま使う() {
		var generated = CoreService.ResolveBatchId("");
		Assert.AreEqual(36, generated.Length, "GUIDのD形式(ハイフン込み36文字)であること");
		Assert.IsTrue(Guid.TryParseExact(generated, "D", out _), "GUIDのD形式としてパースできること");

		var generatedFromNull = CoreService.ResolveBatchId(null);
		Assert.AreEqual(36, generatedFromNull.Length);

		const string existing = "11111111-1111-1111-1111-111111111111";
		Assert.AreEqual(existing, CoreService.ResolveBatchId(existing), "空文字でなければクライアント指定値をそのまま使うこと");

		// 空文字2回の採番が別の値になること(採番のたびにGUIDを生成していることの確認)。
		Assert.AreNotEqual(CoreService.ResolveBatchId(""), CoreService.ResolveBatchId(""));
	}

	private static ExDatabaseSqlite CreateInMemoryDb() {
		var databaseName = $"CoreServiceCostHandlersTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		return new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
	}
}
