using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using CvWpfclient.Models;

namespace UatVm.Scenarios;

/// <summary>
/// 全メニュー画面の表示崩れベースライン（描画のみ・更新操作なし）。
/// <para>
/// <see cref="MenuData.CreateDefault"/> のメニューを列挙し、MainMenuViewModel.DoMenu と同じ手順
/// （Activator生成→Title=メニュー名→InitParam/AddInfo設定）で実Viewを生成して非モーダル表示する。
/// 初期化（BaseWindow.OnContentRenderedのInitCommand）の完了を待ち、標準サイズと最小サイズで
/// JPG保存と <see cref="ScreenLayoutCheck.Inspect"/> を行い、画面ごとの結果を md / JSON へ出力する。
/// </para>
/// <para>
/// 最小サイズは XAML で MinWidth / MinHeight のどちらかが指定されている画面だけ撮る（未指定なら標準のみ）。
/// 寸法は表示後の実効値（BaseWindowの既定最小 640x480 適用後）を使う。
/// 表示崩れは不合格（Fail）にはせず Note と出力ファイルに記録する。開けなかった画面だけを Fail にする。
/// </para>
/// </summary>
public static class MenuLayoutScenario {
	/// <summary>出力先（--out）。未指定なら Doc/test/UatVm/out/menulayout-&lt;日時&gt;。</summary>
	public static string? OutputDirectory { get; set; }
	/// <summary>対象を絞る部分一致（--filter、View型名またはメニュー名）。未指定なら全画面。</summary>
	public static string? Filter { get; set; }

	const int InitTimeoutMs = 30_000;

	sealed record MenuEntry(string Path, string Header, Type ViewType, int InitParam, string? AddInfo, bool IsDialog);

	sealed class SizeResult {
		public string Mode { get; set; } = string.Empty;
		public double Width { get; set; }
		public double Height { get; set; }
		public string? Jpeg { get; set; }
		public int LongCellCount { get; set; }
		public List<ScreenLayoutCheck.Issue> Issues { get; set; } = [];
	}

	sealed class ScreenResult {
		public int No { get; set; }
		public string View { get; set; } = string.Empty;
		public string Menu { get; set; } = string.Empty;
		public int InitParam { get; set; }
		public bool Opened { get; set; }
		public string? Error { get; set; }
		public string? ResizeMode { get; set; }
		public double XamlMinWidth { get; set; }
		public double XamlMinHeight { get; set; }
		public double EffectiveMinWidth { get; set; }
		public double EffectiveMinHeight { get; set; }
		public bool InitTimedOut { get; set; }
		public List<string> Dialogs { get; set; } = [];
		public List<string> Notes { get; set; } = [];
		public List<SizeResult> Sizes { get; set; } = [];
	}

