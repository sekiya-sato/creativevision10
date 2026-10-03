using CvWpfclient.ViewModels._07Haibun;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CvWpfclient.Views._07Haibun;

public partial class SalesOrderAllocationInputView : Helpers.BaseWindow {
	/// <summary>マトリクスの固定列数（得意先 / 受注数 / 受注残 / 配分計）。これより後ろが SKU の動的列。</summary>
	const int FixedColumnCount = 4;
	/// <summary>SKU 列幅。見出しに 在庫・受注残・配分・後 の4行を積むので数値が切れない幅にする</summary>
	const double SkuColumnWidth = 104;

	readonly SalesOrderAllocationInputViewModel? viewModel;

	public SalesOrderAllocationInputView() {
		InitializeComponent();
		viewModel = DataContext as SalesOrderAllocationInputViewModel;
		if (viewModel != null) {
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			RebuildSkuColumns();
		}
		Closed += (_, _) => {
			if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		};
	}

	void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(SalesOrderAllocationInputViewModel.SkuColumns)) RebuildSkuColumns();
	}

	/// <summary>
	/// 商品の SKU（色×サイズ）に合わせてマトリクスの列を作り直す。
	/// <para>
	/// 列見出しには SKU 列のVMを載せて在庫・受注残・配分・配分後在庫を出す（入力に追従する）。
	/// セルは <c>Cells[i].SuText</c> を編集し、受注残を超えたら橙、受注の無いセルは灰色で入力不可にする。
	/// </para>
	/// </summary>
	void RebuildSkuColumns() {
		for (int i = AllocationGrid.Columns.Count - 1; i >= FixedColumnCount; i--) {
			AllocationGrid.Columns.RemoveAt(i);
		}
		if (viewModel == null) return;

		var headerTemplate = (DataTemplate)FindResource("SkuColumnHeaderTemplate");
		var elementBase = (Style)FindResource("DataGridRightTextBlock");
		var editingStyle = (Style)FindResource("DataGridRightTextBox");

		for (int i = 0; i < viewModel.SkuColumns.Count; i++) {
			var cellStyle = new Style(typeof(DataGridCell), AllocationGrid.CellStyle);
			cellStyle.Setters.Add(new Setter(ToolTipProperty, new Binding($"Cells[{i}].ZanText")));
			var over = new DataTrigger { Binding = new Binding($"Cells[{i}].IsOver"), Value = true };
			over.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xB2))));
			cellStyle.Triggers.Add(over);
			var noOrder = new DataTrigger { Binding = new Binding($"Cells[{i}].HasOrder"), Value = false };
			noOrder.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))));
			noOrder.Setters.Add(new Setter(IsEnabledProperty, false));
			cellStyle.Triggers.Add(noOrder);

			AllocationGrid.Columns.Add(new DataGridTextColumn {
				Header = viewModel.SkuColumns[i],
				HeaderTemplate = headerTemplate,
				Width = SkuColumnWidth,
				// 0 を空白で見せたいので int の Su ではなく SuText を経由する
				Binding = new Binding($"Cells[{i}].SuText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
				ElementStyle = elementBase,
				EditingElementStyle = editingStyle,
				CellStyle = cellStyle,
			});
		}
	}
}
