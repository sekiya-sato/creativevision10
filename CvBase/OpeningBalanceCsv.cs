using CvAsset;
using CvBase.Share;
using System.Globalization;
using System.Text;

namespace CvBase;

/// <summary>期首残高の登録区分。</summary>
public enum EnumOpeningBalanceKind {
	/// <summary>売掛（SummaryUriKake / 年月キー）</summary>
	UriKake = 0,
	/// <summary>請求（SummaryUriSei / 請求日キー）</summary>
	UriSei = 1,
	/// <summary>買掛（SummaryKaiKake / 年月キー）</summary>
	KaiKake = 2,
	/// <summary>支払（SummaryKaiShi / 支払日キー）</summary>
	KaiShi = 3,
}

/// <summary>取込プレビューの行状態。</summary>
public enum EnumOpeningBalanceStatus {
	/// <summary>残高があり既存行が無い。登録する</summary>
	New = 0,
	/// <summary>残高があり既存行がある。削除して登録し直す</summary>
	Overwrite = 1,
	/// <summary>残高が0で既存行がある。削除する</summary>
	Delete = 2,
	/// <summary>残高が0で既存行も無い。何もしない</summary>
	Skip = 3,
}

/// <summary>CSVの1列が担う意味。</summary>
public enum EnumOpeningBalanceField {
	Code, Name, Shime, Amount,
	Main, Henpin, Nebiki, Sonota, Tax1, Tax2, Tax3,
	Cash, Fee, Densai, Offset, Other,
	DueDay,
	TaxableAmount1, TaxableAmount2, TaxableAmount3,
}

/// <summary>
/// 区分ごとの対象テーブル・キー列・取引先マスタ。SQLの組み立てと表示文言の唯一の出所とする。
/// </summary>
public sealed record OpeningBalanceKindSpec(
	EnumOpeningBalanceKind Kind,
	string DisplayName,
	string TableName,
	string KeyColumn,
	string OwnerColumn,
	string MasterTableName,
	string OwnerLabel,
	string KeyLabel,
	bool IsClosingBased,
	bool IsPayable) {
	/// <summary>キー列の桁数。年月=6、年月日=8。</summary>
	public int KeyLength => IsClosingBased ? 8 : 6;
}

/// <summary>CSVの列定義。</summary>
public sealed record OpeningBalanceCsvColumn(EnumOpeningBalanceField Field, string Header) {
	/// <summary>内訳列（省略可能で、指定時は整合検査の対象になる）か。</summary>
	public bool IsBreakdown => Field is EnumOpeningBalanceField.Main or EnumOpeningBalanceField.Henpin
		or EnumOpeningBalanceField.Nebiki or EnumOpeningBalanceField.Sonota
		or EnumOpeningBalanceField.Tax1 or EnumOpeningBalanceField.Tax2 or EnumOpeningBalanceField.Tax3
		or EnumOpeningBalanceField.Cash or EnumOpeningBalanceField.Fee or EnumOpeningBalanceField.Densai
		or EnumOpeningBalanceField.Offset or EnumOpeningBalanceField.Other
		or EnumOpeningBalanceField.TaxableAmount1 or EnumOpeningBalanceField.TaxableAmount2 or EnumOpeningBalanceField.TaxableAmount3;
}

/// <summary>内訳の金額。すべて正値で保持する。</summary>
public sealed class OpeningBalanceBreakdown {
	public long Main { get; set; }
	public long Henpin { get; set; }
	public long Nebiki { get; set; }
	public long Sonota { get; set; }
	public long Tax1 { get; set; }
	public long Tax2 { get; set; }
	public long Tax3 { get; set; }
	public long Cash { get; set; }
	public long Fee { get; set; }
	public long Densai { get; set; }
	public long Offset { get; set; }
	public long Other { get; set; }
	/// <summary>税区分1の課税対象額（税抜）。参考値で、DebitTotal の計算には使わない。</summary>
	public long TaxableAmount1 { get; set; }
	public long TaxableAmount2 { get; set; }
	public long TaxableAmount3 { get; set; }

	/// <summary>売上側合計（SummaryUriKake.TotalSales / SummaryKaiKake.TotalShiire）。</summary>
	public long DebitTotal => Main - Henpin - Nebiki + Sonota + Tax1 + Tax2 + Tax3;
	/// <summary>入金側合計（SummaryUriKake.TotalIn / SummaryKaiKake.TotalOut）。</summary>
	public long CreditTotal => Cash + Fee + Densai + Offset + Other;
	/// <summary>内訳から算出した未回収残（正数）。</summary>
	public long NetAmount => DebitTotal - CreditTotal;

	public bool IsEmpty => Main == 0 && Henpin == 0 && Nebiki == 0 && Sonota == 0
		&& Tax1 == 0 && Tax2 == 0 && Tax3 == 0
		&& Cash == 0 && Fee == 0 && Densai == 0 && Offset == 0 && Other == 0
		&& TaxableAmount1 == 0 && TaxableAmount2 == 0 && TaxableAmount3 == 0;
}

/// <summary>CSVから読み取った1行（コード解決前）。</summary>
public sealed class OpeningBalanceCsvRow {
	public int LineNo { get; init; }
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string ShimeText { get; init; } = string.Empty;
	public long Amount { get; init; }
	public bool HasBreakdownColumn { get; init; }
	public OpeningBalanceBreakdown Breakdown { get; init; } = new();
	public string DueDay { get; init; } = string.Empty;
}

/// <summary>検証結果の1件。<see cref="IsWarning"/> が true なら登録を止めない。</summary>
public sealed class OpeningBalanceCsvError {
	public int LineNo { get; init; }
	public string ColumnName { get; init; } = string.Empty;
	public string Detail { get; init; } = string.Empty;
	public bool IsWarning { get; init; }
	public string Kind => IsWarning ? "警告" : "エラー";
}

/// <summary>解析結果。</summary>
public sealed class OpeningBalanceCsvParseResult {
	public List<OpeningBalanceCsvRow> Rows { get; } = [];
	public List<OpeningBalanceCsvError> Errors { get; } = [];
	public bool HasError => Errors.Exists(x => !x.IsWarning);
}

/// <summary>
/// コード解決した取引先。締日1/2/3の全列を持つ(複数締日対応 4.6)。
/// 最終締日(<see cref="ClosingDaySet.Resolve"/>の戻り値の最大値)は取込側で解決する。
/// </summary>
public sealed record OpeningBalanceOwner(long Id, string Code, string Name, int Shime1, int Shime2, int Shime3, int TenType);

