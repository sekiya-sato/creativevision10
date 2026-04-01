using Cvnet10Base;
using NLog;

namespace Cvnet10DomainLogic;

public partial class HhtProcess {
	ExDatabase _db;
	Logger _logger;

	public HhtProcess(ExDatabase db) {
		_db = db;
		_logger = LogManager.GetCurrentClassLogger();
	}
	/// <summary>
	/// マスタを変換して、固定長またはカンマ区切りの文字として返す。
	/// 
	/// </summary>
	/// <param name="isFix">isFix=trueの場合、コードはゼロ埋め8桁、名前はスペース埋め40桁で固定長に変換する。isFix=falseの場合は元の値をそのまま使用する。</param>
	/// <param name="outMasterMei">1=略称, 2=カナ, その他=正式名称</param>
	/// <returns></returns>
	public List<string> CreateMaster(bool isFix = false, int outMasterMei = 0) {
		List<MasterHht> masters = new();
		var sir = _db.Fetch<MasterShiire>();
		var sok = _db.Fetch<MasterTokui>("where IsZaiko=1 and TenType in (0,6)");
		var tan = _db.Fetch<MasterShain>();
		var tok = _db.Fetch<MasterTokui>("where TenType not in (6)");

		AddTorihikiMasters(masters, sir, "SIR", outMasterMei);
		AddTorihikiMasters(masters, sok, "SOK", outMasterMei);
		AddShainMasters(masters, tan, outMasterMei);
		AddTorihikiMasters(masters, tok, "TOK", outMasterMei);

		return CreateOutputLines(masters, isFix);
	}

	private static void AddTorihikiMasters<T>(List<MasterHht> masters, IEnumerable<T> source, string kubun, int outMasterMei)
		where T : MasterTorihiki {
		foreach (var item in source) {
			masters.Add(CreateMasterHht(
				kubun,
				item.Code ?? string.Empty,
				Sanitize(SelectName(null, item, outMasterMei)),
				Sanitize(item.Ryaku ?? string.Empty)));
		}
	}

	private static void AddShainMasters(List<MasterHht> masters, IEnumerable<MasterShain> source, int outMasterMei) {
		foreach (var item in source) {
			masters.Add(CreateMasterHht(
				"TAN",
				(item.Code ?? string.Empty).PadLeft(6, '0') + "  ",
				Sanitize(SelectName(item, null, outMasterMei)),
				string.Empty));
		}
	}

	private static MasterHht CreateMasterHht(string kubun, string code, string name, string nameOpt) {
		return new MasterHht {
			Kubun = kubun,
			Code = code,
			Name = name,
			NameOpt = nameOpt,
			Eol = "*",
		};
	}

	private static List<string> CreateOutputLines(IEnumerable<MasterHht> masters, bool isFix) {
		List<string> outList = new();
		foreach (var master in masters) {
			outList.Add(isFix ? ToFixedLength(master) : ToCsv(master));
		}

		return outList;
	}

	private static string ToFixedLength(MasterHht master) {
		return PadRightSpaces(master.Kubun, 3)
			+ PadLeftZeros(master.Code, 8)
			+ PadRightSpaces(master.Name, 40)
			+ PadRightSpaces(master.NameOpt, 40)
			+ (master.Eol ?? "*");
	}

	private static string ToCsv(MasterHht master) {
		return string.Join(",",
			EscapeCsv(master.Kubun ?? string.Empty),
			EscapeCsv(master.Code ?? string.Empty),
			EscapeCsv(master.Name ?? string.Empty),
			EscapeCsv(master.NameOpt ?? string.Empty),
			EscapeCsv(master.Eol ?? string.Empty));
	}

	private static string EscapeCsv(string s) {
		if (s == null) {
			return string.Empty;
		}

		return '"' + s.Replace("\"", "\"\"") + '"';
	}

	private static string SelectName(MasterShain? shain, MasterTorihiki? torihiki, int outMasterMei) {
		return outMasterMei switch {
			1 => !string.IsNullOrWhiteSpace(shain?.Ryaku) ? shain.Ryaku : !string.IsNullOrWhiteSpace(torihiki?.Ryaku) ? torihiki.Ryaku : torihiki?.Name ?? string.Empty,
			2 => !string.IsNullOrWhiteSpace(shain?.Kana) ? shain.Kana : !string.IsNullOrWhiteSpace(torihiki?.Kana) ? torihiki.Kana : torihiki?.Name ?? string.Empty,
			_ => torihiki?.Name ?? shain?.Name ?? string.Empty,
		};
	}

	private static string Sanitize(string s) {
		if (string.IsNullOrEmpty(s)) {
			return string.Empty;
		}

		var sb = new System.Text.StringBuilder(s.Length);
		foreach (var ch in s) {
			if (char.IsControl(ch)) {
				sb.Append(' ');
				continue;
			}

			switch (ch) {
				case '|':
				case '/':
				case '\\':
				case '"':
				case '\'':
				case '<':
				case '>':
				case '*':
					sb.Append(' ');
					break;
				default:
					sb.Append(ch);
					break;
			}
		}

		return sb.ToString();
	}

	private static string PadLeftZeros(string code, int width) {
		code ??= string.Empty;
		code = code.Trim();
		return code.Length >= width ? code[..width] : code.PadLeft(width, '0');
	}

	private static string PadRightSpaces(string s, int width) {
		s ??= string.Empty;
		return s.Length >= width ? s[..width] : s.PadRight(width, ' ');
	}
}
