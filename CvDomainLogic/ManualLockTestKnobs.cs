namespace CvDomainLogic;

/// <summary>
/// <b>テスト専用。Step T7で削除する。</b>
/// マニュアル排他制御の実プロセス・実サーバでの動作確認のために用意した環境変数スイッチ群。
/// 正典は `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` §3（S1/S2/S3/S5）。
/// <para>
/// 環境変数が未設定の場合、本クラスの全メンバーは本番動作と完全に同一の結果（何もしない／元の値を
/// そのまま返す）になる。プロセス単位で環境変数を1度だけ読み、ケースごとにプロセスを再起動する
/// 運用（計画書§3, §4.1）のため、実行中の再読み込みはしない。
/// </para>
/// </summary>
internal static class ManualLockTestKnobs {
	/// <summary>
	/// S1（計画書§3）。<see cref="ManualLockDb.TryBegin"/>で勝者が確定した直後に呼ばれる。
	/// 環境変数 <c>CV10_LOCK_SLEEP_BEGIN_MS</c>（ミリ秒）。未設定・不正値・0以下は既定（何もしない）
	/// </summary>
	private static readonly int SleepAtBeginMs = ReadNonNegativeInt("CV10_LOCK_SLEEP_BEGIN_MS");

	/// <summary>
	/// S2（計画書§3）。<see cref="ManualLockDb.Progress"/>のUPDATE直前に呼ばれる。
	/// 環境変数 <c>CV10_LOCK_SLEEP_STEP_MS</c>（ミリ秒）。未設定・不正値・0以下は既定（何もしない）
	/// </summary>
	private static readonly int SleepAtProgressMs = ReadNonNegativeInt("CV10_LOCK_SLEEP_STEP_MS");

	/// <summary>
	/// S5（計画書§3）。環境変数 <c>CV10_LOCK_EXPECTED_SEC</c>（秒）。
	/// 未設定・不正値・負値なら<c>null</c>（＝<see cref="OverrideExpectedDuration"/>は元の値をそのまま返す）
	/// </summary>
	private static readonly long? ExpectedDurationOverrideSeconds = ReadNonNegativeLongOrNull("CV10_LOCK_EXPECTED_SEC");

	/// <summary>
	/// S3（計画書§3）。異常判定の閾値の下限（分）。既定値15は<see cref="ManualLockMonitor"/>から
	/// 移設した値（設計書§3.4）。環境変数 <c>CV10_LOCK_MIN_THRESHOLD_MIN</c> は1以上のみ有効で、
	/// 0・負値・未設定・不正値は既定15にフォールバックする。
	/// </summary>
	public static readonly int MinThresholdMinutes = ReadPositiveIntOrDefault("CV10_LOCK_MIN_THRESHOLD_MIN", 15);

	/// <summary>
	/// S1。指定ミリ秒だけ<see cref="Thread.Sleep(int)"/>する。0以下（未設定含む）なら即returnし、
	/// <c>Thread.Sleep(0)</c>すら呼ばない。
	/// </summary>
	public static void SleepAtBegin() {
		if (SleepAtBeginMs <= 0) {
			return;
		}
		Console.WriteLine($"[ManualLockTestKnobs] S1: CV10_LOCK_SLEEP_BEGIN_MS={SleepAtBeginMs}ms sleeping (TryBegin)");
		Thread.Sleep(SleepAtBeginMs);
	}

	/// <summary>
	/// S2。指定ミリ秒だけ<see cref="Thread.Sleep(int)"/>する。0以下（未設定含む）なら即returnし、
	/// <c>Thread.Sleep(0)</c>すら呼ばない。
	/// </summary>
	public static void SleepAtProgress() {
		if (SleepAtProgressMs <= 0) {
			return;
		}
		Console.WriteLine($"[ManualLockTestKnobs] S2: CV10_LOCK_SLEEP_STEP_MS={SleepAtProgressMs}ms sleeping (Progress)");
		Thread.Sleep(SleepAtProgressMs);
	}

	/// <summary>
	/// S5。<c>CV10_LOCK_EXPECTED_SEC</c>が設定されていればその秒数を返し、未設定なら<paramref name="original"/>を
	/// そのまま返す。
	/// </summary>
	public static long OverrideExpectedDuration(long original) => ExpectedDurationOverrideSeconds ?? original;

	private static int ReadNonNegativeInt(string envName) {
		var raw = Environment.GetEnvironmentVariable(envName);
		if (int.TryParse(raw, out var value) && value > 0) {
			return value;
		}
		return 0;
	}

	private static long? ReadNonNegativeLongOrNull(string envName) {
		var raw = Environment.GetEnvironmentVariable(envName);
		if (long.TryParse(raw, out var value) && value >= 0) {
			return value;
		}
		return null;
	}

	private static int ReadPositiveIntOrDefault(string envName, int defaultValue) {
		var raw = Environment.GetEnvironmentVariable(envName);
		if (int.TryParse(raw, out var value) && value >= 1) {
			return value;
		}
		return defaultValue;
	}
}
