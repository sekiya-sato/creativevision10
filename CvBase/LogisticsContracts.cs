using CvAsset;

namespace CvBase;

// 物流連携（WMS）の共通契約。仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md`。

/// <summary>
/// 物流連携のデータ種別（<see cref="TranLogisticsBatch.DataKind"/>）。値はファイル名・ファイル1列目にも使う。
/// </summary>
public static class LogisticsDataKind {
	/// <summary>商品SKUマスタ（送信）</summary>
	public const string PD = "PD";
	/// <summary>場所マスタ（送信）</summary>
	public const string BSY = "BSY";
	/// <summary>出荷指示（送信。配分）</summary>
	public const string ORDER = "ORDER";
	/// <summary>入荷予定（送信。発注・移動出庫）</summary>
	public const string STOCK = "STOCK";
	/// <summary>在庫（送信）</summary>
	public const string ZAIKO = "ZAIKO";
	/// <summary>出荷確定（受信）</summary>
	public const string ORDERFIX = "ORDERFIX";
	/// <summary>欠品（受信。確定数0の出荷確定と同じ扱い）</summary>
	public const string LACK = "LACK";
	/// <summary>入荷確定（受信）</summary>
	public const string STOCKFIX = "STOCKFIX";
	/// <summary>棚卸（受信）</summary>
	public const string INVENTORY = "INVENTORY";

	/// <summary>マスタ出力の種別</summary>
	public static readonly IReadOnlyList<string> MasterKinds = [PD, BSY];
	/// <summary>データ送信の種別</summary>
	public static readonly IReadOnlyList<string> SendKinds = [ORDER, STOCK, ZAIKO];
	/// <summary>受信の種別。ファイル名判定は長い名前から照合する（ORDERFIX と ORDER、STOCKFIX と STOCK の取り違え防止）</summary>
	public static readonly IReadOnlyList<string> ReceiveKinds = [ORDERFIX, STOCKFIX, INVENTORY, LACK];

	/// <summary>受信の種別か</summary>
	public static bool IsReceive(string kind) => ReceiveKinds.Contains(kind);

	/// <summary>
	/// 受信ファイル名から種別を判定する（旧CVと同じくファイル名に種別文字列を含むか）。判定できなければ空文字。
	/// </summary>
	public static string DetectReceiveKind(string fileName) {
		var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).ToUpperInvariant();
		return ReceiveKinds.FirstOrDefault(name.Contains) ?? string.Empty;
	}

	/// <summary>表示名</summary>
	public static string DisplayName(string kind) => kind switch {
		PD => "商品SKUマスタ",
		BSY => "場所マスタ",
		ORDER => "出荷指示",
		STOCK => "入荷予定",
		ZAIKO => "在庫",
		ORDERFIX => "出荷確定",
		LACK => "欠品",
		STOCKFIX => "入荷確定",
		INVENTORY => "棚卸",
		_ => kind,
	};
}

/// <summary>
/// 物流連携の設定（<see cref="MasterConfig"/> カテゴリ <see cref="MasterConfig.CategoryLogistics"/>）。
/// </summary>
/// <param name="LinkCode">連携先コード</param>
/// <param name="BaseFolder">サーバ上の連携フォルダ。空なら使用不可</param>
/// <param name="TargetSokoCodes">対象倉庫コード</param>
/// <param name="EncodingName">ファイルの文字コード名（shift_jis / utf-8）</param>
/// <param name="FileFormat">ファイル形式の識別子</param>
public sealed record LogisticsSettings(string LinkCode, string BaseFolder, string[] TargetSokoCodes, string EncodingName, string FileFormat) {
	/// <summary>送信フォルダ名</summary>
	public const string SendDir = "send";
	/// <summary>送信控えフォルダ名</summary>
	public const string SendBackupDir = "send_bak";
	/// <summary>受信フォルダ名</summary>
	public const string ReceiveDir = "recv";
	/// <summary>取込済み原文フォルダ名</summary>
	public const string ReceiveBackupDir = "recv_bak";
	/// <summary>作成中フォルダ名</summary>
	public const string WorkDir = "work";

