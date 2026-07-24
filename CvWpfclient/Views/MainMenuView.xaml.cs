using CvWpfclient.Helpers;
using CvWpfclient.Services;
using CvWpfclient.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CvWpfclient.Views;

public partial class MainMenuView : Window {
	private const double ChartLeftMargin = 32;
	private const double ChartRightMargin = 8;
	private const double ChartTopMargin = 12;
	private const int MaxVisibleLabels = 36;
	private const double MinimumXAxisLabelSpacing = 32;
	private readonly List<Point> _forecastPlotPoints = [];
	private readonly List<ForecastChartPoint> _forecastDataPoints = [];
	private double _forecastPlotBottom;
	private double _forecastPlotTop;
	private double _forecastPlotLeft;
	private double _forecastPlotRight;
	private Line? _forecastHoverGuideLine;
	private Ellipse? _forecastHoverMarker;
	private MainMenuViewModel? _forecastViewModel;

	public MainMenuView() {
		InitializeComponent();
		ApplyWindowIcon(App.MainThemeService.CurrentTheme);
		App.MainThemeService.MainThemeChanged += OnMainThemeChanged;
		DataContextChanged += MainMenuView_DataContextChanged;
		Loaded += MainMenuView_Loaded;
		AttachForecastViewModel(DataContext as MainMenuViewModel);
		Closed += MainMenuView_Closed;
	}

	private void OnMainThemeChanged(object? sender, MainTheme theme) {
		if (Dispatcher.CheckAccess()) {
			ApplyWindowIcon(theme);
			RenderForecastChart();
			return;
		}

		Dispatcher.Invoke(() => {
			ApplyWindowIcon(theme);
			RenderForecastChart();
		});
	}

	private void MainMenuView_Closed(object? sender, EventArgs e) {
		App.MainThemeService.MainThemeChanged -= OnMainThemeChanged;
		DataContextChanged -= MainMenuView_DataContextChanged;
		Loaded -= MainMenuView_Loaded;
		AttachForecastViewModel(null);
		if (DataContext is IDisposable disposable) {
			disposable.Dispose();
		}
		ClientLib.ExitAllWithoutMe(DataContext);
		Closed -= MainMenuView_Closed;
	}

	private void ApplyWindowIcon(MainTheme theme) {
		Icon = MainThemeService.GetWindowIcon(theme);
	}

	private void MainMenuView_Loaded(object sender, RoutedEventArgs e) {
		RenderForecastChart();
	}

