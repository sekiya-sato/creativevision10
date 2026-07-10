using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels.Sub;

public partial class InputBarcodeViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial string BarcodeText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<InputBarcodeRow> ListData { get; set; } = [];

	[ObservableProperty]
	public partial InputBarcodeRow? Current { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = "バーコードを読み取ってください";

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial int TotalSu { get; set; }

	readonly Dictionary<long, MasterShohin> shohinCache = [];

	[RelayCommand(IncludeCancelCommand = true)]
	async Task AddBarcode(CancellationToken ct) {
		var barcode = BarcodeText.Trim();
		if (barcode.Length == 0) return;

		try {
			var sku = await LoadShohinColSizAsync(barcode, ct);
			if (sku == null) {
				Message = $"バーコードが見つかりません: {barcode}";
				MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
				BarcodeText = string.Empty;
				return;
			}

			var shohin = await LoadShohinAsync(sku.Id_Shohin, ct);
			if (shohin == null) {
				Message = $"商品が見つかりません: 商品Id={sku.Id_Shohin}";
				MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
				BarcodeText = string.Empty;
				return;
			}

			AddOrIncrementRow(barcode, sku, shohin);
			BarcodeText = string.Empty;
		}
		catch (OperationCanceledException) {
			throw;
		}
		catch (Exception ex) {
			Message = $"バーコード読取エラー: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
	}

	void AddOrIncrementRow(string barcode, DerivedShohinColSiz sku, MasterShohin shohin) {
		var existing = ListData.FirstOrDefault(x => string.Equals(x.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
		if (existing != null) {
			existing.Su++;
			Current = existing;
			UpdateTotals();
			Message = $"数量を加算しました: {barcode}";
			return;
		}

		var row = new InputBarcodeRow {
			Barcode = barcode,
			Su = 1,
			Tanka = shohin.TankaJodai,
			Jodai = shohin.TankaJodai,
			Gedai = shohin.TankaGenka,
			IdShohin = shohin.Id,
			CodeShohin = shohin.Code ?? string.Empty,
			MeiShohin = shohin.Name ?? string.Empty,
			IdCol = sku.Id_Col,
			CodeCol = sku.Code_Col,
			MeiCol = sku.Mei_Col,
			IdSiz = sku.Id_Siz,
			CodeSiz = sku.Code_Siz,
			MeiSiz = sku.Mei_Siz,
		};
		ListData.Add(row);
		Current = row;
		UpdateTotals();
		Message = $"追加しました: {barcode}";
	}

	async Task<DerivedShohinColSiz?> LoadShohinColSizAsync(string barcode, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(new QueryListParam(
				itemType: typeof(DerivedShohinColSiz),
				where: "(Jan1 = @0 OR Jan2 = @0 OR Jan3 = @0)",
				order: "Code, Code_Col, Code_Siz, RowIdx",
				parameters: [barcode],
				maxCount: 1))
		};

		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();

		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "バーコード検索でエラーが発生しました");
		}
		if (reply.Code == -1) return null;
		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) return null;
		return list.Cast<DerivedShohinColSiz>().FirstOrDefault();
	}

	async Task<MasterShohin?> LoadShohinAsync(long idShohin, CancellationToken ct) {
		if (shohinCache.TryGetValue(idShohin, out var cached)) return cached;

		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryByIdParam),
			DataMsg = Common.SerializeObject(new QueryByIdParam(typeof(MasterShohin), idShohin))
		};

		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();

		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "商品検索でエラーが発生しました");
		}
		if (reply.Code == -1) return null;
		if (Common.DeserializeObject(reply.DataMsg ?? "{}", reply.DataType) is not MasterShohin shohin) return null;

		shohinCache[idShohin] = shohin;
		return shohin;
	}

	void UpdateTotals() {
		Count = ListData.Count;
		TotalSu = ListData.Sum(x => x.Su);
	}

	[RelayCommand]
	void DoOk() {
		if (ListData.Count == 0) {
			MessageEx.ShowWarningDialog("バーコードが入力されていません", owner: ClientLib.GetActiveView(this));
			return;
		}

		ClientLib.ExitDialogResult(this, true);
	}

	public List<Tran99Meisai> CreateMeisaiRows(int kubun) =>
		[.. ListData.Select(x => x.ToMeisai(kubun))];

	protected override void OnExit() {
		ClientLib.ExitDialogResult(this, false);
	}
}

public partial class InputBarcodeRow : ObservableObject {
	[ObservableProperty]
	public partial string Barcode { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int Su { get; set; }

	[ObservableProperty]
	public partial int Tanka { get; set; }

	[ObservableProperty]
	public partial long IdShohin { get; set; }

	[ObservableProperty]
	public partial string CodeShohin { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MeiShohin { get; set; } = string.Empty;

	[ObservableProperty]
	public partial long IdCol { get; set; }

	[ObservableProperty]
	public partial string CodeCol { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MeiCol { get; set; } = string.Empty;

	[ObservableProperty]
	public partial long IdSiz { get; set; }

	[ObservableProperty]
	public partial string CodeSiz { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string MeiSiz { get; set; } = string.Empty;

	public int Jodai { get; set; }
	public int Gedai { get; set; }

	public Tran99Meisai ToMeisai(int kubun) => new() {
		Kubun = kubun,
		Id_Shohin = IdShohin,
		Code_Shohin = CodeShohin,
		Mei_Shohin = MeiShohin,
		JanCode = Barcode,
		Id_Col = IdCol,
		Code_Col = CodeCol,
		Mei_Col = MeiCol,
		Id_Siz = IdSiz,
		Code_Siz = CodeSiz,
		Mei_Siz = MeiSiz,
		Su = Su,
		Tanka = Tanka,
		Kingaku = Su * Tanka,
		Jodai = Jodai,
		Gedai = Gedai,
	};
}
