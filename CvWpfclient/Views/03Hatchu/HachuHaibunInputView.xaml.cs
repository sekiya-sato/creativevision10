using CvWpfclient.ViewModels._03Hatchu;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CvWpfclient.Views._03Hatchu;

public partial class HachuHaibunInputView : Helpers.BaseWindow {
	/// <summary>クロス表の固定列数（行 / 入庫先CD / 入庫先名 / 合計）。これより後ろが SKU の動的列。</summary>
	const int FixedColumnCount = 4;
	// 列ヘッダに 色CD/サイズCD と 発注数・残・計 を積むため、数値が切れない幅を確保する
	// (ヘッダ内側は列幅からヘッダのパディング分を引いた値になる)
	const double SkuColumnWidth = 112;

	HachuHaibunInputViewModel? viewModel;

	public HachuHaibunInputView() {
		InitializeComponent();
		// DataContext は XAML で宣言済みなので、InitializeComponent 直後に購読すればよい。
		viewModel = DataContext as HachuHaibunInputViewModel;
		if (viewModel != null) {
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			RebuildSkuColumns();
		}
		Closed += (_, _) => {
			if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		};
	}

	void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(HachuHaibunInputViewModel.SkuColumns)) RebuildSkuColumns();
	}

	/// <summary>
	/// 選択中商品の SKU（色×サイズ）に合わせてクロス表の列を作り直す。
	/// <para>
	/// 商品ごとに色サイズの構成が違うため列は静的に書けない。列ヘッダには SKU サマリを
	/// そのまま載せ、<c>SkuColumnHeaderTemplate</c> で発注数・残・計を表示する
	/// （サマリは ObservableObject なので入力に追従する）。
	/// </para>
	/// </summary>
	void RebuildSkuColumns() {
		for (int i = HaibunGrid.Columns.Count - 1; i >= FixedColumnCount; i--) {
			HaibunGrid.Columns.RemoveAt(i);
		}
		if (viewModel == null) return;

		var headerTemplate = (DataTemplate)FindResource("SkuColumnHeaderTemplate");
		var elementStyle = (Style)FindResource("DataGridRightTextBlock");
		var editingElementStyle = (Style)FindResource("DataGridRightTextBox");

		for (int i = 0; i < viewModel.SkuColumns.Count; i++) {
			HaibunGrid.Columns.Add(new DataGridTextColumn {
				Header = viewModel.SkuColumns[i],
				HeaderTemplate = headerTemplate,
				Width = SkuColumnWidth,
				// 行VM(HachuHaibunTenpoRow)の Cells は選択商品ぶんのセル列。
				// 0 を空白で見せたいので int の Su ではなく SuText を経由する。
				Binding = new Binding($"Cells[{i}].SuText") { Mode = BindingMode.TwoWay },
				ElementStyle = elementStyle,
				EditingElementStyle = editingElementStyle,
			});
		}
	}

	/// <summary>選択セルの色・サイズを画面下部へ表示する（旧システムの下部表示に対応）。</summary>
	void HaibunGrid_CurrentCellChanged(object sender, EventArgs e) {
		if (viewModel == null) return;
		viewModel.SelectedCellInfo = HaibunGrid.CurrentCell.Column?.Header is HachuHaibunSkuSummary summary
			? $"色CD {summary.ColDisplay}　　サイズCD {summary.SizDisplay}　　JAN {summary.JanCode}"
			: string.Empty;
	}
}
