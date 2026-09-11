using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._02Yosan;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CvWpfclient.Views._02Yosan;

public partial class DailyShopBudgetQueryView : Helpers.BaseWindow {
	/// <summary>列名→固定幅(px)。ここに無い列名は店舗列とみなす。</summary>
	static readonly Dictionary<string, double> ColumnWidths = new() {
		["日付"] = 110,
		["曜日"] = 52,
		["売上計"] = 112,
		["予算計"] = 112,
		["前年売上計"] = 112,
		["予算比"] = 88,
		["前年売上比"] = 88,
	};

	static readonly HashSet<string> PercentColumns = ["予算比", "前年売上比"];

	// 旧画面の配色に合わせた固定色。ダーク/ライト双方で背景色そのものは変えず、
	// 文字色を固定の濃色にすることで両テーマで視認性を保つ（本画面のみの特例）。
	static readonly Brush SalesColumnBrush = FrozenBrush(0xFF, 0xF9, 0xC4);
	static readonly Brush BudgetColumnBrush = FrozenBrush(0xDC, 0xED, 0xC8);
	static readonly Brush BudgetRatioColumnBrush = FrozenBrush(0xA5, 0xD6, 0xA7);
	static readonly Brush PrevYearColumnBrush = FrozenBrush(0xE1, 0xF5, 0xFE);
	static readonly Brush DbNullCellBrush = FrozenBrush(0xC8, 0xC8, 0xC8);
	static readonly Brush CellForegroundBrush = FrozenBrush(0x21, 0x21, 0x21);
	static readonly Brush SundayBrush = FrozenBrush(0xD3, 0x2F, 0x2F);
	static readonly Brush SaturdayBrush = FrozenBrush(0x19, 0x76, 0xD2);

	readonly DataRowColumnIsDbNullConverter _dbNullConverter = new();
	readonly ShopBudgetTotalCellConverter _totalCellConverter = new();

	ScrollViewer? _detailScrollViewer;
	ScrollViewer? _totalScrollViewer;
	bool _detailScrollHandlerAttached;

	public DailyShopBudgetQueryView() {
		InitializeComponent();
		PreviewKeyDown += DailyShopBudgetQueryView_PreviewKeyDown;
	}

	static SolidColorBrush FrozenBrush(byte r, byte g, byte b) {
		var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	/// <summary>
	/// Esc は結果パネル表示中は BackToConditionsCommand、条件パネル表示中は ExitCommand に振り分ける。
	/// F5/F6/F7 (今月/前月/翌月) は結果パネル表示中のみ有効。
	/// いずれも InputBindings に置くと条件/結果パネルで衝突するため、ここで一元的に判定する。
	/// </summary>
	void DailyShopBudgetQueryView_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (DataContext is not DailyShopBudgetQueryViewModel vm) return;

		switch (e.Key) {
			case Key.Escape:
				e.Handled = true;
				if (vm.IsResultVisible) {
					if (vm.BackToConditionsCommand.CanExecute(null)) vm.BackToConditionsCommand.Execute(null);
				}
				else {
					if (vm.ExitCommand.CanExecute(null)) vm.ExitCommand.Execute(null);
				}
				break;

			case Key.F6 when vm.IsResultVisible:
				e.Handled = true;
				if (vm.PrevMonthCommand.CanExecute(null)) vm.PrevMonthCommand.Execute(null);
				break;

			case Key.F5 when vm.IsResultVisible:
				e.Handled = true;
				if (vm.ThisMonthCommand.CanExecute(null)) vm.ThisMonthCommand.Execute(null);
				break;

			case Key.F7 when vm.IsResultVisible:
				e.Handled = true;
				if (vm.NextMonthCommand.CanExecute(null)) vm.NextMonthCommand.Execute(null);
				break;
		}
	}

	void DetailGrid_Loaded(object sender, RoutedEventArgs e) => TryWireScrollSync();

	void TotalGrid_Loaded(object sender, RoutedEventArgs e) => TryWireScrollSync();