	/// <summary>使用できない理由。使用できれば空文字</summary>
	public string UnusableReason =>
		string.IsNullOrWhiteSpace(BaseFolder) ? "連携フォルダ(LogisticsBaseFolder)が設定されていません。"
		: TargetSokoCodes.Length == 0 ? "対象倉庫(LogisticsTargetSoko)が設定されていません。"
		: !LogisticsFileFormat.IsSupported(FileFormat) ? $"ファイル形式 {FileFormat} には対応していません。"
		: string.Empty;

	/// <summary>
	/// 設定行から組み立てる。行が無い項目は初期値（<see cref="MasterConfig.CreateDefaultData"/> と同じ）を使う。
	/// </summary>
	public static LogisticsSettings From(IEnumerable<MasterConfig> configs) {
		var map = configs.Where(c => c.Category == MasterConfig.CategoryLogistics)
			.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => (g.First().Val ?? string.Empty).Trim());
		string Get(string name, string def) => map.TryGetValue(name, out var v) && v.Length > 0 ? v : def;
		var codes = Get(MasterConfig.NameLogisticsTargetSoko, string.Empty)
			.Split([',', '、', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct().ToArray();
		return new LogisticsSettings(
			Get(MasterConfig.NameLogisticsLinkCode, "WMS"),
			Get(MasterConfig.NameLogisticsBaseFolder, string.Empty),
			codes,
			Get(MasterConfig.NameLogisticsEncoding, "shift_jis"),
			Get(MasterConfig.NameLogisticsFileFormat, LogisticsFileFormat.Cv10V1));
	}
}

/// <summary>CSV 1データ行（ヘッダ行を除く）。<see cref="LineNo"/> はデータ行の1始まり</summary>
public sealed record LogisticsCsvLine(int LineNo, string RawText, IReadOnlyList<string> Fields);

/// <summary>
/// 物流連携のファイル形式 <c>cv10-v1</c>（仕様 5章）。
/// CSV・全項目ダブルクォート囲み・CRLF・ヘッダ行あり。数値は整数そのまま、日付は yyyyMMdd。
/// 文字コードの変換とファイル入出力は呼び出し側で行う。
/// </summary>
public static class LogisticsFileFormat {
	/// <summary>形式識別子 cv10-v1</summary>
	public const string Cv10V1 = "cv10-v1";

	/// <summary>対応している形式か</summary>
	public static bool IsSupported(string format) => format == Cv10V1;

	/// <summary>種別ごとの列見出し（列順の定義）</summary>
	public static IReadOnlyList<string> Columns(string kind) => kind switch {
		LogisticsDataKind.PD => ["データ区分", "商品CD", "品名", "略称", "カナ", "色CD", "色名", "サイズCD", "サイズ名", "JAN1", "JAN2", "JAN3",
			"ブランドCD", "シーズンCD", "アイテムCD", "原産国CD", "素材", "上代", "原価", "在庫管理区分", "消費税区分", "店頭投入日", "削除FLG"],
		LogisticsDataKind.BSY => ["データ区分", "場所区分", "場所CD", "名称", "カナ", "略称", "郵便番号", "住所1", "住所2", "住所3", "電話番号", "FAX番号", "削除FLG"],
		LogisticsDataKind.ORDER => ["データ区分", "区分", "指示伝票番号", "指示ID", "配分区分", "指示日", "納品日", "倉庫CD", "出荷先CD",
			"商品CD", "色CD", "サイズCD", "JAN", "指示数", "単価", "上代", "下代", "元伝票ID", "メモ"],
		LogisticsDataKind.STOCK => ["データ区分", "区分", "元伝票ID", "元伝票行No", "計上日", "入荷予定日", "入荷倉庫CD", "取引先CD",
			"商品CD", "色CD", "サイズCD", "JAN", "予定数", "単価", "上代", "メモ"],
		LogisticsDataKind.ZAIKO => ["データ区分", "基準日時", "倉庫CD", "商品CD", "色CD", "サイズCD", "JAN", "有効在庫", "積送中"],
		LogisticsDataKind.ORDERFIX or LogisticsDataKind.LACK => ["データ区分", "区分", "指示ID", "出荷日", "倉庫CD", "出荷先CD",
			"商品CD", "色CD", "サイズCD", "JAN", "確定数", "欠品数", "送り状NO", "メモ"],
		LogisticsDataKind.STOCKFIX => ["データ区分", "区分", "元伝票ID", "元伝票行No", "入荷日", "入荷倉庫CD", "取引先CD",
			"商品CD", "色CD", "サイズCD", "JAN", "入荷数", "メモ"],
		LogisticsDataKind.INVENTORY => ["データ区分", "棚卸日", "倉庫CD", "棚番", "商品CD", "色CD", "サイズCD", "JAN", "数量", "メモ"],
		_ => throw new ArgumentException($"未対応の種別です: {kind}", nameof(kind)),
	};