/// <summary>取込1行の確定結果。</summary>
public sealed class OpeningBalanceEntry {
	public int LineNo { get; init; }
	public EnumOpeningBalanceStatus Status { get; init; }
	public long OwnerId { get; init; }
	public string OwnerCode { get; init; } = string.Empty;
	public string OwnerName { get; init; } = string.Empty;
	public long Amount { get; init; }
	public long BreakdownTotal { get; init; }
	public string Note { get; init; } = string.Empty;
	/// <summary>登録する Summary* 行。<see cref="EnumOpeningBalanceStatus.New"/> / <see cref="EnumOpeningBalanceStatus.Overwrite"/> のみ非null。</summary>
	public BaseDbClass? Record { get; init; }

	public string StatusText => Status switch {
		EnumOpeningBalanceStatus.New => "新規",
		EnumOpeningBalanceStatus.Overwrite => "上書き",
		EnumOpeningBalanceStatus.Delete => "削除",
		_ => "対象外",
	};
}

/// <summary>行の確定に必要な入力一式。</summary>
public sealed class OpeningBalanceBuildRequest {
	public EnumOpeningBalanceKind Kind { get; init; }
	/// <summary>期首行のキー。売掛・買掛は yyyyMM、請求・支払は yyyyMMdd。</summary>
	public string KeyDate { get; init; } = string.Empty;
	/// <summary>請求・支払の請求開始日 yyyyMMdd。</summary>
	public string DayFrom { get; init; } = string.Empty;
	/// <summary>期首年月日 yyyyMMdd。</summary>
	public string FiscalStartDate { get; init; } = string.Empty;
	/// <summary>請求・支払で画面が選択した締日。</summary>
	public int SelectedShime { get; init; }
	public IReadOnlyList<OpeningBalanceCsvRow> Rows { get; init; } = [];
	/// <summary>取引先コード（大文字小文字を区別しない）から解決した取引先。</summary>
	public IReadOnlyDictionary<string, OpeningBalanceOwner> Owners { get; init; }
		= new Dictionary<string, OpeningBalanceOwner>(StringComparer.OrdinalIgnoreCase);
	/// <summary>
	/// 自社締日(<c>MasterSysman.ShimeBi</c>)。<see cref="OpeningBalanceOwner.Shime1"/> が0(未設定)の取引先を
	/// <see cref="ClosingDaySet.Resolve"/> で解決する際のフォールバック値として使う(3.1、4.6)。
	/// このクラスはDB非依存なので、呼出側(サーバー/画面)が既存の照会経路で取得して渡すこと。
	/// </summary>
	public int OwnShime { get; init; }
	/// <summary>取引先Idごとの現在の期首残高（正数表示）。行が無い取引先は含めない。</summary>
	public IReadOnlyDictionary<long, long> ExistingAmounts { get; init; } = new Dictionary<long, long>();
}

/// <summary>行の確定結果一式。</summary>
public sealed class OpeningBalanceBuildResult {
	public List<OpeningBalanceEntry> Entries { get; } = [];
	public List<OpeningBalanceCsvError> Errors { get; } = [];
	public bool HasError => Errors.Exists(x => !x.IsWarning);

	public int NewCount => Entries.Count(x => x.Status == EnumOpeningBalanceStatus.New);
	public int OverwriteCount => Entries.Count(x => x.Status == EnumOpeningBalanceStatus.Overwrite);
	public int DeleteCount => Entries.Count(x => x.Status == EnumOpeningBalanceStatus.Delete);
	public int SkipCount => Entries.Count(x => x.Status == EnumOpeningBalanceStatus.Skip);
	public long TotalAmount => Entries.Sum(x => x.Amount);

	/// <summary>洗い替え対象の取引先Id。削除だけの行も含む（対象外の行は含めない）。</summary>
	public long[] OwnerIds => [.. Entries
		.Where(x => x.Status != EnumOpeningBalanceStatus.Skip)
		.Select(x => x.OwnerId)
		.Distinct()];

	/// <summary>登録する行。</summary>
	public List<BaseDbClass> Records => [.. Entries.Where(x => x.Record != null).Select(x => x.Record!)];
}

/// <summary>テンプレートCSVの1行。</summary>
public sealed record OpeningBalanceTemplateRow(
	string Code, string Name, int Shime1, long Amount, OpeningBalanceBreakdown? Breakdown, string DueDay);

/// <summary>取引先一覧の絞り込み。テンプレート出力は絞り、取込時のコード解決は絞らない。</summary>
[Flags]
public enum EnumOpeningBalanceOwnerScope {
	/// <summary>絞り込まない。取込時のコード解決に使う（該当しない取引先も引いて、専用の警告・エラーを出すため）</summary>
	All = 0,
	/// <summary>売掛・請求で <c>TenType IN (1,3)</c>（卸先・売仕店のみ）に絞る</summary>
	OwnerTypeFilter = 1,
	/// <summary>請求・支払で選択した締日に絞る</summary>
	ClosingFilter = 2,
	/// <summary>取引先コード範囲で絞る</summary>
	CodeRange = 4,
	/// <summary>既存の期首残がある取引先だけに絞る</summary>
	ExistingOnly = 8,
}

/// <summary>
/// 取引先一覧＋その取引先の既存の期首残高。<c>Msg101_Op_Query</c> の <c>ItemType</c> として
/// クライアント・サーバーの双方で解決できる共有DTOである。
/// </summary>
public sealed class OpeningBalanceOwnerRow {
	public long Id { get; set; }
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int Shime1 { get; set; }
	/// <summary>締日2。<see cref="ClosingDaySet.Resolve"/> による有効締日集合の解決に使う(4.6)。</summary>
	public int Shime2 { get; set; }
	/// <summary>締日3。同上。</summary>
	public int Shime3 { get; set; }
	public int TenType { get; set; }
	/// <summary>対象キー日付に既存行があれば1。</summary>
	public int HasExisting { get; set; }
	public long Balance { get; set; }
	/// <summary>TotalSales / TotalShiire</summary>
	public long DebitTotal { get; set; }
	/// <summary>TotalIn / TotalOut</summary>
	public long CreditTotal { get; set; }
	/// <summary>Uriage / Shiire</summary>
	public long Main { get; set; }
	public long Henpin { get; set; }
	public long Nebiki { get; set; }
	public long Sonota { get; set; }
	public long Tax1 { get; set; }
	public long Tax2 { get; set; }
	public long Tax3 { get; set; }
	public long Cash { get; set; }
	public long Fee { get; set; }
	public long Densai { get; set; }
	public long Offset { get; set; }
	public long Other { get; set; }
	public long TaxableAmount1 { get; set; }
	public long TaxableAmount2 { get; set; }
	public long TaxableAmount3 { get; set; }
	public string DueDay { get; set; } = string.Empty;

