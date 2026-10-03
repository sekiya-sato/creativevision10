using CvBase;
using CvBaseSqlite;

namespace UatVm.Seed;

/// <summary>
/// シーダー共通のスキーマ準備。
/// <para>
/// シードは CvServer の起動（起動時の <see cref="UpdateDb"/> 適用）より前に、複製DBへ直接書き込む。
/// 複製元が古い版のままだと、在庫・引当の再計算が新しい列（例: 配分再設計 Step 4 の <c>TranHaibun.ArrivedSu</c>）を読んで失敗するので、
/// 先に製品と同じ migration を当てておく（CvServer 起動時は最新版として何もしない）。
/// </para>
/// </summary>
public static class SeedSchema {
	/// <summary>複製DBへ <see cref="UpdateDb"/> の migration を当てる</summary>
	public static void ApplyMigrations(ExDatabaseSqlite db, Action<string> trace) {
		UpdateDb.WriteVersionInfoAsync(db).GetAwaiter().GetResult();
		trace("UpdateDb の migration を複製DBへ適用");
	}
}