	/// <summary>種別の列番号（0始まり）。見出しが無ければ例外</summary>
	public static int ColumnIndex(string kind, string header) {
		var index = Columns(kind).ToList().IndexOf(header);
		return index >= 0 ? index : throw new ArgumentException($"{kind} に列 {header} はありません。", nameof(header));
	}

	/// <summary>全項目をダブルクォートで囲んだ1行を作る（項目内の " は "" にする。改行は空白へ置き換える）</summary>
	public static string BuildLine(IEnumerable<string?> fields) =>
		string.Join(",", fields.Select(f => "\"" + (f ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace("\"", "\"\"") + "\""));

	/// <summary>
	/// ファイル本文を作る（見出し行＋データ行、CRLF、末尾改行あり）。データ行の列数が定義と違えば例外。
	/// </summary>
	/// <returns>本文と、データ行ごとの1行文字列（送信行の RawText に使う）</returns>
	public static (string Text, IReadOnlyList<string> Lines) BuildFile(string kind, IEnumerable<IReadOnlyList<string?>> rows) {
		var columns = Columns(kind);
		var lines = new List<string>();
		foreach (var row in rows) {
			if (row.Count != columns.Count) {
				throw new ArgumentException($"{kind} の列数が定義({columns.Count})と違います: {row.Count}", nameof(rows));
			}
			lines.Add(BuildLine(row));
		}
		var text = string.Concat(new[] { BuildLine(columns) }.Concat(lines).Select(l => l + "\r\n"));
		return (text, lines);
	}

	/// <summary>
	/// 受信ファイル本文を解析する。1行目が見出し行と一致すれば読み飛ばす（見出し無しのファイルも受ける）。
	/// 空行は無視する。列数の違う行も返し、検査は呼び出し側で行う。
	/// </summary>
	/// <exception cref="InvalidDataException">引用符が閉じられていない場合</exception>
	public static List<LogisticsCsvLine> Parse(string kind, string text) {
		var columns = Columns(kind);
		text = (text ?? string.Empty).TrimStart('﻿');
		var physical = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		var rows = CsvText.Parse(text);
		var result = new List<LogisticsCsvLine>();
		var dataNo = 0;
		for (var i = 0; i < rows.Count; i++) {
			var fields = rows[i].Fields.Select(f => f.Trim()).ToList();
			if (fields.All(f => f.Length == 0)) {
				continue;
			}
			if (i == 0 && fields.Count == columns.Count && fields.SequenceEqual(columns)) {
				continue;
			}
			var lineIndex = rows[i].LineNo - 1;
			var raw = lineIndex >= 0 && lineIndex < physical.Length ? physical[lineIndex] : string.Join(",", fields);
			result.Add(new LogisticsCsvLine(++dataNo, raw, fields));
		}
		return result;
	}
}
