/*
# description
MoonPhaseIcon は指定日の月の満ち欠けを描画する部品です。
AstronomyEngine で月相角・照明率・月齢を計算し、外周の半円と明暗境界の半楕円で光っている部分を描きます。
大きさは Width / Height に合わせて伸縮します（正方形でない場合は短辺に合わせて中央に描画）。

# example
<controls:MoonPhaseIcon Width="30" Height="30"
	Date="{Binding MoonDate}" Note="{Binding MoonPhaseNote}" />
 */
using System.Windows;
using System.Windows.Media;
using CosineKitty;

namespace CvWpfclient.Helpers.Controls;

/// <summary>
/// 指定日の月相を描画する部品。計算結果は読取専用の依存関係プロパティで参照できる。
/// </summary>
public class MoonPhaseIcon : FrameworkElement {
	/// <summary>朔望月の平均日数</summary>
	private const double SynodicMonth = 29.530588853;

	private static readonly Brush DefaultLightBrush = CreateLightBrush();
	private static readonly Brush DefaultShadowBrush = CreateFrozenBrush(Color.FromRgb(0x24, 0x5C, 0x88));
	private static readonly Brush DefaultRimBrush = CreateFrozenBrush(Color.FromArgb(0x8C, 0x4B, 0x71, 0x8C));

	private Pen? _rimPen;
	private string? _autoToolTip;

	static MoonPhaseIcon() {
		// 小さく表示するとツールチップが主な情報源になるため、表示時間を延ばす。
		System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(
			typeof(MoonPhaseIcon), new FrameworkPropertyMetadata(20000));
	}

	public MoonPhaseIcon() {
		Recalculate();
	}

	#region 入力プロパティ

	/// <summary>月相を計算する日付。時刻は無視し、その日の正午（ローカル時刻）で計算する。</summary>
	public static readonly DependencyProperty DateProperty = DependencyProperty.Register(
		nameof(Date), typeof(DateTime), typeof(MoonPhaseIcon),
		new FrameworkPropertyMetadata(DateTime.Today, FrameworkPropertyMetadataOptions.AffectsRender, OnCalcPropertyChanged));

	public DateTime Date {
		get => (DateTime)GetValue(DateProperty);
		set => SetValue(DateProperty, value);
	}

