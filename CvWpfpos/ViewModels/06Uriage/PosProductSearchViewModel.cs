using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Services;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

public partial class PosProductSearchViewModel : ObservableObject {
	private readonly PosGrpcClient client;

	[ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
	[ObservableProperty] public partial ObservableCollection<MasterShohin> Products { get; set; } = [];
	[ObservableProperty] public partial MasterShohin? SelectedProductMaster { get; set; }
	[ObservableProperty] public partial ObservableCollection<DerivedShohinColSiz> Skus { get; set; } = [];
	[ObservableProperty] public partial DerivedShohinColSiz? SelectedSku { get; set; }
	[ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
	[ObservableProperty] public partial bool IsBusy { get; set; }

	/// <summary>ダイアログの戻り値。JAN 検索または SKU 選択で設定される。</summary>
	public PosProduct? SelectedProduct { get; private set; }

	/// <summary>選択された商品のバーコード（JAN）。</summary>
	public string SelectedBarcode { get; private set; } = string.Empty;

	/// <summary>View 側が購読して DialogResult = true を設定する。</summary>
	public event EventHandler? RequestClose;

	public PosProductSearchViewModel() : this(AppGlobal.Client) { }

	public PosProductSearchViewModel(PosGrpcClient client) {
		this.client = client;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task Search(CancellationToken cancellationToken) {
		var text = SearchText.Trim();
		if (string.IsNullOrWhiteSpace(text)) { StatusMessage = "検索文字を入力してください。"; return; }

		IsBusy = true;
		Products.Clear();
		Skus.Clear();
		SelectedProductMaster = null;
		SelectedSku = null;
		SelectedProduct = null;
		SelectedBarcode = string.Empty;
		try {
			if (text.Length >= 8 && long.TryParse(text, out _)) {
				var product = await client.LookupProductAsync(text, cancellationToken);
				if (product == null) { StatusMessage = $"JANコードが見つかりません: {text}"; return; }
				SelectedProduct = product;
				SelectedBarcode = text;
				RequestClose?.Invoke(this, EventArgs.Empty);
				return;
			}

			var list = await client.QueryListAsync<MasterShohin>("Code like @0 or Name like @0", "Code", [$"%{text}%"], 100, cancellationToken);
			foreach (var item in list) Products.Add(item);
			StatusMessage = $"{list.Count} 件見つかりました。";
		}
		catch (OperationCanceledException) { StatusMessage = "検索を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"検索エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}

	partial void OnSelectedProductMasterChanged(MasterShohin? value) {
		Skus.Clear();
		SelectedSku = null;
		if (value == null) return;
		_ = LoadSkusAsync(value.Id);
	}

	private async Task LoadSkusAsync(long productId) {
		try {
			var list = await client.QueryListAsync<DerivedShohinColSiz>("Id_Shohin=@0", null, [productId.ToString()], 100, CancellationToken.None);
			foreach (var item in list) Skus.Add(item);
		}
		catch (Exception ex) { StatusMessage = $"SKU 読み込みエラー: {ex.Message}"; }
	}

	/// <summary>選択SKUをサーバで引き直し、会計と同じ店舗上代・税区分で返す。</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SelectSku(CancellationToken cancellationToken) {
		if (SelectedSku == null || SelectedProductMaster == null) return;
		IsBusy = true;
		try {
			var product = await client.LookupSkuAsync(SelectedSku.Id, cancellationToken);
			if (product == null) { StatusMessage = "SKU が見つかりません。"; return; }
			SelectedProduct = product;
			SelectedBarcode = product.Barcode;
			RequestClose?.Invoke(this, EventArgs.Empty);
		}
		catch (OperationCanceledException) { StatusMessage = "SKU の選択を中止しました。"; }
		catch (Exception ex) { StatusMessage = $"SKU 選択エラー: {ex.Message}"; }
		finally { IsBusy = false; }
	}
}
