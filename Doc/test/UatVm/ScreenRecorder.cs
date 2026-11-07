using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace UatVm;

/// <summary>
/// デモ動画用に、画面の指定範囲を ffmpeg（gdigrab）で録画する。
/// 録画中の操作の時刻と、強調したい要素の画面上の位置を「キュー」として JSON に残し、編集でテロップ・拡大に使う。
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable {
	private readonly Process _ffmpeg;
	private readonly Stopwatch _clock = new();
	private readonly List<Cue> _cues = [];
	private readonly string _cuePath;

	/// <summary>録画ファイル</summary>
	public string VideoPath { get; }

	/// <summary>キュー1件。t は録画開始からの秒、rect は録画範囲内のピクセル座標（x, y, w, h）。</summary>
	public sealed record Cue(double T, string Name, string? Text, double[]? Rect);

	private ScreenRecorder(Process ffmpeg, string videoPath) {
		_ffmpeg = ffmpeg;
		VideoPath = videoPath;
		_cuePath = Path.ChangeExtension(videoPath, ".cues.json");
	}

	/// <summary>
	/// 録画を始める。ffmpeg は環境変数 CV_FFMPEG、なければ PATH の ffmpeg を使う。
	/// </summary>
	public static async Task<ScreenRecorder> StartAsync(string videoPath, Int32Rect region, int fps = 30) {
		Directory.CreateDirectory(Path.GetDirectoryName(videoPath)!);
		// マウスを録画範囲の外（タスクバーにも掛からない位置）へ退避して、ホバー表示が映り込まないようにする
		SetCursorPos(region.X + region.Width - 10, region.Y + region.Height + 20);
		var exe =Environment.GetEnvironmentVariable("CV_FFMPEG") is { Length: > 0 } e ? e : "ffmpeg";
		var psi = new ProcessStartInfo(exe) {
			RedirectStandardInput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var a in new[] {
			"-y", "-hide_banner", "-loglevel", "warning",
			"-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "0",
			"-offset_x", region.X.ToString(), "-offset_y", region.Y.ToString(),
			"-video_size", $"{region.Width}x{region.Height}", "-i", "desktop",
			// 編集前提の中間ファイル。画質優先・高速エンコード。
			"-c:v", "libx264", "-preset", "ultrafast", "-crf", "12", "-pix_fmt", "yuv444p", videoPath,
		}) psi.ArgumentList.Add(a);
		var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg を起動できません。");
		var rec = new ScreenRecorder(p, videoPath);
		// gdigrab の最初のフレームが出るまで少し待ってから時計を開始する。
		await Task.Delay(700);
		rec._clock.Start();
		return rec;
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool SetCursorPos(int x, int y);

	/// <summary>録画開始からの経過秒</summary>
	public double Now => _clock.Elapsed.TotalSeconds;

	/// <summary>キューを記録する（位置なし）。</summary>
	public void Mark(string name, string? text = null) => _cues.Add(new Cue(Math.Round(Now, 3), name, text, null));

	/// <summary>要素の画面上の位置つきでキューを記録する。</summary>
	public void Mark(string name, FrameworkElement element, string? text = null, Point origin = default) {
		double[]? rect = null;
		if (element.IsVisible && PresentationSource.FromVisual(element) != null) {
			var tl = element.PointToScreen(new Point(0, 0));
			var br = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
			rect = [Math.Round(tl.X - origin.X), Math.Round(tl.Y - origin.Y), Math.Round(br.X - tl.X), Math.Round(br.Y - tl.Y)];
		}
		_cues.Add(new Cue(Math.Round(Now, 3), name, text, rect));
	}

	/// <summary>録画中の待ち時間（UIを止めない）</summary>
	public static Task Pause(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

	/// <summary>録画を止めてキューを書き出す。</summary>
	public async ValueTask DisposeAsync() {
		Mark("end");
		try {
			await _ffmpeg.StandardInput.WriteAsync("q");
			await _ffmpeg.StandardInput.FlushAsync();
		}
		catch (IOException) { /* 既に終了 */ }
		if (!_ffmpeg.WaitForExit(30_000)) _ffmpeg.Kill();
		var err = await _ffmpeg.StandardError.ReadToEndAsync();
		var json = JsonSerializer.Serialize(new { video = Path.GetFileName(VideoPath), cues = _cues, ffmpeg = err },
			new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
		await File.WriteAllTextAsync(_cuePath, json, new UTF8Encoding(false));
		_ffmpeg.Dispose();
	}
}

/// <summary>録画中に人が操作しているように見せる入力の補助</summary>
public static class DemoTyping {
	/// <summary>文字列を1文字ずつ設定する（バインディング経由で TextBox に打鍵のように表示される）。</summary>
	public static async Task TypeAsync(Action<string> set, string text, int msPerChar = 90) {
		var sb = new StringBuilder();
		foreach (var ch in text) {
			sb.Append(ch);
			set(sb.ToString());
			await Task.Delay(msPerChar);
		}
	}

	/// <summary>Visual ツリーから型と条件で要素を探す。</summary>
	public static T? Find<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject {
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var i = 0; i < count; i++) {
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T t && (predicate?.Invoke(t) ?? true)) return t;
			if (Find(child, predicate) is { } found) return found;
		}
		return null;
	}
}