	/// <summary>既存の期首残高（正数＝未回収）。</summary>
	public long Amount => DebitTotal - CreditTotal;

	public OpeningBalanceOwner ToOwner() => new(Id, Code, Name, Shime1, Shime2, Shime3, TenType);

	public OpeningBalanceBreakdown ToBreakdown() => new() {
		Main = Main, Henpin = Henpin, Nebiki = Nebiki, Sonota = Sonota,
		Tax1 = Tax1, Tax2 = Tax2, Tax3 = Tax3,
		Cash = Cash, Fee = Fee, Densai = Densai, Offset = Offset, Other = Other,
		TaxableAmount1 = TaxableAmount1, TaxableAmount2 = TaxableAmount2, TaxableAmount3 = TaxableAmount3,
	};
}

/// <summary>
/// 期首残高CSV（標準形式）の解析・検証・行生成。WPFにもDBにも依存しない純ロジックとして
/// <c>Tests/TestServer</c> から直接検証する（<see cref="PaysakiClosingCheck"/> と同じ置き方）。
/// <para>
/// 標準形式は「先頭のコメント行（<c>#</c>）＋日本語1行ヘッダ＋データ行」で、金額は常に
/// **正数＝未回収残**である。内部の <c>Balance</c>（正＝未回収）への変換はここで行う。
/// 期首行は期首直前の1期間分の実績行であり、帳票側の <c>PreviousBalance</c> の SUM に自然に含まれる。
/// </para>
/// <para>
/// 内訳列（売上/仕入・返品・値引・その他売上・消費税1/2/3・課税対象額1/2/3）は
/// <c>SummaryUriKake</c> 等と同じく**税抜**で入力する（2026-09-01 全体設計 3.8）。
/// <c>消費税1/2/3</c> は税区分ごとに移行元で1回だけ丸めた確定額、
/// <c>課税対象額1/2/3</c> はその元になった税抜金額（インボイス制度の税率別内訳の参考値）であり、
/// 期首残高の合計計算（<see cref="OpeningBalanceBreakdown.DebitTotal"/>）には含めない。
/// 移行元（旧CVnet）が税込で集計していた場合は、取込前に税抜へ変換してから入力すること。
/// </para>
/// </summary>
public static class OpeningBalanceCsv {
	/// <summary>期首日が未設定のときの既定値。この値のままでは再計算の凍結ガードが働かない。</summary>
	public const string UnsetFiscalStartDate = "19010101";

	private static readonly OpeningBalanceKindSpec[] Specs = [
		new(EnumOpeningBalanceKind.UriKake, "売掛", nameof(SummaryUriKake), nameof(SummaryUriKake.DenMonth),
			nameof(SummaryUriKake.Id_Tokui), nameof(MasterTokui), "得意先", "期首残の年月", false, false),
		new(EnumOpeningBalanceKind.UriSei, "請求", nameof(SummaryUriSei), nameof(SummaryUriSei.DenDay),
			nameof(SummaryUriSei.Id_Tokui), nameof(MasterTokui), "得意先", "期首残の請求日", true, false),
		new(EnumOpeningBalanceKind.KaiKake, "買掛", nameof(SummaryKaiKake), nameof(SummaryKaiKake.DenMonth),
			nameof(SummaryKaiKake.Id_Shiire), nameof(MasterShiire), "仕入先", "期首残の年月", false, true),
		new(EnumOpeningBalanceKind.KaiShi, "支払", nameof(SummaryKaiShi), nameof(SummaryKaiShi.DenDay),
			nameof(SummaryKaiShi.Id_Shiire), nameof(MasterShiire), "仕入先", "期首残の支払日", true, true),
	];

	/// <summary>登録を許可するテーブル名。サーバー側の許可リストと同じ集合を返す。</summary>
	public static IReadOnlyList<string> AllowedTableNames => [.. Specs.Select(x => x.TableName)];

	public static OpeningBalanceKindSpec GetSpec(EnumOpeningBalanceKind kind) =>
		Specs.First(x => x.Kind == kind);

	public static OpeningBalanceKindSpec? FindSpecByTableName(string tableName) =>
		Specs.FirstOrDefault(x => string.Equals(x.TableName, tableName, StringComparison.OrdinalIgnoreCase));

	/// <summary>区分に応じた標準形式の列定義。</summary>
	public static IReadOnlyList<OpeningBalanceCsvColumn> GetColumns(EnumOpeningBalanceKind kind, bool includeBreakdown) {
		var spec = GetSpec(kind);
		List<OpeningBalanceCsvColumn> columns = [
			new(EnumOpeningBalanceField.Code, $"{spec.OwnerLabel}コード"),
			new(EnumOpeningBalanceField.Name, $"{spec.OwnerLabel}名"),
		];
		if (spec.IsClosingBased) {
			columns.Add(new(EnumOpeningBalanceField.Shime, "締日"));
		}
		columns.Add(new(EnumOpeningBalanceField.Amount, "期首残高"));
		if (includeBreakdown) {
			columns.Add(new(EnumOpeningBalanceField.Main, spec.IsPayable ? "仕入" : "売上"));
			columns.Add(new(EnumOpeningBalanceField.Henpin, "返品"));
			columns.Add(new(EnumOpeningBalanceField.Nebiki, "値引"));
			if (kind == EnumOpeningBalanceKind.UriSei) {
				columns.Add(new(EnumOpeningBalanceField.Sonota, "その他売上"));
			}
			columns.Add(new(EnumOpeningBalanceField.Tax1, "消費税1"));
			columns.Add(new(EnumOpeningBalanceField.Tax2, "消費税2"));
			columns.Add(new(EnumOpeningBalanceField.Tax3, "消費税3"));
			columns.Add(new(EnumOpeningBalanceField.Cash, spec.IsPayable ? "現金支払" : "現金入金"));
			columns.Add(new(EnumOpeningBalanceField.Fee, "振込手数料"));
			columns.Add(new(EnumOpeningBalanceField.Densai, "電子記録債権"));
			columns.Add(new(EnumOpeningBalanceField.Offset, spec.IsPayable ? "相殺支払" : "相殺入金"));
			columns.Add(new(EnumOpeningBalanceField.Other, spec.IsPayable ? "その他支払" : "その他入金"));
			columns.Add(new(EnumOpeningBalanceField.TaxableAmount1, "課税対象額1"));
			columns.Add(new(EnumOpeningBalanceField.TaxableAmount2, "課税対象額2"));
			columns.Add(new(EnumOpeningBalanceField.TaxableAmount3, "課税対象額3"));
		}
		if (spec.IsClosingBased) {
			columns.Add(new(EnumOpeningBalanceField.DueDay, spec.IsPayable ? "支払予定日" : "入金予定日"));
		}
		return columns;
	}