	private void MainMenuView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
		AttachForecastViewModel(e.NewValue as MainMenuViewModel);
		RenderForecastChart();
	}

	private void AttachForecastViewModel(MainMenuViewModel? viewModel) {
		if (_forecastViewModel != null) {
			_forecastViewModel.PropertyChanged -= ForecastViewModel_PropertyChanged;
		}

		_forecastViewModel = viewModel;
		if (_forecastViewModel != null) {
			_forecastViewModel.PropertyChanged += ForecastViewModel_PropertyChanged;
		}
	}

	private void ForecastViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName is not nameof(MainMenuViewModel.ForecastChart) and not null) {
			return;
		}

		if (Dispatcher.CheckAccess()) {
			RenderForecastChart();
			return;
		}

		Dispatcher.BeginInvoke(RenderForecastChart);
	}

	private void ForecastCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
		RenderForecastChart();
	}

	private void RenderForecastChart() {
		if (!IsLoaded || ForecastCanvas.ActualWidth <= 0 || ForecastCanvas.ActualHeight <= 0) {
			return;
		}

		ForecastToolTip.IsOpen = false;
		ForecastCanvas.Children.Clear();
		_forecastPlotPoints.Clear();
		_forecastDataPoints.Clear();

		var chart = _forecastViewModel?.ForecastChart;
		if (chart == null || chart.Points.Count == 0) {
			return;
		}

		_forecastPlotLeft = ChartLeftMargin;
		_forecastPlotRight = Math.Max(_forecastPlotLeft + 1, ForecastCanvas.ActualWidth - ChartRightMargin);
		var labelStep = GetXAxisLabelStep(chart.Points.Count);
		var isDense = labelStep > 1;
		var bottomMargin = isDense ? 48d : 30d;
		_forecastPlotTop = ChartTopMargin;
		_forecastPlotBottom = Math.Max(_forecastPlotTop + 1, ForecastCanvas.ActualHeight - bottomMargin);

		var minTemperature = chart.MinTemperature;
		var maxTemperature = chart.MaxTemperature;
		if (Math.Abs(maxTemperature - minTemperature) < double.Epsilon) {
			minTemperature -= 5;
			maxTemperature += 5;
		}

		var lineBrush = GetChartBrush("MainMenuChartLineColor", Color.FromRgb(33, 150, 243));
		var fillBrush = GetChartBrush("MainMenuChartFillColor", Color.FromArgb(80, 33, 150, 243));
		var textBrush = GetChartBrush("MainMenuChartTextColor", Colors.Black);
		var gridBrush = CreateTransparentBrush(textBrush, 0.2);
		AddYAxis(minTemperature, maxTemperature, textBrush, gridBrush);

		for (var index = 0; index < chart.Points.Count; index++) {
			var point = chart.Points[index];
			var x = chart.Points.Count == 1
				? (_forecastPlotLeft + _forecastPlotRight) / 2
				: _forecastPlotLeft + ((_forecastPlotRight - _forecastPlotLeft) * index / (chart.Points.Count - 1));
			var y = MapTemperatureToY(point.Temperature, minTemperature, maxTemperature);
			_forecastPlotPoints.Add(new Point(x, y));
			_forecastDataPoints.Add(point);
		}

		var smoothPoints = CreateSmoothPoints(_forecastPlotPoints);
		var fillPoints = new PointCollection { new Point(_forecastPlotPoints[0].X, _forecastPlotBottom) };
		foreach (var smoothPoint in smoothPoints) {
			fillPoints.Add(smoothPoint);
		}
		fillPoints.Add(new Point(_forecastPlotPoints[^1].X, _forecastPlotBottom));
		ForecastCanvas.Children.Add(new Polygon { Fill = fillBrush, Points = fillPoints });
		ForecastCanvas.Children.Add(new Polyline {
			Stroke = lineBrush,
			StrokeLineJoin = PenLineJoin.Round,
			StrokeThickness = 2,
			Points = smoothPoints,
		});

		foreach (var point in _forecastPlotPoints) {
			var marker = new Ellipse {
				Fill = lineBrush,
				Height = 6,
				Stroke = lineBrush,
				StrokeThickness = 1,
				Width = 6,
			};
			Canvas.SetLeft(marker, point.X - marker.Width / 2);
			Canvas.SetTop(marker, point.Y - marker.Height / 2);
			ForecastCanvas.Children.Add(marker);
		}

		AddXAxisLabels(chart.Points, labelStep, isDense, textBrush);
		AddHoverVisuals(lineBrush);
	}

	private void AddYAxis(double minTemperature, double maxTemperature, Brush textBrush, Brush gridBrush) {
		for (var temperature = minTemperature; temperature <= maxTemperature + 0.001; temperature += 5) {
			var y = MapTemperatureToY(temperature, minTemperature, maxTemperature);
			ForecastCanvas.Children.Add(new Line {
				Stroke = gridBrush,
				StrokeThickness = 1,
				X1 = _forecastPlotLeft,
				X2 = _forecastPlotRight,
				Y1 = y,
				Y2 = y,
			});

			var label = new TextBlock {
				FontSize = 10,
				Foreground = textBrush,
				Text = temperature.ToString("0"),
				TextAlignment = TextAlignment.Right,
				Width = ChartLeftMargin - 8,
			};
			Canvas.SetLeft(label, 0);
			Canvas.SetTop(label, y - 7);
			ForecastCanvas.Children.Add(label);
		}
	}

	private int GetXAxisLabelStep(int pointCount) {
		if (pointCount <= 2) {
			return 1;
		}

		var plotWidth = _forecastPlotRight - _forecastPlotLeft;
		var maxVisibleLabelsByWidth = Math.Max(2, (int)Math.Floor(plotWidth / MinimumXAxisLabelSpacing) + 1);
		var maxVisibleLabels = Math.Min(MaxVisibleLabels, maxVisibleLabelsByWidth);
		return Math.Max(1, (int)Math.Ceiling((double)(pointCount - 1) / (maxVisibleLabels - 1)));
	}

	private void AddXAxisLabels(IReadOnlyList<ForecastChartPoint> points, int labelStep, bool isDense, Brush textBrush) {
		var labelWidth = isDense ? 46d : 56d;
		for (var index = 0; index < points.Count; index += labelStep) {
			AddXAxisLabel(index);
		}

		if ((points.Count - 1) % labelStep != 0) {
			AddXAxisLabel(points.Count - 1);
		}

		void AddXAxisLabel(int index) {
			var point = _forecastPlotPoints[index];
			var label = new TextBlock {
				FontSize = isDense ? 9 : 11,
				Foreground = textBrush,
				Text = points[index].TimeLabel,
				TextAlignment = TextAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Width = labelWidth,
			};
			if (isDense) {
				label.RenderTransform = new RotateTransform(45);
				label.RenderTransformOrigin = new Point(0.5, 0);
			}
			Canvas.SetLeft(label, point.X - labelWidth / 2);
			Canvas.SetTop(label, _forecastPlotBottom + 3);
			ForecastCanvas.Children.Add(label);
		}
	}

	private void AddHoverVisuals(Brush lineBrush) {
		_forecastHoverGuideLine = new Line {
			Stroke = CreateTransparentBrush(lineBrush, 0.65),
			StrokeDashArray = new DoubleCollection { 2, 2 },
			StrokeThickness = 1,
			Visibility = Visibility.Collapsed,
			Y1 = _forecastPlotTop,
			Y2 = _forecastPlotBottom,
		};
		_forecastHoverMarker = new Ellipse {
			Fill = GetChartBrush("MainMenuChartBackgroundBrush", Colors.White),
			Height = 10,
			Stroke = lineBrush,
			StrokeThickness = 2,
			Visibility = Visibility.Collapsed,
			Width = 10,
		};
		ForecastCanvas.Children.Add(_forecastHoverGuideLine);
		ForecastCanvas.Children.Add(_forecastHoverMarker);
	}

	private void ForecastCanvas_MouseMove(object sender, MouseEventArgs e) {
		if (_forecastPlotPoints.Count == 0 || _forecastHoverGuideLine == null || _forecastHoverMarker == null) {
			return;
		}

		var mousePosition = e.GetPosition(ForecastCanvas);
		if (mousePosition.X < _forecastPlotLeft || mousePosition.X > _forecastPlotRight || mousePosition.Y < _forecastPlotTop || mousePosition.Y > _forecastPlotBottom) {
			HideForecastToolTip();
			return;
		}

		var index = _forecastPlotPoints.Count == 1
			? 0
			: Math.Clamp((int)Math.Round((mousePosition.X - _forecastPlotLeft) / (_forecastPlotRight - _forecastPlotLeft) * (_forecastPlotPoints.Count - 1)), 0, _forecastPlotPoints.Count - 1);
		var point = _forecastPlotPoints[index];
		var forecast = _forecastDataPoints[index];

		_forecastHoverGuideLine.X1 = point.X;
		_forecastHoverGuideLine.X2 = point.X;
		_forecastHoverGuideLine.Visibility = Visibility.Visible;
		Canvas.SetLeft(_forecastHoverMarker, point.X - _forecastHoverMarker.Width / 2);
		Canvas.SetTop(_forecastHoverMarker, point.Y - _forecastHoverMarker.Height / 2);
		_forecastHoverMarker.Visibility = Visibility.Visible;

		ForecastToolTipTime.Text = forecast.DateTime.ToString("M月d日 H時");
		ForecastToolTipTemperature.Text = $"気温 {forecast.Temperature:F1}℃";
		ForecastToolTip.HorizontalOffset = point.X + 10 + 140 > ForecastCanvas.ActualWidth ? point.X - 150 : point.X + 10;
		ForecastToolTip.VerticalOffset = point.Y < 56 ? point.Y + 10 : point.Y - 56;
		ForecastToolTip.IsOpen = true;
	}

	private void ForecastCanvas_MouseLeave(object sender, MouseEventArgs e) {
		HideForecastToolTip();
	}

	private void HideForecastToolTip() {
		ForecastToolTip.IsOpen = false;
		if (_forecastHoverGuideLine != null) {
			_forecastHoverGuideLine.Visibility = Visibility.Collapsed;
		}
		if (_forecastHoverMarker != null) {
			_forecastHoverMarker.Visibility = Visibility.Collapsed;
		}
	}

	private double MapTemperatureToY(double temperature, double minTemperature, double maxTemperature) {
		return _forecastPlotBottom - ((temperature - minTemperature) / (maxTemperature - minTemperature) * (_forecastPlotBottom - _forecastPlotTop));
	}

	private static PointCollection CreateSmoothPoints(IReadOnlyList<Point> source) {
		if (source.Count <= 2) {
			var directPoints = new PointCollection();
			foreach (var point in source) {
				directPoints.Add(point);
			}
			return directPoints;
		}

		const int samplesPerSegment = 8;
		var points = new PointCollection();
		for (var index = 0; index < source.Count - 1; index++) {
			var point0 = index == 0 ? source[index] : source[index - 1];
			var point1 = source[index];
			var point2 = source[index + 1];
			var point3 = index + 2 < source.Count ? source[index + 2] : point2;
			for (var sample = 0; sample < samplesPerSegment; sample++) {
				var t = sample / (double)samplesPerSegment;
				points.Add(InterpolateCatmullRom(point0, point1, point2, point3, t));
			}
		}
		points.Add(source[^1]);
		return points;
	}

	private static Point InterpolateCatmullRom(Point point0, Point point1, Point point2, Point point3, double t) {
		var t2 = t * t;
		var t3 = t2 * t;
		return new Point(
			0.5 * ((2 * point1.X) + (-point0.X + point2.X) * t + (2 * point0.X - 5 * point1.X + 4 * point2.X - point3.X) * t2 + (-point0.X + 3 * point1.X - 3 * point2.X + point3.X) * t3),
			0.5 * ((2 * point1.Y) + (-point0.Y + point2.Y) * t + (2 * point0.Y - 5 * point1.Y + 4 * point2.Y - point3.Y) * t2 + (-point0.Y + 3 * point1.Y - 3 * point2.Y + point3.Y) * t3));
	}

	private Brush GetChartBrush(string key, Color fallback) {
		return TryFindResource(key) switch {
			Brush brush => brush,
			Color color => new SolidColorBrush(color),
			_ => new SolidColorBrush(fallback),
		};
	}

	private static Brush CreateTransparentBrush(Brush source, double opacity) {
		var brush = source.Clone();
		brush.Opacity = opacity;
		return brush;
	}

	private void MenuTree_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key != Key.Enter) {
			return;
		}
		if (DataContext is not MainMenuViewModel vm) {
			return;
		}
		if (vm.SelectedMenu?.ViewType == null) {
			return;
		}
		if (vm.DoMenuCommand.CanExecute(null)) {
			vm.DoMenuCommand.Execute(null);
			e.Handled = true;
		}
	}
}
