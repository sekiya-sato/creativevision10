using CvWpfclient.ViewModels._01Master;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CvWpfclient.Views._01Master;

public partial class MasterJouDaiBulkChangeView : Helpers.BaseWindow {
	/// <summary>
	/// Price Matrix（③価格タブ、PriceMatrixGrid）の固定列数（商品CD/商品名/通常上代/原価）。
	/// これより後ろがScope（設計3.4）ぶんの動的列。
	/// </summary>
	const int FixedColumnCount = 4;
	const double ScopeColumnWidth = 110;

	// 原価割れ(C7)・最低販売価格違反(C8、設計2.8)のセル背景色。両方に該当する場合は
	// Style.Triggersの後勝ちにより最低販売価格違反の色を優先する（より重い警告として後段に置く）。
	static readonly Brush CostViolationBrush = FrozenBrush(0xFF, 0xCD, 0xD2);
	static readonly Brush MinPriceViolationBrush = FrozenBrush(0xFF, 0xB7, 0x4D);

	readonly MasterJouDaiBulkChangeViewModel? viewModel;
	ObservableCollection<JodaiScopeRow>? subscribedScopeRows;

	public MasterJouDaiBulkChangeView() {
		InitializeComponent();
		viewModel = DataContext as MasterJouDaiBulkChangeViewModel;
		if (viewModel != null) {
			viewModel.PropertyChanged += OnViewModelPropertyChanged;
			RewireScopeRowsCollection();
		}
		Closed += (_, _) => {
			if (viewModel != null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
			if (subscribedScopeRows != null) subscribedScopeRows.CollectionChanged -= ScopeRows_CollectionChanged;
		};
	}

	/// <summary>
	/// Scope列の見出しテンプレート。<see cref="DataGridColumn.Header"/> に積んだ
	/// <see cref="JodaiScopeRow"/> の <see cref="JodaiScopeRow.Name"/> を表示する。
	/// Scopeのリネームには列を作り直さずバインドが追従する。
	/// </summary>
	static readonly DataTemplate ScopeHeaderTemplate = BuildScopeHeaderTemplate();

	static DataTemplate BuildScopeHeaderTemplate() {
		var text = new FrameworkElementFactory(typeof(TextBlock));
		text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
		text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
		text.SetBinding(TextBlock.TextProperty, new Binding(nameof(JodaiScopeRow.Name)));
		var template = new DataTemplate { VisualTree = text };
		template.Seal();
		return template;
	}

	static SolidColorBrush FrozenBrush(byte r, byte g, byte b) {
		var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	/// <summary>PriceMatrixGridのスタイル適用（CellStyleの既定値解決）が済んでから初回の列生成を行う。</summary>
	void PriceMatrixGrid_Loaded(object sender, RoutedEventArgs e) => RebuildScopeColumns();

	void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName != nameof(MasterJouDaiBulkChangeViewModel.ScopeRows)) return;
		// ScopeRowsが丸ごと差し替えられた（ResetScopeRows・LoadEditAsync）。購読先を付け替えて列を作り直す
		RewireScopeRowsCollection();
		RebuildScopeColumns();
	}

	void RewireScopeRowsCollection() {
		if (viewModel == null) return;
		if (subscribedScopeRows != null) subscribedScopeRows.CollectionChanged -= ScopeRows_CollectionChanged;
		subscribedScopeRows = viewModel.ScopeRows;
		subscribedScopeRows.CollectionChanged += ScopeRows_CollectionChanged;
	}

	/// <summary>ScopeRowsへの直接Add/Remove（AddScopeRow/RemoveScopeRow等）を拾う。</summary>
	void ScopeRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildScopeColumns();