	/// <summary>
	/// 取引先一覧と、対象キー日付における既存の期首残高を引くSQL。
	/// <para>
	/// パラメータは @0=キー日付、@1=コード開始、@2=コード終了、@3=締日。
	/// コード範囲・締日は空文字／未使用でも同じSQLで通るようにしてあるので、呼び出し側は常に4つ渡す。
	/// </para>
	/// <para>
	/// 取込時のコード解決では <see cref="EnumOpeningBalanceOwnerScope.All"/> を使い、絞り込みを一切かけない。
	/// 絞り込むと、対象外の取引先が「マスタにありません」という誤ったエラーになり、
	/// 「卸先・売仕店ではありません」「締日が一致しません」という本来の指摘が出せなくなる。
	/// </para>
	/// </summary>
	public static string BuildOwnerQuerySql(EnumOpeningBalanceKind kind, EnumOpeningBalanceOwnerScope scope) {
		var spec = GetSpec(kind);
		var mainColumn = spec.IsPayable ? nameof(SummaryKaiKake.Shiire) : nameof(SummaryUriKake.Uriage);
		var debitColumn = spec.IsPayable ? nameof(SummaryKaiKake.TotalShiire) : nameof(SummaryUriKake.TotalSales);
		var creditColumn = spec.IsPayable ? nameof(SummaryKaiKake.TotalOut) : nameof(SummaryUriKake.TotalIn);
		var sonota = kind == EnumOpeningBalanceKind.UriSei ? "IFNULL(s.Sonota, 0)" : "0";
		var dueDay = kind switch {
			EnumOpeningBalanceKind.UriSei => "IFNULL(s.NyukinYoteiDay, '')",
			EnumOpeningBalanceKind.KaiShi => "IFNULL(s.ShiharaiYoteiDay, '')",
			_ => "''",
		};
		// TenType は MasterTokui にしか無い(MasterShiire は MasterTorihiki のまま)
		var tenType = spec.IsPayable ? "0" : "t.TenType";
		// 売掛・請求は卸先(1)・売仕店(3)だけを対象にする(倉庫・直営店を除く)
		var ownerTypeWhere = !spec.IsPayable && scope.HasFlag(EnumOpeningBalanceOwnerScope.OwnerTypeFilter)
			? " AND t.TenType IN (1, 3)" : string.Empty;
		// 締日での絞り込みは「取引先の最終締日(有効締日集合の最大値)が選択締日と一致するか」へ広げる(4.6)。
		// 前詰めバリデーション(3.2)により 0 でない締日は昇順に前詰めされているため、最終締日は
		// 「0でない最後の列」で求まる。全て0なら自社締日へフォールバックする(3.1)。
		var finalShimeSql = $"""
			CASE WHEN t.Shime3 <> 0 THEN t.Shime3
			     WHEN t.Shime2 <> 0 THEN t.Shime2
			     WHEN t.Shime1 <> 0 THEN t.Shime1
			     ELSE {ClosingDaySet.OwnShimeSubquerySql} END
			""".ReplaceLineEndings(" ");
		var closingWhere = spec.IsClosingBased && scope.HasFlag(EnumOpeningBalanceOwnerScope.ClosingFilter)
			? $" AND {finalShimeSql} = @3" : string.Empty;
		var codeWhere = scope.HasFlag(EnumOpeningBalanceOwnerScope.CodeRange)
			? " AND (@1 = '' OR t.Code >= @1) AND (@2 = '' OR t.Code <= @2)" : string.Empty;
		var existingWhere = scope.HasFlag(EnumOpeningBalanceOwnerScope.ExistingOnly)
			? " AND s.Id IS NOT NULL" : string.Empty;

		// Offset は SQLite の予約語なので必ず引用符で囲む
		return $"""
SELECT t.Id AS Id, t.Code AS Code, t.Name AS Name, t.Shime1 AS Shime1, t.Shime2 AS Shime2, t.Shime3 AS Shime3, {tenType} AS TenType,
       CASE WHEN s.Id IS NULL THEN 0 ELSE 1 END AS HasExisting,
       IFNULL(s.Balance, 0) AS Balance,
       IFNULL(s.{debitColumn}, 0) AS DebitTotal,
       IFNULL(s.{creditColumn}, 0) AS CreditTotal,
       IFNULL(s.{mainColumn}, 0) AS Main,
       IFNULL(s.Henpin, 0) AS Henpin,
       IFNULL(s.Nebiki, 0) AS Nebiki,
       {sonota} AS Sonota,
       IFNULL(s.Tax1, 0) AS Tax1,
       IFNULL(s.Tax2, 0) AS Tax2,
       IFNULL(s.Tax3, 0) AS Tax3,
       IFNULL(s.Cash, 0) AS Cash,
       IFNULL(s.Fee, 0) AS Fee,
       IFNULL(s.Densai, 0) AS Densai,
       IFNULL(s."Offset", 0) AS "Offset",
       IFNULL(s.Other, 0) AS Other,
       IFNULL(s.TaxableAmount1, 0) AS TaxableAmount1,
       IFNULL(s.TaxableAmount2, 0) AS TaxableAmount2,
       IFNULL(s.TaxableAmount3, 0) AS TaxableAmount3,
       {dueDay} AS DueDay
FROM {spec.MasterTableName} AS t
LEFT JOIN {spec.TableName} AS s ON s.{spec.OwnerColumn} = t.Id AND s.{spec.KeyColumn} = @0
WHERE 1 = 1{codeWhere}{ownerTypeWhere}{closingWhere}{existingWhere}
ORDER BY t.Code
""";
	}

	/// <summary>締日の表示文字列。99は「末日」。</summary>
	public static string FormatShime(int shime) =>
		shime > 28 ? "末日" : shime is >= 1 and <= 28 ? $"{shime}日" : string.Empty;

