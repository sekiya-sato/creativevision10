using CvWpfclient.ViewModels._07Haibun;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CvWpfclient.Views._07Haibun;

public partial class InventoryAllocationInputView : Helpers.BaseWindow {
	/// <summary>マトリクスの固定列数（配分先 / 種別 / 比率 / 合計）。これより後ろが SKU の動的列。</summary>
	const int FixedColumnCount = 4;
	/// <summary>SKU 列幅。見出しに 在庫・配分・後 を積むので数値が切れない幅にする</summary>
	const double SkuColumnWidth = 96;

	readonly InventoryAllocationInputViewModel? viewModel;

	public InventoryAllocationInputView() {
		InitializeComponent();
		viewModel = DataContext as InventoryAllocationInputViewModel;
		if (viewModel != null) {
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			RebuildSkuColumns();
		}
		Closed += (_, _) => {
			if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		};
	}

	void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(InventoryAllocationInputViewModel.SkuColumns)) RebuildSkuColumns();
	}

	/// <summary>商品の SKU（色×サイズ）に合わせてマトリクスの列を作り直す（発注配分入力・受注配分(商品別)と同じ作り）。</summary>
	void RebuildSkuColumns() {
		for (int i = AllocationGrid.Columns.Count - 1; i >= FixedColumnCount; i--) {
			AllocationGrid.Columns.RemoveAt(i);
		}
		if (viewModel == null) return;
		var headerTemplate = (DataTemplate)FindResource("SkuColumnHeaderTemplate");
		var elementStyle = (Style)FindResource("DataGridRightTextBlock");
		var editingStyle = (Style)FindResource("DataGridRightTextBox");
		for (int i = 0; i < viewModel.SkuColumns.Count; i++) {
			AllocationGrid.Columns.Add(new DataGridTextColumn {
				Header = viewModel.SkuColumns[i],
				HeaderTemplate = headerTemplate,
				Width = SkuColumnWidth,
				// 0 を空白で見せたいので int の Su ではなく SuText を経由する
				Binding = new Binding($"Cells[{i}].SuText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
				ElementStyle = elementStyle,
				EditingElementStyle = editingStyle,
			});
		}
	}

	/// <summary>選択中の SKU 列を ViewModel へ伝える（「選択中の列だけ」按分に使う）。</summary>
	void AllocationGrid_CurrentCellChanged(object? sender, EventArgs e) {
		if (viewModel == null) return;
		var index = AllocationGrid.CurrentCell.Column == null ? -1 : AllocationGrid.Columns.IndexOf(AllocationGrid.CurrentCell.Column);
		viewModel.SelectedSkuIndex = index >= FixedColumnCount ? index - FixedColumnCount : -1;
	}
}
