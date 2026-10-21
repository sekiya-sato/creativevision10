using CvAsset;
using CvWpfclient.Helpers;
using System.IO;
using System.Text;

namespace CvWpfclient.Services;

/// <summary>
/// メニューのお気に入り（Doc/spec/2026-10-20_メニュー実行権限と個人メニュー_最小変更設計案.md §4.2）。
/// <para>
/// このPCの <c>%LocalAppData%\CreativeVision10\Menu\favorites_ログインID.json</c> に FunctionId の配列を表示順で保存する。
/// 共用PCでもログインIDごとに分かれ、別のPCには引き継がない（J5）。
/// </para>
/// </summary>
public static class MenuFavoriteStore {
	/// <summary>保存フォルダ。UATでは利用者の実ファイルを汚さないよう一時フォルダへ差し替える</summary>
	public static string Directory { get; set; } = Path.Combine(ClientLib.GetDataDir(), "Menu");

	/// <summary>ログインIDごとの保存ファイル</summary>
	public static string GetFilePath(string loginId) {
		var invalid = Path.GetInvalidFileNameChars();
		var name = string.Concat(loginId.Select(c => invalid.Contains(c) ? '_' : c));
		return Path.Combine(Directory, $"favorites_{name}.json");
	}

	/// <summary>読み込む。ファイルが無ければ空。壊れていれば例外（呼び出し側で通知し、上書きしない）</summary>
	public static List<string> Load(string loginId) {
		var path = GetFilePath(loginId);
		if (!File.Exists(path)) {
			return [];
		}
		return Common.DeserializeObject<List<string>>(File.ReadAllText(path, Encoding.UTF8)) ?? [];
	}

	/// <summary>一時ファイルへ書いてから置き換え、書込み途中で既存の保存を壊さない</summary>
	public static void Save(string loginId, IEnumerable<string> functionIds) {
		var path = GetFilePath(loginId);
		System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temp = path + ".tmp";
		File.WriteAllText(temp, Common.SerializeObject(functionIds.ToList()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(temp, path, overwrite: true);
	}
}