	/// <summary>テンプレートCSVの行（コメント行＋ヘッダ行＋データ行）を組み立てる。</summary>
	public static List<string> BuildTemplateLines(
		EnumOpeningBalanceKind kind, bool includeBreakdown,
		string fiscalStartDate, string keyDate, int selectedShime,
		IEnumerable<OpeningBalanceTemplateRow> rows) {
		var spec = GetSpec(kind);
		var columns = GetColumns(kind, includeBreakdown);
		var keyLabel = spec.IsClosingBased ? $"対象{(spec.IsPayable ? "支払日" : "請求日")}={FormatDate(keyDate)}" : $"対象年月={FormatDate(keyDate)}";
		var shimeLabel = spec.IsClosingBased ? $" / 締日={FormatShime(selectedShime)}" : string.Empty;
		List<string> lines = [
			$"# CV10 期首残高取込 / 区分={spec.DisplayName} / 期首日={FormatDate(fiscalStartDate)} / {keyLabel}{shimeLabel} / 金額は正数=未回収残",
			CsvText.BuildLine(columns.Select(x => x.Header)),
		];
		lines.AddRange(rows.Select(row => CsvText.BuildLine(columns.Select(column => FormatTemplateField(column, row)))));
		return lines;
	}

	private static string FormatTemplateField(OpeningBalanceCsvColumn column, OpeningBalanceTemplateRow row) {
		var breakdown = row.Breakdown;
		return column.Field switch {
			EnumOpeningBalanceField.Code => row.Code,
			EnumOpeningBalanceField.Name => row.Name,
			EnumOpeningBalanceField.Shime => FormatShime(row.Shime1),
			EnumOpeningBalanceField.Amount => row.Amount == 0 ? string.Empty : row.Amount.ToString(CultureInfo.InvariantCulture),
			EnumOpeningBalanceField.DueDay => FormatDate(row.DueDay),
			_ => breakdown == null ? string.Empty : FormatAmount(GetBreakdownValue(breakdown, column.Field)),
		};
	}

	private static string FormatAmount(long value) => value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

	private static long GetBreakdownValue(OpeningBalanceBreakdown breakdown, EnumOpeningBalanceField field) => field switch {
		EnumOpeningBalanceField.Main => breakdown.Main,
		EnumOpeningBalanceField.Henpin => breakdown.Henpin,
		EnumOpeningBalanceField.Nebiki => breakdown.Nebiki,
		EnumOpeningBalanceField.Sonota => breakdown.Sonota,
		EnumOpeningBalanceField.Tax1 => breakdown.Tax1,
		EnumOpeningBalanceField.Tax2 => breakdown.Tax2,
		EnumOpeningBalanceField.Tax3 => breakdown.Tax3,
		EnumOpeningBalanceField.Cash => breakdown.Cash,
		EnumOpeningBalanceField.Fee => breakdown.Fee,
		EnumOpeningBalanceField.Densai => breakdown.Densai,
		EnumOpeningBalanceField.Offset => breakdown.Offset,
		EnumOpeningBalanceField.Other => breakdown.Other,
		EnumOpeningBalanceField.TaxableAmount1 => breakdown.TaxableAmount1,
		EnumOpeningBalanceField.TaxableAmount2 => breakdown.TaxableAmount2,
		EnumOpeningBalanceField.TaxableAmount3 => breakdown.TaxableAmount3,
		_ => 0,
	};

	private static void SetBreakdownValue(OpeningBalanceBreakdown breakdown, EnumOpeningBalanceField field, long value) {
		switch (field) {
			case EnumOpeningBalanceField.Main: breakdown.Main = value; break;
			case EnumOpeningBalanceField.Henpin: breakdown.Henpin = value; break;
			case EnumOpeningBalanceField.Nebiki: breakdown.Nebiki = value; break;
			case EnumOpeningBalanceField.Sonota: breakdown.Sonota = value; break;
			case EnumOpeningBalanceField.Tax1: breakdown.Tax1 = value; break;
			case EnumOpeningBalanceField.Tax2: breakdown.Tax2 = value; break;
			case EnumOpeningBalanceField.Tax3: breakdown.Tax3 = value; break;
			case EnumOpeningBalanceField.Cash: breakdown.Cash = value; break;
			case EnumOpeningBalanceField.Fee: breakdown.Fee = value; break;
			case EnumOpeningBalanceField.Densai: breakdown.Densai = value; break;
			case EnumOpeningBalanceField.Offset: breakdown.Offset = value; break;
			case EnumOpeningBalanceField.Other: breakdown.Other = value; break;
			case EnumOpeningBalanceField.TaxableAmount1: breakdown.TaxableAmount1 = value; break;
			case EnumOpeningBalanceField.TaxableAmount2: breakdown.TaxableAmount2 = value; break;
			case EnumOpeningBalanceField.TaxableAmount3: breakdown.TaxableAmount3 = value; break;
		}
	}