	/// <summary>
	/// 結果パネル(ResultPanel)が Collapsed→Visible になった直後に発火する。Visible になった時点では
	/// まだ当該フレームの measure/テンプレート展開が済んでいない場合があるため、
	/// DispatcherPriority.Loaded で1フレーム遅延させてから配線を試みる。
	/// </summary>
	void ResultPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
		if (e.NewValue is not true) return;
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, TryWireScrollSync);
	}

	/// <summary>
	/// 明細グリッドの ScrollChanged (ルーテッドイベント、DataGrid自身に AddHandler で登録)を配線し、
	/// 現在の明細横オフセットを合計グリッドへ初期同期する。
	/// ScrollViewer がまだ展開されていない場合は何もしないが、null をキャッシュしないため
	/// (DetailScrollViewer/TotalScrollViewer プロパティ側で毎回再取得を試みるため)次回呼び出しで取れる。
	/// </summary>
	void TryWireScrollSync() {
		if (!_detailScrollHandlerAttached) {
			DetailGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DetailGrid_ScrollChanged));
			_detailScrollHandlerAttached = true;
		}

		var detailScrollViewer = DetailScrollViewer;
		var totalScrollViewer = TotalScrollViewer;
		if (detailScrollViewer != null && totalScrollViewer != null) {
			// 初回同期: 検索直後など HorizontalChange==0 のケースでも合計グリッド側を明細グリッドに揃える。
			totalScrollViewer.ScrollToHorizontalOffset(detailScrollViewer.HorizontalOffset);
		}
	}

	/// <summary>明細グリッドの横スクロールを合計グリッドへ一方向に反映する（合計→明細の逆方向は無し）。</summary>
	void DetailGrid_ScrollChanged(object sender, ScrollChangedEventArgs e) {
		if (e.HorizontalChange == 0) return;
		TotalScrollViewer?.ScrollToHorizontalOffset(e.HorizontalOffset);
	}

	/// <summary>DetailGrid内のScrollViewer。null の間は毎回再取得を試みる(Collapsed直後は取得できないため)。</summary>
	ScrollViewer? DetailScrollViewer {
		get {
			_detailScrollViewer ??= FindScrollViewer(DetailGrid);
			return _detailScrollViewer;
		}
	}

	/// <summary>TotalGrid内のScrollViewer。null の間は毎回再取得を試みる(Collapsed直後は取得できないため)。</summary>
	ScrollViewer? TotalScrollViewer {
		get {
			_totalScrollViewer ??= FindScrollViewer(TotalGrid);
			return _totalScrollViewer;
		}
	}

	static ScrollViewer? FindScrollViewer(DependencyObject parent) {
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is ScrollViewer scrollViewer) return scrollViewer;

			var found = FindScrollViewer(child);
			if (found != null) return found;
		}
		return null;
	}

	/// <summary>明細グリッド(ResultTable)の自動生成列に書式・配色を割り当てる。</summary>
	void DetailGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) {
		if (e.Column is not DataGridTextColumn column) return;
		var grid = (DataGrid)sender;
		string name = e.PropertyName;

		column.Width = new DataGridLength(ColumnWidths.GetValueOrDefault(name, 112));

		switch (name) {
			case "日付":
				column.ElementStyle = BuildTextStyle(TextAlignment.Left);
				break;

			case "曜日":
				column.ElementStyle = BuildWeekdayStyle();
				break;

			default:
				bool isPercent = PercentColumns.Contains(name);
				if (column.Binding is Binding binding) {
					binding.StringFormat = isPercent ? "{0:0.0}" : "{0:#,##0}";
				}
				column.ElementStyle = BuildTextStyle(TextAlignment.Right);
				var background = ColumnBackground(name);
				if (background != null) {
					column.CellStyle = BuildCellStyle(grid, background, grayColumnName: null);
				}
				break;
		}
	}

	/// <summary>
	/// 合計グリッド(TotalTable)の自動生成列に書式・配色を割り当てる。
	/// 店舗列は行(日付列のラベル)によって金額/比率が混在するため、DataRowView 全体を受け取る
	/// ShopBudgetTotalCellConverter で書式を切り替える。DBNull セルは DataRowColumnIsDbNullConverter で
	/// グレー背景にする。
	/// </summary>
	void TotalGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) {
		if (e.Column is not DataGridTextColumn column) return;
		var grid = (DataGrid)sender;
		string name = e.PropertyName;

		column.Width = new DataGridLength(ColumnWidths.GetValueOrDefault(name, 112));

		if (name == "日付") {
			// 行ラベル("売上計"等)。DBNullにはならない。
			column.ElementStyle = BuildTextStyle(TextAlignment.Left, bold: true);
			return;
		}

		// 曜日・売上計・予算計・予算比・前年売上計・前年売上比・店舗列は、いずれも
		// 「自身の行以外はDBNull」または「常にDBNull(曜日)」または「店舗列(常に値あり、行で書式のみ変わる)」。
		column.Binding = new Binding(".") { Converter = _totalCellConverter, ConverterParameter = name };
		column.ElementStyle = BuildTextStyle(TextAlignment.Right);
		var background = ColumnBackground(name);
		column.CellStyle = BuildCellStyle(grid, background, grayColumnName: name);
	}

	static Brush? ColumnBackground(string columnName) => columnName switch {
		"売上計" => SalesColumnBrush,
		"予算計" => BudgetColumnBrush,
		"予算比" => BudgetRatioColumnBrush,
		"前年売上計" or "前年売上比" => PrevYearColumnBrush,
		_ => null,
	};

	static Style BuildTextStyle(TextAlignment alignment, bool bold = false) {
		var style = new Style(typeof(TextBlock));
		style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, alignment));
		style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
		style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
		style.Setters.Add(new Setter(TextBlock.ForegroundProperty, CellForegroundBrush));
		if (bold) style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
		return style;
	}

	static Style BuildWeekdayStyle() {
		var style = BuildTextStyle(TextAlignment.Center);
		var sundayTrigger = new DataTrigger {
			Binding = new Binding(nameof(TextBlock.Text)) { RelativeSource = new RelativeSource(RelativeSourceMode.Self) },
			Value = "日",
		};
		sundayTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, SundayBrush));
		style.Triggers.Add(sundayTrigger);

		var saturdayTrigger = new DataTrigger {
			Binding = new Binding(nameof(TextBlock.Text)) { RelativeSource = new RelativeSource(RelativeSourceMode.Self) },
			Value = "土",
		};
		saturdayTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, SaturdayBrush));
		style.Triggers.Add(saturdayTrigger);
		return style;
	}

	/// <summary>
	/// グリッド既定の CellStyle を土台に、列背景色と(合計グリッドのみ)DBNullグレー化を追加する。
	/// </summary>
	Style BuildCellStyle(DataGrid grid, Brush? background, string? grayColumnName) {
		var style = new Style(typeof(DataGridCell), grid.CellStyle);
		if (background != null) {
			style.Setters.Add(new Setter(Control.BackgroundProperty, background));
		}
		if (grayColumnName != null) {
			var trigger = new DataTrigger {
				Binding = new Binding(".") { Converter = _dbNullConverter, ConverterParameter = grayColumnName },
				Value = true,
			};
			trigger.Setters.Add(new Setter(Control.BackgroundProperty, DbNullCellBrush));
			style.Triggers.Add(trigger);
		}
		return style;
	}
}
