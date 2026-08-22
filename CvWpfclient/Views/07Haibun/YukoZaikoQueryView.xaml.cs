using System.Windows;
using System.Windows.Controls;

namespace CvWpfclient.Views._07Haibun;

public partial class YukoZaikoQueryView : Helpers.BaseWindow {
	public YukoZaikoQueryView() {
		InitializeComponent();
	}

	/// <summary>
	/// 数量マトリクスの自動生成列に表示スタイルを割り当てる。
	/// 倉庫列は左詰め、倉庫毎Total列は太字強調、数値列は右詰め＋符号色分けにする。
	/// </summary>
	void StockGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) {
		if (e.Column is not DataGridTextColumn column) return;

		switch (e.PropertyName) {
			case "倉庫":
				column.ElementStyle = FindResource("StockQuerySokoCell") as Style;
				break;
			case "倉庫毎Total":
				ApplyNumericStyle(column, "StockQueryTotalCell");
				break;
			default:
				if (e.PropertyType == typeof(int)) {
					ApplyNumericStyle(column, "StockQueryNumericCell");
				}
				break;
		}
	}

	void ApplyNumericStyle(DataGridTextColumn column, string styleKey) {
		if (column.Binding is System.Windows.Data.Binding binding) {
			binding.StringFormat = "{0:N0}";
		}
		column.ElementStyle = FindResource(styleKey) as Style;
	}
}