	/// <summary>
	/// 標準形式のCSVテキストを解析する。<c>#</c> で始まる行と全列が空の行は読み飛ばす。
	/// 列は順不同で、ヘッダ名で対応付ける。
	/// </summary>
	public static OpeningBalanceCsvParseResult Parse(string text, EnumOpeningBalanceKind kind) {
		var result = new OpeningBalanceCsvParseResult();
		var spec = GetSpec(kind);
		List<CsvTextRow> rows;
		try {
			rows = CsvText.Parse(text);
		}
		catch (InvalidDataException ex) {
			result.Errors.Add(new OpeningBalanceCsvError { Detail = ex.Message });
			return result;
		}

		var contentRows = rows.Where(x => !IsSkippableRow(x)).ToList();
		if (contentRows.Count == 0) {
			result.Errors.Add(new OpeningBalanceCsvError { Detail = "データがありません。ヘッダ行と1行以上のデータが必要です。" });
			return result;
		}

		var headerRow = contentRows[0];
		var allColumns = GetColumns(kind, includeBreakdown: true);
		var fieldIndex = new Dictionary<EnumOpeningBalanceField, int>();
		for (var i = 0; i < headerRow.Fields.Count; i++) {
			var header = NormalizeHeader(headerRow.Fields[i]);
			if (header.Length == 0) continue;
			var column = allColumns.FirstOrDefault(x => NormalizeHeader(x.Header) == header);
			if (column == null) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = headerRow.LineNo,
					ColumnName = headerRow.Fields[i].Trim(),
					IsWarning = true,
					Detail = "認識できない列です。取込では無視します。",
				});
				continue;
			}
			if (!fieldIndex.TryAdd(column.Field, i)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = headerRow.LineNo,
					ColumnName = column.Header,
					Detail = "同じ列が2回あります。",
				});
			}
		}

		foreach (var required in new[] { EnumOpeningBalanceField.Code, EnumOpeningBalanceField.Amount }) {
			if (!fieldIndex.ContainsKey(required)) {
				var header = allColumns.First(x => x.Field == required).Header;
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = headerRow.LineNo,
					ColumnName = header,
					Detail = $"ヘッダ行に「{header}」列がありません。",
				});
			}
		}
		if (result.HasError) return result;

		var breakdownFields = allColumns.Where(x => x.IsBreakdown && fieldIndex.ContainsKey(x.Field)).ToList();
		foreach (var row in contentRows.Skip(1)) {
			var code = GetField(row, fieldIndex, EnumOpeningBalanceField.Code).Trim();
			if (code.Length == 0) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = $"{spec.OwnerLabel}コード",
					Detail = "コードが空です。",
				});
				continue;
			}

			var amountText = GetField(row, fieldIndex, EnumOpeningBalanceField.Amount);
			if (!TryParseAmount(amountText, out var amount, out var amountError)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = "期首残高",
					Detail = amountError,
				});
				continue;
			}

			var breakdown = new OpeningBalanceBreakdown();
			var breakdownFailed = false;
			foreach (var column in breakdownFields) {
				var value = GetField(row, fieldIndex, column.Field);
				if (!TryParseAmount(value, out var parsed, out var error)) {
					result.Errors.Add(new OpeningBalanceCsvError {
						LineNo = row.LineNo,
						ColumnName = column.Header,
						Detail = error,
					});
					breakdownFailed = true;
					continue;
				}
				SetBreakdownValue(breakdown, column.Field, parsed);
			}
			if (breakdownFailed) continue;

			var dueDay = string.Empty;
			if (fieldIndex.ContainsKey(EnumOpeningBalanceField.DueDay)) {
				var dueText = GetField(row, fieldIndex, EnumOpeningBalanceField.DueDay);
				if (!TryParseDate(dueText, out dueDay, out var dueError)) {
					var header = allColumns.First(x => x.Field == EnumOpeningBalanceField.DueDay).Header;
					result.Errors.Add(new OpeningBalanceCsvError {
						LineNo = row.LineNo,
						ColumnName = header,
						Detail = dueError,
					});
					continue;
				}
			}

			result.Rows.Add(new OpeningBalanceCsvRow {
				LineNo = row.LineNo,
				Code = code,
				Name = GetField(row, fieldIndex, EnumOpeningBalanceField.Name).Trim(),
				ShimeText = GetField(row, fieldIndex, EnumOpeningBalanceField.Shime).Trim(),
				Amount = amount,
				HasBreakdownColumn = breakdownFields.Count > 0,
				Breakdown = breakdown,
				DueDay = dueDay,
			});
		}

		if (result.Rows.Count == 0 && !result.HasError) {
			result.Errors.Add(new OpeningBalanceCsvError { Detail = "データ行がありません。" });
		}
		return result;
	}

	private static bool IsSkippableRow(CsvTextRow row) {
		if (row.Fields.All(string.IsNullOrWhiteSpace)) return true;
		var first = row.Fields[0].TrimStart('﻿').TrimStart();
		return first.StartsWith('#');
	}

	private static string GetField(CsvTextRow row, Dictionary<EnumOpeningBalanceField, int> map, EnumOpeningBalanceField field) =>
		map.TryGetValue(field, out var index) && index < row.Fields.Count ? row.Fields[index] : string.Empty;

	/// <summary>
	/// ヘッダ名を照合用に正規化する。BOM・空白・全角空白・かっこ書き（「得意先名(参考)」など）を落とし、
	/// 全角英数記号を半角へ揃える。
	/// </summary>
	public static string NormalizeHeader(string? value) {
		var text = ToHalfWidth(value ?? string.Empty).Replace("﻿", string.Empty).Trim();
		var open = text.IndexOf('(');
		if (open > 0) text = text[..open];
		return text.Replace(" ", string.Empty).Replace("\t", string.Empty).Trim();
	}

	/// <summary>
	/// 金額文字列を円単位の <see cref="long"/> へ正規化する。
	/// Excel経由を前提に、桁区切りカンマ・通貨記号・全角数字・会計表記の <c>(1200)</c> を受け付ける。
	/// 小数付きは拒否する。
	/// </summary>
	public static bool TryParseAmount(string? value, out long amount, out string error) {
		amount = 0;
		error = string.Empty;
		var original = value ?? string.Empty;
		var text = ToHalfWidth(original).Trim();
		if (text.Length == 0) return true;

		var negative = false;
		if (text.Length >= 2 && text[0] == '(' && text[^1] == ')') {
			negative = true;
			text = text[1..^1].Trim();
		}

		var builder = new StringBuilder();
		foreach (var ch in text) {
			if (ch is ',' or ' ' or '¥' or '￥' or '円') continue;
			builder.Append(ch);
		}
		text = builder.ToString();
		if (text.Length == 0) {
			error = $"数値として読めません。値='{original.Trim()}'";
			return false;
		}
		if (text.Contains('.')) {
			error = $"円単位の整数で入力してください。値='{original.Trim()}'";
			return false;
		}
		if (text[0] is '-' or '+') {
			if (text[0] == '-') negative = !negative;
			text = text[1..];
		}
		if (text.Length == 0 || !text.All(char.IsAsciiDigit)) {
			error = $"数値として読めません。値='{original.Trim()}'";
			return false;
		}
		if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) {
			error = $"金額が大きすぎます。値='{original.Trim()}'";
			return false;
		}
		amount = negative ? -parsed : parsed;
		return true;
	}

	/// <summary>日付文字列を yyyyMMdd へ正規化する。空文字は空のまま通す。</summary>
	public static bool TryParseDate(string? value, out string yyyymmdd, out string error) {
		yyyymmdd = string.Empty;
		error = string.Empty;
		var text = ToHalfWidth(value ?? string.Empty).Trim();
		if (text.Length == 0) return true;

		string[] formats = ["yyyyMMdd", "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d"];
		if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
			error = $"日付として読めません。値='{text}'";
			return false;
		}
		yyyymmdd = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	/// <summary>全角英数記号を半角へ揃える。全角空白は半角空白にする。</summary>
	public static string ToHalfWidth(string value) {
		var builder = new StringBuilder(value.Length);
		foreach (var ch in value) {
			builder.Append(ch switch {
				'　' => ' ',
				'－' or '−' or 'ー' => '-',
				>= '！' and <= '～' => (char)(ch - 0xfee0),
				_ => ch,
			});
		}
		return builder.ToString();
	}

	/// <summary>yyyyMM / yyyyMMdd を yyyy/MM / yyyy/MM/dd で表示する。</summary>
	public static string FormatDate(string? value) {
		var text = (value ?? string.Empty).Trim();
		return text.Length switch {
			6 => $"{text[..4]}/{text[4..6]}",
			8 => $"{text[..4]}/{text[4..6]}/{text[6..8]}",
			_ => text,
		};
	}

	/// <summary>
	/// 解析済みの行とコード解決結果から、登録する Summary* 行と行状態を確定する。
	/// </summary>
	public static OpeningBalanceBuildResult Build(OpeningBalanceBuildRequest request) {
		var result = new OpeningBalanceBuildResult();
		var spec = GetSpec(request.Kind);

		if (string.IsNullOrWhiteSpace(request.FiscalStartDate) || request.FiscalStartDate == UnsetFiscalStartDate) {
			result.Errors.Add(new OpeningBalanceCsvError {
				Detail = "期首日が未設定です。システム管理マスタで期首年月日を設定してください。",
			});
			return result;
		}
		if (!IsBeforeFiscalStart(request.KeyDate, request.FiscalStartDate, spec)) {
			result.Errors.Add(new OpeningBalanceCsvError {
				ColumnName = spec.KeyLabel,
				Detail = $"{spec.KeyLabel} {FormatDate(request.KeyDate)} は期首({FormatDate(request.FiscalStartDate)})以降です。期首より前を指定してください。",
			});
			return result;
		}

		var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var row in request.Rows) {
			if (seen.TryGetValue(row.Code, out var firstLine)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = $"{spec.OwnerLabel}コード",
					Detail = $"'{row.Code}' は {firstLine}行目にもあります。",
				});
				continue;
			}
			seen[row.Code] = row.LineNo;

			if (!request.Owners.TryGetValue(row.Code, out var owner)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = $"{spec.OwnerLabel}コード",
					Detail = $"コード '{row.Code}' は{spec.OwnerLabel}マスタにありません。",
				});
				continue;
			}

			if (!spec.IsPayable && owner.TenType is not (1 or 3)) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = $"{spec.OwnerLabel}コード",
					IsWarning = true,
					Detail = $"'{owner.Code}' は卸先・売仕店ではありません。{spec.DisplayName}残の対象として正しいか確認してください。",
				});
			}
			if (row.Name.Length > 0 && row.Name != owner.Name) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = $"{spec.OwnerLabel}名",
					IsWarning = true,
					Detail = $"マスタは「{owner.Name}」です。CSVの「{row.Name}」は取込に影響しません。",
				});
			}
			if (spec.IsClosingBased) {
				// 期首残高は取引先ごとに繰越額が1つなので、締日が増えても行を分けない。取引先の
				// 最終締日(有効締日集合の最大値、Resolveの戻り値は昇順のためその末尾)で1行だけ作る(4.6)。
				var ownerDays = ClosingDaySet.Resolve(owner.Shime1, owner.Shime2, owner.Shime3, request.OwnShime);
				var finalShime = ownerDays[^1];
				if (finalShime != request.SelectedShime) {
					result.Errors.Add(new OpeningBalanceCsvError {
						LineNo = row.LineNo,
						ColumnName = "締日",
						Detail = $"{spec.OwnerLabel}'{owner.Code}'の最終締日は{FormatShime(finalShime)}で、選択した締日({FormatShime(request.SelectedShime)})と一致しません。" +
							$"期首残高は最終締日({FormatShime(finalShime)})で取り込んでください。",
					});
					continue;
				}
			}

			var breakdown = row.Breakdown;
			var hasBreakdown = !breakdown.IsEmpty;
			if (hasBreakdown && breakdown.NetAmount != row.Amount) {
				result.Errors.Add(new OpeningBalanceCsvError {
					LineNo = row.LineNo,
					ColumnName = "期首残高",
					Detail = $"内訳から算出した残高 {breakdown.NetAmount:N0} が期首残高 {row.Amount:N0} と一致しません。",
				});
				continue;
			}

			var hasExisting = request.ExistingAmounts.TryGetValue(owner.Id, out var existingAmount);
			var isZero = row.Amount == 0 && !hasBreakdown;
			var status = isZero
				? hasExisting ? EnumOpeningBalanceStatus.Delete : EnumOpeningBalanceStatus.Skip
				: hasExisting ? EnumOpeningBalanceStatus.Overwrite : EnumOpeningBalanceStatus.New;

			result.Entries.Add(new OpeningBalanceEntry {
				LineNo = row.LineNo,
				Status = status,
				OwnerId = owner.Id,
				OwnerCode = owner.Code,
				OwnerName = owner.Name,
				Amount = row.Amount,
				BreakdownTotal = hasBreakdown ? breakdown.NetAmount : 0,
				Note = hasExisting ? $"現在 {existingAmount:N0}" : string.Empty,
				Record = status is EnumOpeningBalanceStatus.New or EnumOpeningBalanceStatus.Overwrite
					? CreateRecord(request, spec, owner, row, hasBreakdown)
					: null,
			});
		}

		return result;
	}

	/// <summary>
	/// 期首日から、期首行のキー日付と（請求・支払の）請求開始日を求める。
	/// <para>
	/// 売掛・買掛は期首年月の前月。請求・支払は「期首日の直前に来る締日(＝取引先の最終締日)」で、
	/// 請求開始日はその1つ前の締日の翌日とする。1つ前の締日は<see cref="ClosingDaySet.GetBillingPeriod"/>が
	/// 実装する3.3の <c>prev</c> の考え方（最小要素なら前月の最大要素、それ以外は同月内の1つ前）で求める。
	/// <paramref name="days"/> に1件しかない（単一締日）ときは常に「前月の同じ締日」扱いになるため、
	/// 現行(単一締日のみ)の結果と完全に一致する。
	/// </para>
	/// <para>
	/// これにより <c>CalcSummaryUriSei</c> の <c>previousBalance</c>（<c>DayTo &lt; 開始日</c>）へ確実に入る。
	/// 期首行は取引先ごとに1行しか作らないため(4.6)、<paramref name="days"/> は呼出側が画面全体の
	/// 締日候補(<see cref="ClosingDaySet.ResolveDistinctDays"/>の戻り値)を渡せば十分であり、
	/// 結果として実際の締期間より広いDayFromになる場合があるが、期首行の DayFrom は
	/// <c>PreviousBalance</c>（<c>DayTo</c>比較のみ）の材料でしかないため実害は無い(4.6)。
	/// </para>
	/// </summary>
	public static (string KeyDate, string DayFrom) GetDefaultKeyDate(
		EnumOpeningBalanceKind kind, string fiscalStartDate, IReadOnlyList<int> days, int shime) {
		var spec = GetSpec(kind);
		if (!DateTime.TryParseExact(fiscalStartDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fiscalStart)) {
			return (string.Empty, string.Empty);
		}

		if (!spec.IsClosingBased) {
			return (fiscalStart.AddMonths(-1).ToString("yyyyMM", CultureInfo.InvariantCulture), string.Empty);
		}
		if (shime is (< 1 or > 28) and not (int)EnumShime.DayLast) {
			return (string.Empty, string.Empty);
		}
		if (days == null || days.Count == 0 || !days.Contains(shime)) {
			return (string.Empty, string.Empty);
		}

		// 期首日の属する月の締日がまだ期首以降なら、1か月前の締日が「直前の締日」になる
		var closing = GetClosingDay(fiscalStart, shime);
		if (closing >= fiscalStart) {
			closing = GetClosingDay(fiscalStart.AddMonths(-1), shime);
		}
		var (dayFrom, _) = ClosingDaySet.GetBillingPeriod(closing.ToString("yyyyMM", CultureInfo.InvariantCulture), days, shime);
		return (closing.ToString("yyyyMMdd", CultureInfo.InvariantCulture), dayFrom);
	}

	/// <summary>指定月の締日。<c>SummaryDb.GetClosingDay</c> と同じ規則（99は末日、月末を超える指定は月末へ丸める）。</summary>
	public static DateTime GetClosingDay(DateTime month, int shime) {
		var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
		return new DateTime(month.Year, month.Month, shime == (int)EnumShime.DayLast ? lastDay : Math.Min(shime, lastDay));
	}

	/// <summary>キー日付が期首より前かどうか。売掛・買掛は年月、請求・支払は年月日で比較する。</summary>
	public static bool IsBeforeFiscalStart(string keyDate, string fiscalStartDate, OpeningBalanceKindSpec spec) {
		if (keyDate.Length != spec.KeyLength) return false;
		var boundary = spec.IsClosingBased ? fiscalStartDate : fiscalStartDate[..6];
		return string.CompareOrdinal(keyDate, boundary) < 0;
	}

	private static BaseDbClass CreateRecord(
		OpeningBalanceBuildRequest request, OpeningBalanceKindSpec spec,
		OpeningBalanceOwner owner, OpeningBalanceCsvRow row, bool hasBreakdown) {
		var breakdown = row.Breakdown;
		// 内訳が無い期首行は、繰越の起点として必要な合計だけを持つ（内訳は全て0）。
		var debit = hasBreakdown ? breakdown.DebitTotal : row.Amount;
		var credit = hasBreakdown ? breakdown.CreditTotal : 0;
		// Balance は当期間ネット(正=未回収)。4テーブル共通で Balance = DebitTotal - CreditTotal。
		var balance = debit - credit;

		return spec.Kind switch {
			EnumOpeningBalanceKind.UriKake => new SummaryUriKake {
				Id_Tokui = owner.Id,
				DenMonth = request.KeyDate,
				Balance = balance,
				TotalIn = credit,
				TotalSales = debit,
				Uriage = breakdown.Main,
				Henpin = breakdown.Henpin,
				Nebiki = breakdown.Nebiki,
				Sonota = breakdown.Sonota,
				Tax1 = breakdown.Tax1,
				Tax2 = breakdown.Tax2,
				Tax3 = breakdown.Tax3,
				TaxableAmount1 = breakdown.TaxableAmount1,
				TaxableAmount2 = breakdown.TaxableAmount2,
				TaxableAmount3 = breakdown.TaxableAmount3,
				Cash = breakdown.Cash,
				Fee = breakdown.Fee,
				Densai = breakdown.Densai,
				Offset = breakdown.Offset,
				Other = breakdown.Other,
			},
			EnumOpeningBalanceKind.UriSei => new SummaryUriSei {
				Id_Tokui = owner.Id,
				DenDay = request.KeyDate,
				DayFrom = request.DayFrom,
				DayTo = request.KeyDate,
				SeikyuNo = string.Empty,
				Renban = 0,
				NyukinYoteiDay = row.DueDay,
				Balance = balance,
				TotalIn = credit,
				TotalSales = debit,
				Uriage = breakdown.Main,
				Henpin = breakdown.Henpin,
				Nebiki = breakdown.Nebiki,
				Sonota = breakdown.Sonota,
				Tax1 = breakdown.Tax1,
				Tax2 = breakdown.Tax2,
				Tax3 = breakdown.Tax3,
				TaxableAmount1 = breakdown.TaxableAmount1,
				TaxableAmount2 = breakdown.TaxableAmount2,
				TaxableAmount3 = breakdown.TaxableAmount3,
				Cash = breakdown.Cash,
				Fee = breakdown.Fee,
				Densai = breakdown.Densai,
				Offset = breakdown.Offset,
				Other = breakdown.Other,
			},
			EnumOpeningBalanceKind.KaiKake => new SummaryKaiKake {
				Id_Shiire = owner.Id,
				DenMonth = request.KeyDate,
				Balance = balance,
				TotalOut = credit,
				TotalShiire = debit,
				Shiire = breakdown.Main,
				Henpin = breakdown.Henpin,
				Nebiki = breakdown.Nebiki,
				Sonota = breakdown.Sonota,
				Tax1 = breakdown.Tax1,
				Tax2 = breakdown.Tax2,
				Tax3 = breakdown.Tax3,
				TaxableAmount1 = breakdown.TaxableAmount1,
				TaxableAmount2 = breakdown.TaxableAmount2,
				TaxableAmount3 = breakdown.TaxableAmount3,
				Cash = breakdown.Cash,
				Fee = breakdown.Fee,
				Densai = breakdown.Densai,
				Offset = breakdown.Offset,
				Other = breakdown.Other,
			},
			_ => new SummaryKaiShi {
				Id_Shiire = owner.Id,
				DenDay = request.KeyDate,
				DayFrom = request.DayFrom,
				DayTo = request.KeyDate,
				ShiharaiYoteiDay = row.DueDay,
				Balance = balance,
				TotalOut = credit,
				TotalShiire = debit,
				Shiire = breakdown.Main,
				Henpin = breakdown.Henpin,
				Nebiki = breakdown.Nebiki,
				Sonota = breakdown.Sonota,
				Tax1 = breakdown.Tax1,
				Tax2 = breakdown.Tax2,
				Tax3 = breakdown.Tax3,
				TaxableAmount1 = breakdown.TaxableAmount1,
				TaxableAmount2 = breakdown.TaxableAmount2,
				TaxableAmount3 = breakdown.TaxableAmount3,
				Cash = breakdown.Cash,
				Fee = breakdown.Fee,
				Densai = breakdown.Densai,
				Offset = breakdown.Offset,
				Other = breakdown.Other,
			},
		};
	}
}
