using Newtonsoft.Json;

namespace Cvnet10Asset;

public sealed partial class Common {
	/// <summary>
	/// 4世代バックアップ処理(現行ファイル+当日を含む過去3世代)
	/// </summary>
	/// <param name="targetFile"></param>
	/// <returns></returns>
	public static async Task<int> Backup4GeneAsync(string targetFile, CancellationToken cancellationToken = default) {
		if (string.IsNullOrWhiteSpace(targetFile))
			return 0;
		if (!File.Exists(targetFile))
			return 0;
		var dir = Path.GetDirectoryName(targetFile) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(dir))
			return 0;
		var fname = Path.GetFileName(targetFile);
		var nowstr = DateTime.Now.ToString("yyyyMMdd");
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var files = Directory.GetFiles(dir, fname + ".2???????.back"); // yyyyMMdd
			var index = Array.IndexOf(files, targetFile + "." + nowstr + ".back");
			if (index >= 0) return 0; // 現在日のコピーがあれば何もしない
			await Task.Run(() => File.Copy(targetFile, targetFile + "." + nowstr + ".back"), cancellationToken);
			Array.Sort(files);
			var cnt = 0;
			for (int i = files.Length - 1; i >= 0; i--) {
				cancellationToken.ThrowIfCancellationRequested();
				cnt++;
				if (cnt > 2) File.Delete(files[i]);
			}
		}
		catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine(ex, $"ファイルバックアップエラー fname={fname}:");
		}
		return 0;
	}
	/// <summary>
	/// ファイルからの読込
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="myObj"></param>
	/// <param name="fname"></param>
	/// <param name="fbackup"></param>
	/// <returns></returns>
	public static async Task<(bool Success, T? Obj)> LoadAsync<T>(string fname, CancellationToken cancellationToken = default) where T : new() {
		try {
			if (string.IsNullOrWhiteSpace(fname) || !File.Exists(fname))
				return (false, default);
			cancellationToken.ThrowIfCancellationRequested();
			var contents = await File.ReadAllTextAsync(fname, cancellationToken).ConfigureAwait(false);
			var myObj = JsonConvert.DeserializeObject<T>(contents);
			if (myObj is null)
				return (false, default);
			// 正常に読み込めたらバックアップ処理
			await Backup4GeneAsync(fname, cancellationToken).ConfigureAwait(false);
			return (true, myObj);
		}
		catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine(ex, $"ファイル読込エラー fname={fname}:");
			return (false, default);
		}
	}
	/// <summary>
	/// ファイルへの保存
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="myObj"></param>
	/// <param name="fname"></param>
	/// <param name="fbackup"></param>
	/// <returns></returns>
	public static async Task<bool> SaveAsync<T>(T myObj, string fname, bool isFlush = false, CancellationToken cancellationToken = default) where T : new() {
		try {
			if (string.IsNullOrWhiteSpace(fname))
				return false;
			var contents = JsonConvert.SerializeObject(myObj); // publicなプロパティが保存される
			await using var sw = new StreamWriter(fname, false);
			await sw.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
			if (isFlush) await sw.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine(ex, $"ファイル保存エラー fname={fname}:");
			return false;
		}
		return true;
	}
}