	public static async Task RunAsync(VmSession session) {
		var outDir = Path.GetFullPath(OutputDirectory
			?? Path.Combine("..", "Doc", "test", "UatVm", "out", "menulayout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
		var jpgDir = Path.Combine(outDir, "jpg");
		Directory.CreateDirectory(jpgDir);

		var entries = Enumerate(MenuData.CreateDefault(), string.Empty).ToList();
		// ロール別ショートカットは標準メニューと同じViewなので、View型＋InitParamで重複を除く（先頭の出現を採用）
		var unique = entries
			.GroupBy(x => (x.ViewType, x.InitParam))
			.Select(g => g.First())
			.Where(x => string.IsNullOrEmpty(Filter)
				|| x.ViewType.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
				|| x.Header.Contains(Filter, StringComparison.OrdinalIgnoreCase))
			.ToList();
		session.Note("対象", new { OutDir = outDir, MenuItems = entries.Count, Screens = unique.Count, Filter });

		var results = new List<ScreenResult>();
		var no = 0;
		foreach (var entry in unique) {
			no++;
			var result = await RunOneAsync(session, entry, no, jpgDir);
			results.Add(result);
			Console.WriteLine($"[menulayout] {no}/{unique.Count} {entry.ViewType.Name} opened={result.Opened} issues={string.Join("/", result.Sizes.Select(s => s.Issues.Count))}{(result.Error != null ? " error=" + result.Error : string.Empty)}");
			// 途中で落ちても結果が残るよう毎回書き出す
			WriteOutputs(outDir, results);
		}

		var failed = results.Where(x => !x.Opened).ToList();
		session.Check("全メニュー画面を開けた", failed.Count == 0,
			new { Total = results.Count, Failed = failed.Select(x => new { x.View, x.Error }) });
		session.Note("出力", new { OutDir = outDir, Total = results.Count, WithIssues = results.Count(x => x.Sizes.Any(s => s.Issues.Count > 0)) });
	}

	/// <summary>1画面分。例外は捕捉して結果へ残し、次の画面へ進む。</summary>
	static async Task<ScreenResult> RunOneAsync(VmSession session, MenuEntry entry, int no, string jpgDir) {
		var result = new ScreenResult {
			No = no,
			View = entry.ViewType.FullName ?? entry.ViewType.Name,
			Menu = entry.Path,
			InitParam = entry.InitParam,
		};
		var baseName = $"{no:000}_{entry.ViewType.Name}{(entry.InitParam != 0 ? "_p" + entry.InitParam : string.Empty)}";
		var before = Application.Current.Windows.Cast<Window>().ToHashSet();
		var dialogStart = session.Dialogs.Count;
		Window? view = null;
		try {
			view = Activator.CreateInstance(entry.ViewType) as Window
				?? throw new InvalidOperationException("Windowを生成できませんでした。");
			view.Title = entry.Header;
			if (view.DataContext is BaseViewModel vm0) {
				vm0.InitParam = entry.InitParam;
				vm0.AddInfo = entry.ViewType == typeof(CvWpfclient.Views._00System.SysGeneralMenteView)
					// 実メニューではテーブル選択画面の結果「テーブル名|件数」が入る。説明どおり MasterMeisho を対象にする
					? "MasterMeisho|100"
					: entry.AddInfo;
			}
			result.ResizeMode = view.ResizeMode.ToString();
			result.XamlMinWidth = view.MinWidth;
			result.XamlMinHeight = view.MinHeight;

			var rendered = new TaskCompletionSource();
			view.ContentRendered += (_, _) => rendered.TrySetResult();
			view.Show();
			await Task.WhenAny(rendered.Task, Task.Delay(10_000));
			if (!rendered.Task.IsCompleted) result.Notes.Add("ContentRendered が10秒以内に来ない");

			// InitCommand（BaseWindowが自動実行）の完了待ち
			var sw = Stopwatch.StartNew();
			await Task.Delay(200);
			while (HasRunningCommand(view.DataContext) && sw.ElapsedMilliseconds < InitTimeoutMs) {
				await Task.Delay(100);
			}
			if (HasRunningCommand(view.DataContext)) {
				result.InitTimedOut = true;
				result.Notes.Add($"初期化コマンドが{InitTimeoutMs / 1000}秒以内に終わらない（実行中のまま撮影）");
			}
			if (!view.IsVisible) {
				// 初期化失敗等でViewModelが自ら閉じた
				result.Notes.Add("初期化後にViewが閉じられた");
				result.Opened = false;
				result.Error = "初期化後にViewが閉じられた";
				return result;
			}
			result.Opened = true;
			result.EffectiveMinWidth = view.MinWidth;
			result.EffectiveMinHeight = view.MinHeight;

			result.Sizes.Add(await CaptureAsync(session, view, jpgDir, baseName + "_std", "標準"));

			var hasXamlMin = result.XamlMinWidth > 0 || result.XamlMinHeight > 0;
			if (hasXamlMin) {
				if (view.WindowState != WindowState.Normal) view.WindowState = WindowState.Normal;
				view.SizeToContent = SizeToContent.Manual;
				view.Width = view.MinWidth > 0 ? view.MinWidth : view.ActualWidth;
				view.Height = view.MinHeight > 0 ? view.MinHeight : view.ActualHeight;
				result.Sizes.Add(await CaptureAsync(session, view, jpgDir, baseName + "_min", "最小"));
			}
		}
		catch (Exception ex) {
			var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
			result.Error = $"{inner.GetType().Name}: {inner.Message}";
			session.Fail($"{baseName}:例外", inner.ToString());
		}
		finally {
			result.Dialogs.AddRange(session.Dialogs.Skip(dialogStart)
				.Select(d => $"{d.Request.Kind}/{d.Request.Button}->{d.Result}: {Oneline(d.Request.Message)}"));
			await CloseAllNewWindowsAsync(before, view, result);
		}
		var issueCount = result.Sizes.Sum(s => s.Issues.Count);
		session.Note($"{baseName}:結果", new {
			result.View, result.Menu, Sizes = result.Sizes.Select(s => new { s.Mode, s.Width, s.Height, Issues = s.Issues.Count, s.LongCellCount }),
			result.Dialogs, result.Notes, IssueCount = issueCount,
		});
		return result;
	}

	static async Task<SizeResult> CaptureAsync(VmSession session, Window view, string jpgDir, string name, string mode) {
		await Application.Current.Dispatcher.InvokeAsync(view.UpdateLayout, DispatcherPriority.ApplicationIdle);
		await Task.Delay(300);
		var size = new SizeResult { Mode = mode };
		await Application.Current.Dispatcher.InvokeAsync(() => {
			size.Width = Math.Round(view.ActualWidth);
			size.Height = Math.Round(view.ActualHeight);
			size.Jpeg = Path.GetFileName(ScreenLayoutCheck.SaveJpeg(view, jpgDir, name));
			var all = ScreenLayoutCheck.Inspect(view);
			size.Issues = all.Where(x => x.Kind != ScreenLayoutCheck.LongCellKind).ToList();
			size.LongCellCount = all.Count - size.Issues.Count;
		}, DispatcherPriority.ApplicationIdle);
		return size;
	}

	/// <summary>
	/// 対象Viewと、表示中に新たに開いたWindow（子画面等）を閉じる。閉じられない（確認でキャンセルされた）場合は隠して記録する。
	/// </summary>
	static async Task CloseAllNewWindowsAsync(HashSet<Window> before, Window? view, ScreenResult result) {
		var created = Application.Current.Windows.Cast<Window>().Where(w => !before.Contains(w)).ToList();
		// 子画面を先に閉じる
		foreach (var w in created.Where(w => !ReferenceEquals(w, view)).Concat(view != null ? [view] : [])) {
			try {
				if (!ReferenceEquals(w, view)) result.Notes.Add($"子画面を閉じた: {w.GetType().Name}");
				w.Close();
			}
			catch (Exception ex) {
				result.Notes.Add($"Close例外 {w.GetType().Name}: {ex.GetType().Name}: {ex.Message}");
			}
		}
		await Task.Delay(50);
		foreach (var w in created.Concat(view != null ? [view] : []).Distinct()) {
			if (w.IsLoaded && w.IsVisible) {
				result.Notes.Add($"閉じられずに残ったため非表示化: {w.GetType().Name}");
				try { w.Hide(); } catch (InvalidOperationException) { }
			}
		}
	}

	static bool HasRunningCommand(object? dataContext) {
		if (dataContext == null) return false;
		if (dataContext is IViewModelLifecycle lifecycle) return lifecycle.HasRunningCommand;
		foreach (var prop in dataContext.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (!typeof(IAsyncRelayCommand).IsAssignableFrom(prop.PropertyType)) continue;
			try {
				if (prop.GetValue(dataContext) is IAsyncRelayCommand { IsRunning: true }) return true;
			}
			catch (TargetInvocationException) { /* 取得不能なプロパティは無視 */ }
		}
		return false;
	}

	static IEnumerable<MenuEntry> Enumerate(IEnumerable<MenuData> items, string parent) {
		foreach (var item in items) {
			var path = string.IsNullOrEmpty(parent) ? item.Header : $"{parent} > {item.Header}";
			if (item.IsExecutable) {
				yield return new MenuEntry(path, item.Header, item.ViewType, item.InitParam, item.AddInfo, item.IsDialog);
			}
			if (item.SubItems is { Count: > 0 } sub) {
				foreach (var child in Enumerate(sub, path)) yield return child;
			}
		}
	}

	static string Oneline(string? text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

	static void WriteOutputs(string outDir, List<ScreenResult> results) {
		var json = JsonSerializer.Serialize(results, new JsonSerializerOptions {
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		});
		File.WriteAllText(Path.Combine(outDir, "menulayout.json"), json.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));

		var md = new StringBuilder();
		md.Append("# メニュー画面 表示崩れベースライン\r\n\r\n");
		md.Append($"- 実行: {DateTime.Now:yyyy/MM/dd HH:mm:ss}\r\n");
		md.Append($"- 画面数: {results.Count} / 開けなかった: {results.Count(x => !x.Opened)} / 崩れあり: {results.Count(x => x.Sizes.Any(s => s.Issues.Count > 0))}\r\n");
		md.Append($"- LongCellKind（{ScreenLayoutCheck.LongCellKind}）は件数のみ\r\n\r\n");
		md.Append("## 一覧\r\n\r\n| No | View | メニュー | 標準 | 最小 | LongCell | 備考 |\r\n|---|---|---|---|---|---|---|\r\n");
		foreach (var r in results) {
			string Cell(string mode) => r.Sizes.FirstOrDefault(s => s.Mode == mode) is { } s ? $"{s.Width}x{s.Height} 崩れ{s.Issues.Count}" : "-";
			var remark = r.Opened ? string.Join(" / ", r.Notes) : $"**開けない** {r.Error}";
			md.Append($"| {r.No} | {ShortName(r.View)} | {Esc(r.Menu)} | {Cell("標準")} | {Cell("最小")} | {r.Sizes.Sum(s => s.LongCellCount)} | {Esc(remark)} |\r\n");
		}
		md.Append("\r\n## 崩れ詳細\r\n");
		foreach (var r in results.Where(x => x.Sizes.Any(s => s.Issues.Count > 0) || x.Dialogs.Count > 0 || !x.Opened)) {
			md.Append($"\r\n### {r.No:000} {ShortName(r.View)}（{Esc(r.Menu)}）\r\n\r\n");
			if (r.Error != null) md.Append($"- エラー: {Esc(r.Error)}\r\n");
			foreach (var d in r.Dialogs) md.Append($"- ダイアログ: {Esc(d)}\r\n");
			foreach (var s in r.Sizes.Where(s => s.Issues.Count > 0)) {
				md.Append($"\r\n{s.Mode} {s.Width}x{s.Height}（{s.Jpeg}）\r\n\r\n| Kind | Element | Text | 位置 | 幅 | 必要幅 |\r\n|---|---|---|---|---|---|\r\n");
				foreach (var i in s.Issues) {
					md.Append($"| {i.Kind} | {Esc(i.Element)} | {Esc(i.Text)} | {i.Left},{i.Top} | {i.Width} | {i.Need} |\r\n");
				}
			}
		}
		File.WriteAllText(Path.Combine(outDir, "menulayout.md"), md.ToString(), new UTF8Encoding(false));
	}

	static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
	static string Esc(string? text) => Oneline(text).Replace("|", "\\|");
}