	/// <summary>ツールチップの末尾に追加する文字列（旧暦など）。</summary>
	public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
		nameof(Note), typeof(string), typeof(MoonPhaseIcon),
		new PropertyMetadata(null, OnToolTipPropertyChanged));

	public string? Note {
		get => (string?)GetValue(NoteProperty);
		set => SetValue(NoteProperty, value);
	}

	/// <summary>true のとき、月相名・月齢・照明率をツールチップへ自動設定する。</summary>
	public static readonly DependencyProperty ShowDetailToolTipProperty = DependencyProperty.Register(
		nameof(ShowDetailToolTip), typeof(bool), typeof(MoonPhaseIcon),
		new PropertyMetadata(true, OnToolTipPropertyChanged));

	public bool ShowDetailToolTip {
		get => (bool)GetValue(ShowDetailToolTipProperty);
		set => SetValue(ShowDetailToolTipProperty, value);
	}

	/// <summary>太陽光が当たっている部分の塗り。</summary>
	public static readonly DependencyProperty LightBrushProperty = DependencyProperty.Register(
		nameof(LightBrush), typeof(Brush), typeof(MoonPhaseIcon),
		new FrameworkPropertyMetadata(DefaultLightBrush, FrameworkPropertyMetadataOptions.AffectsRender));

	public Brush? LightBrush {
		get => (Brush?)GetValue(LightBrushProperty);
		set => SetValue(LightBrushProperty, value);
	}

	/// <summary>影の部分の塗り。</summary>
	public static readonly DependencyProperty ShadowBrushProperty = DependencyProperty.Register(
		nameof(ShadowBrush), typeof(Brush), typeof(MoonPhaseIcon),
		new FrameworkPropertyMetadata(DefaultShadowBrush, FrameworkPropertyMetadataOptions.AffectsRender));

	public Brush? ShadowBrush {
		get => (Brush?)GetValue(ShadowBrushProperty);
		set => SetValue(ShadowBrushProperty, value);
	}

	/// <summary>外周線の色。null で外周線なし。</summary>
	public static readonly DependencyProperty RimBrushProperty = DependencyProperty.Register(
		nameof(RimBrush), typeof(Brush), typeof(MoonPhaseIcon),
		new FrameworkPropertyMetadata(DefaultRimBrush, FrameworkPropertyMetadataOptions.AffectsRender, OnRimChanged));

	public Brush? RimBrush {
		get => (Brush?)GetValue(RimBrushProperty);
		set => SetValue(RimBrushProperty, value);
	}

	/// <summary>外周線の太さ（直径24相当での太さ。表示サイズに比例する）。</summary>
	public static readonly DependencyProperty RimThicknessProperty = DependencyProperty.Register(
		nameof(RimThickness), typeof(double), typeof(MoonPhaseIcon),
		new FrameworkPropertyMetadata(0.8, FrameworkPropertyMetadataOptions.AffectsRender, OnRimChanged));

	public double RimThickness {
		get => (double)GetValue(RimThicknessProperty);
		set => SetValue(RimThicknessProperty, value);
	}

	#endregion

	#region 計算結果（読取専用）

	private static readonly DependencyPropertyKey PhaseAnglePropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(PhaseAngle), typeof(double), typeof(MoonPhaseIcon), new PropertyMetadata(0.0));
	/// <summary>月相角（0=新月, 90=上弦, 180=満月, 270=下弦）。</summary>
	public static readonly DependencyProperty PhaseAngleProperty = PhaseAnglePropertyKey.DependencyProperty;
	public double PhaseAngle => (double)GetValue(PhaseAngleProperty);

	private static readonly DependencyPropertyKey IlluminationPropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(Illumination), typeof(double), typeof(MoonPhaseIcon), new PropertyMetadata(0.0));
	/// <summary>照明率（0.0～1.0）。</summary>
	public static readonly DependencyProperty IlluminationProperty = IlluminationPropertyKey.DependencyProperty;
	public double Illumination => (double)GetValue(IlluminationProperty);

	private static readonly DependencyPropertyKey MoonAgePropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(MoonAge), typeof(double), typeof(MoonPhaseIcon), new PropertyMetadata(0.0));
	/// <summary>月齢（直前の新月からの経過日数）。</summary>
	public static readonly DependencyProperty MoonAgeProperty = MoonAgePropertyKey.DependencyProperty;
	public double MoonAge => (double)GetValue(MoonAgeProperty);

	private static readonly DependencyPropertyKey PhaseNamePropertyKey = DependencyProperty.RegisterReadOnly(
		nameof(PhaseName), typeof(string), typeof(MoonPhaseIcon), new PropertyMetadata(""));
	/// <summary>月相の呼び名（新月・三日月・上弦・満月・十六夜など）。</summary>
	public static readonly DependencyProperty PhaseNameProperty = PhaseNamePropertyKey.DependencyProperty;
	public string PhaseName => (string)GetValue(PhaseNameProperty);

	#endregion

	private static void OnCalcPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
		((MoonPhaseIcon)d).Recalculate();

	private static void OnToolTipPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
		((MoonPhaseIcon)d).UpdateToolTip();

	private static void OnRimChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
		((MoonPhaseIcon)d)._rimPen = null;

	/// <summary>Date から月相を計算し、読取専用プロパティとツールチップを更新する。</summary>
	private void Recalculate() {
		var result = Calculate(Date);
		SetValue(PhaseAnglePropertyKey, result.PhaseAngle);
		SetValue(IlluminationPropertyKey, result.Illumination);
		SetValue(MoonAgePropertyKey, result.MoonAge);
		SetValue(PhaseNamePropertyKey, GetPhaseName(result.PhaseAngle));
		UpdateToolTip();
	}

	private void UpdateToolTip() {
		if (!ShowDetailToolTip) {
			// 自動設定した値だけを解除し、利用側が設定した ToolTip は残す。
			if (_autoToolTip is not null && ReferenceEquals(ToolTip, _autoToolTip))
				ClearValue(ToolTipProperty);
			_autoToolTip = null;
			return;
		}
		var text = $"{PhaseName}　月齢 {MoonAge:F1}　照明率 {Illumination * 100:F0}%";
		if (!string.IsNullOrWhiteSpace(Note))
			text += Environment.NewLine + Note;
		_autoToolTip = text;
		ToolTip = text;
	}

	/// <summary>月相の計算結果。</summary>
	public readonly record struct MoonPhaseResult(double PhaseAngle, double Illumination, double MoonAge);

	/// <summary>指定日の正午（ローカル時刻）における月相を計算する。</summary>
	public static MoonPhaseResult Calculate(DateTime date) {
		var noon = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Local);
		var time = new AstroTime(noon.ToUniversalTime());

		double phase = Astronomy.MoonPhase(time);
		double fraction = Math.Clamp(Astronomy.Illumination(Body.Moon, time).phase_fraction, 0.0, 1.0);

		// 平均月齢から直前の新月付近を求め、前後2日の範囲で実際の新月時刻を検索する。
		double approxAge = phase / 360.0 * SynodicMonth;
		double age = approxAge;
		var newMoon = Astronomy.SearchMoonPhase(0.0, time.AddDays(-approxAge - 2.0), 4.0);
		if (newMoon is not null) {
			var found = time.ut - newMoon.ut;
			if (found >= 0 && found < SynodicMonth + 1.0)
				age = found;
		}
		return new MoonPhaseResult(phase, fraction, age);
	}

	/// <summary>月相角から呼び名を返す（月相角1日≒12.2°）。</summary>
	public static string GetPhaseName(double phase) => phase switch {
		< 6 or >= 354 => "新月",
		< 54 => "三日月",
		< 84 => "上弦前",
		< 96 => "上弦",
		< 168 => "上弦後",
		< 186 => "満月",
		< 198 => "十六夜",
		< 210 => "立待月",
		< 222 => "居待月",
		< 234 => "寝待月",
		< 264 => "下弦前",
		< 276 => "下弦",
		_ => "有明月",
	};

	protected override Size MeasureOverride(Size availableSize) {
		// サイズ未指定時は既定の 24×24 とする。
		double w = double.IsInfinity(availableSize.Width) ? 24 : Math.Min(24, availableSize.Width);
		double h = double.IsInfinity(availableSize.Height) ? 24 : Math.Min(24, availableSize.Height);
		double s = Math.Min(w, h);
		return new Size(s, s);
	}

	protected override void OnRender(DrawingContext dc) {
		double size = Math.Min(ActualWidth, ActualHeight);
		if (size <= 0)
			return;

		double r = size / 2.0;
		var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

		dc.DrawEllipse(ShadowBrush, null, center, r, r);

		if (LightBrush is not null) {
			var light = CreateLightGeometry(center, r, PhaseAngle, Illumination);
			if (light is not null)
				dc.DrawGeometry(LightBrush, null, light);
		}

		if (RimBrush is not null && RimThickness > 0) {
			double thickness = RimThickness * size / 24.0;
			_rimPen ??= CreateRimPen(RimBrush, thickness);
			if (Math.Abs(_rimPen.Thickness - thickness) > 0.001)
				_rimPen = CreateRimPen(RimBrush, thickness);
			dc.DrawEllipse(null, _rimPen, center, r - thickness / 2.0, r - thickness / 2.0);
		}
	}

	/// <summary>
	/// 光っている部分の形状を作る。外周の半円（満ちる月は右、欠ける月は左）と、
	/// 明暗境界（横半径 |k|·r の半楕円）で囲む。日本から見た向き（北が上）で描く。
	/// </summary>
	public static Geometry? CreateLightGeometry(Point center, double r, double phase, double fraction) {
		double f = Math.Clamp(fraction, 0.0, 1.0);
		if (f <= 0.001)
			return null;
		if (f >= 0.999)
			return new EllipseGeometry(center, r, r);

		bool waxing = phase < 180.0;
		// 明暗境界の水平位置係数（-1:左端, 0:中央, 1:右端）
		double k = waxing ? 1.0 - 2.0 * f : 2.0 * f - 1.0;

		var top = new Point(center.X, center.Y - r);
		var bottom = new Point(center.X, center.Y + r);

		var geometry = new StreamGeometry();
		using (var ctx = geometry.Open()) {
			ctx.BeginFigure(top, isFilled: true, isClosed: true);
			// 外周: 上端→下端（満ちる月は右回り、欠ける月は左回り）
			ctx.ArcTo(bottom, new Size(r, r), 0, false,
				waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
			// 明暗境界: 下端→上端（右側を通るなら左回り、左側を通るなら右回り）
			double rx = Math.Abs(k) * r;
			if (rx < 0.01)
				ctx.LineTo(top, true, true);
			else
				ctx.ArcTo(top, new Size(rx, r), 0, false,
					k > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
		}
		geometry.Freeze();
		return geometry;
	}

	private static Pen CreateRimPen(Brush brush, double thickness) {
		var pen = new Pen(brush, thickness);
		pen.Freeze();
		return pen;
	}

	private static Brush CreateFrozenBrush(Color color) {
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	private static Brush CreateLightBrush() {
		var brush = new LinearGradientBrush {
			StartPoint = new Point(0, 0),
			EndPoint = new Point(1, 1),
			MappingMode = BrushMappingMode.RelativeToBoundingBox,
		};
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF4, 0xB0), 0.0));
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD5, 0x1F), 0.55));
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xB0, 0x10), 1.0));
		brush.Freeze();
		return brush;
	}
}