	/// <summary>
	/// Scope（列）の増減に合わせてPriceMatrixGridの動的列を作り直す（設計書5.4「Scope列は実行時に生成する」）。
	/// <para>
	/// 動的列生成は<c>CvWpfclient/Views/03Hatchu/HachuHaibunInputView.xaml.cs</c>の
	/// <c>RebuildSkuColumns</c>（配分クロス表: 行=入庫先/列=色サイズ）と同じ流儀に揃えた。行VM
	/// （<see cref="JodaiMeisaiRow"/>）の<see cref="JodaiMeisaiRow.Cells"/>を列インデックスで
	/// <c>Cells[i]</c>と束縛し、列ヘッダには<see cref="JodaiScopeRow"/>自体を積んで
	/// <c>HeaderTemplate</c>で<c>Name</c>を表示する（Scope名変更に列の作り直し無しで追従する）。
	/// </para>
	/// <para>列の並び順は<see cref="MasterJouDaiBulkChangeViewModel.ScopeRows"/>の並びと一致させる。
	/// <see cref="JodaiMeisaiRow.SyncCells"/>が<c>Cells</c>を同じ順で揃えている前提。</para>
	/// </summary>
	void RebuildScopeColumns() {
		if (!PriceMatrixGrid.IsLoaded) return;

		for (int i = PriceMatrixGrid.Columns.Count - 1; i >= FixedColumnCount; i--) {
			PriceMatrixGrid.Columns.RemoveAt(i);
		}
		if (viewModel == null) return;

		// 見出しテンプレートはコードで組み立てる。XAML のリソースに置いて FindResource で引く形は、
		// 置き場所（DataGrid.Resources / Window.Resources）を問わず解決できず
		// ResourceReferenceKeyNotFoundException で画面のロードごと落ちた。列生成自体がコード側の
		// 処理なので、テンプレートもコード側に閉じておくほうが依存が少なく壊れにくい。
		var headerTemplate = ScopeHeaderTemplate;
		// 右寄せスタイルはアプリ共通リソース（App.xaml）。万一未登録でも列生成は続けたいので Try で引く。
		var elementStyle = PriceMatrixGrid.TryFindResource("DataGridRightTextBlock") as Style;
		var editingElementStyle = PriceMatrixGrid.TryFindResource("DataGridRightTextBox") as Style;

		for (int i = 0; i < viewModel.ScopeRows.Count; i++) {
			PriceMatrixGrid.Columns.Add(new DataGridTextColumn {
				// HeaderにJodaiScopeRow自体を積む（HachuHaibunInputViewがHachuHaibunSkuSummaryを積むのと同じ）。
				// Name変更（Scope名のリネーム）は列の作り直し無しでHeaderTemplateのバインドが追従する
				Header = viewModel.ScopeRows[i],
				HeaderTemplate = headerTemplate,
				Width = ScopeColumnWidth,
				Binding = new Binding($"Cells[{i}].JodaiNew") {
					Mode = BindingMode.TwoWay,
					UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
					StringFormat = "{0:N0}",
				},
				ElementStyle = elementStyle,
				EditingElementStyle = editingElementStyle,
				CellStyle = BuildMatrixCellStyle(i),
			});
		}
	}

	/// <summary>
	/// セルの原価割れ・最低販売価格違反（設計2.8のC7/C8）を背景色で警告する。
	/// <c>DailyShopBudgetQueryView.xaml.cs</c>の<c>BuildCellStyle</c>と同じ「グリッド既定のCellStyleを
	/// 土台に、DataTriggerで背景だけ足す」流儀。判定結果そのもの（<see cref="JodaiPriceCell.IsCostViolation"/>/
	/// <see cref="JodaiPriceCell.IsMinPriceViolation"/>）はViewModel側で観測できるプロパティなので、
	/// UatVmではこちらを検証し、色そのものはこの画面での目視確認に委ねる。
	/// </summary>
	Style BuildMatrixCellStyle(int scopeIndex) {
		var style = new Style(typeof(DataGridCell), PriceMatrixGrid.CellStyle);

		var costTrigger = new DataTrigger { Binding = new Binding($"Cells[{scopeIndex}].IsCostViolation"), Value = true };
		costTrigger.Setters.Add(new Setter(Control.BackgroundProperty, CostViolationBrush));
		style.Triggers.Add(costTrigger);

		var minPriceTrigger = new DataTrigger { Binding = new Binding($"Cells[{scopeIndex}].IsMinPriceViolation"), Value = true };
		minPriceTrigger.Setters.Add(new Setter(Control.BackgroundProperty, MinPriceViolationBrush));
		style.Triggers.Add(minPriceTrigger);

		return style;
	}

	/// <summary>
	/// Price Matrixで選択中のセル（商品行×Scope列）をViewModelへ伝える。<c>DataGrid.SelectedCells</c>は
	/// バインドできないため、コードビハインドで読み取って<see cref="MasterJouDaiBulkChangeViewModel.SetSelectedMatrixCells"/>
	/// へ渡すだけに留める（業務ロジック本体はViewModel側）。列のHeaderには<see cref="JodaiScopeRow"/>自体を
	/// 積んでいるので、固定列（Headerが文字列）は<see langword="as"/>で自然に除外される。
	/// </summary>
	void PriceMatrixGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e) {
		if (viewModel == null) return;
		var cells = PriceMatrixGrid.SelectedCells
			.Select(c => (Row: c.Item as JodaiMeisaiRow, Scope: c.Column?.Header as JodaiScopeRow))
			.Where(c => c.Row != null && c.Scope != null)
			.Select(c => (c.Row!, c.Scope!.No))
			.ToList();
		viewModel.SetSelectedMatrixCells(cells);
	}
}
