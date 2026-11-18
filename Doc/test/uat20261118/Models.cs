using CommunityToolkit.Mvvm.ComponentModel;

namespace UatAvalonia;

public partial class ProductRow : ObservableObject {
	[ObservableProperty] public partial string Code { get; set; } = "";
	[ObservableProperty] public partial string Name { get; set; } = "";
	[ObservableProperty] public partial string Kana { get; set; } = "";
	[ObservableProperty] public partial string Category { get; set; } = "定番";
	[ObservableProperty] public partial string Supplier { get; set; } = "1001 ダミー商事";
	[ObservableProperty] public partial decimal Price { get; set; }
	[ObservableProperty] public partial bool Enabled { get; set; } = true;
	[ObservableProperty] public partial DateTimeOffset? StartDate { get; set; } = new(2026, 11, 18, 0, 0, 0, TimeSpan.FromHours(9));
	[ObservableProperty] public partial string Memo { get; set; } = "架空の検証データ";
	public ProductRow Copy() => new() {
		Code = Code, Name = Name, Kana = Kana, Category = Category, Supplier = Supplier,
		Price = Price, Enabled = Enabled, StartDate = StartDate, Memo = Memo
	};
}

public partial class DetailRow : ObservableObject {
	[ObservableProperty] public partial string Color { get; set; } = "黒";
	[ObservableProperty] public partial string Size { get; set; } = "M";
	[ObservableProperty] public partial decimal Quantity { get; set; } = 1;
}

// 実サーバ・DB・ファイルへは接続せず、アプリを閉じると初期状態に戻す。
public sealed class DummyProductService {
	private readonly List<ProductRow> rows = Enumerable.Range(1, 40).Select(i => new ProductRow {
		Code = $"{i:000000}", Name = $"検証商品 {i:00}", Kana = $"ケンショウショウヒン{i:00}",
		Category = i % 2 == 0 ? "季節" : "定番", Price = 1000 + i * 100, Enabled = i % 5 != 0
	}).ToList();

	public async Task<List<ProductRow>> SearchAsync(string filter, string category, CancellationToken ct) {
		await Task.Delay(900, ct);
		return rows.Where(x => (category == "すべて" || x.Category == category) &&
			(string.IsNullOrWhiteSpace(filter) || x.Code.Contains(filter) || x.Name.Contains(filter) || x.Kana.Contains(filter)))
			.Select(x => x.Copy()).ToList();
	}

	public async Task SaveAsync(ProductRow row, bool insert, CancellationToken ct) {
		await Task.Delay(500, ct);
		var index = rows.FindIndex(x => x.Code == row.Code);
		if (insert && index >= 0) throw new InvalidOperationException("同じコードが存在します。");
		if (!insert && index < 0) throw new InvalidOperationException("修正対象がありません。");
		if (insert) rows.Add(row.Copy()); else rows[index] = row.Copy();
	}

	public async Task DeleteAsync(string code, CancellationToken ct) {
		await Task.Delay(500, ct);
		rows.RemoveAll(x => x.Code == code);
	}
}
