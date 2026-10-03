using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>移動先倉庫の積送残を、既存一括登録I/Fによる補正移動受で解消する。</summary>
public partial class InTransitClearViewModel : BaseViewModel {
	const int DetailsPerSlip = 100;
	List<SummaryStock> snapshot = [];
	bool hasCurrentList;

	[ObservableProperty]
	public partial ObservableCollection<InTransitWarehouseRow> Rows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial bool OperationStopped { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(LoadStatusCommand), nameof(SelectAllTargetsCommand), nameof(ClearAllTargetsCommand), nameof(ExecuteCommand))]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial bool HasUncertainResult { get; set; }

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "積送残を取得してください。";

	bool CanOperate() => !IsProcessing;
	bool CanExecute() => !IsProcessing && hasCurrentList && OperationStopped && !HasUncertainResult && Rows.Any(x => x.IsTarget);

	[RelayCommand]
	async Task Init() => await LoadStatus(CancellationToken.None);

	[RelayCommand(CanExecute = nameof(CanOperate), IncludeCancelCommand = true)]
	async Task LoadStatus(CancellationToken ct) {
		if (IsProcessing) return;
		IsProcessing = true;
		hasCurrentList = false;
		StatusMessage = "積送残を取得しています...";
		try {
			await RefreshAsync(ct);
			StatusMessage = HasUncertainResult
				? "前回の実行結果をサーバと生成伝票で確認してください。一覧取得では実行禁止を解除しません。"
				: $"{Rows.Count:N0} 倉庫の積送残を取得しました。対象倉庫にチェックを付けてください。";
		}
		catch (OperationCanceledException) {
			StatusMessage = "一覧取得を中止しました。再取得してください。";
		}
		catch (Exception ex) {
			StatusMessage = $"一覧取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
		}
	}

	[RelayCommand(CanExecute = nameof(CanOperate))]
	void SelectAllTargets() {
		foreach (var row in Rows) row.IsTarget = true;
	}

	[RelayCommand(CanExecute = nameof(CanOperate))]
	void ClearAllTargets() {
		foreach (var row in Rows) row.IsTarget = false;
	}

	[RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
	async Task Execute(CancellationToken ct) {
		if (!CanExecute()) return;
		var ids = Rows.Where(x => x.IsTarget).Select(x => x.WarehouseId).ToHashSet();
		var targets = Rows.Where(x => x.IsTarget).ToArray();
		var positive = targets.Sum(x => x.PositiveQty);
		var negative = targets.Sum(x => x.NegativeQty);
		if (MessageEx.ShowQuestionDialog(
			$"{ids.Count:N0} 倉庫・{targets.Sum(x => x.SkuCount):N0} SKUの積送残をクリアしますか？\n"
			+ $"正数 {positive:N0}、負数 {negative:N0} を移動受として計上し、実在庫が同数増減します。\n"
			+ "移動・HHT・他端末のクリア・在庫再集計を停止してください。\n"
			+ "出庫番号別の消込は行いません。クリア済み分を通常の移動受で再登録しないでください。",
			owner: ClientLib.GetActiveView(this)) != System.Windows.MessageBoxResult.Yes) return;

		IsProcessing = true;
		bool writeSent = false;
		bool writeConfirmed = false;
		try {
			StatusMessage = "選択倉庫の積送残を再確認しています...";
			var current = await ReadBalanceAsync(ids, ct);
			if (!SameBalance(snapshot.Where(x => ids.Contains(x.Id_Soko)), current)) {
				hasCurrentList = false;
				StatusMessage = "積送残が変わりました。何も登録していません。一覧を再取得してください。";
				MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			var warehouses = await ReadWarehousesAsync(ids, ct);
			if (warehouses.Count != ids.Count || warehouses.Any(x => x.Id <= 0 || x.IsZaiko != 1)) {
				throw new InvalidOperationException("対象倉庫のマスタが存在しないか、在庫対象外です。");
			}
			var where = WarehouseWhere("b.Id_Soko", ids, out var parameters);
			var invalid = await CoreServiceClient.QuerySqlListAsync<ScalarCountRow>($"""
				select count(*) Cnt from ({BalanceSql(ids, out _)}) b
				left join MasterShohin ms on ms.Id = b.Id_Shohin
				where {where} and (b.Id_Shohin <= 0 or b.Id_Col < 0 or b.Id_Siz < 0 or ms.Id is null or ms.IsZaiko <> 1)
				""", parameters, ct);
			if (invalid.Count != 1 || invalid[0].Cnt != 0) {
				throw new InvalidOperationException("対象SKUの商品マスタが存在しないか、在庫対象外・不正なSKUです。");
			}
			var details = await ReadDetailsAsync(ids, ct);
			if (!SameBalance(current, details.SelectMany(pair => pair.Value.Select(x => new SummaryStock {
				Id_Soko = pair.Key, Id_Shohin = x.Id_Shohin, Id_Col = x.Id_Col, Id_Siz = x.Id_Siz, TransitQty = x.Su,
			})))) {
				throw new InvalidOperationException("明細取得中に積送残が変わりました。一覧を再取得してください。");
			}
			var receipts = BuildReceipts(details, warehouses, DateTime.Now);
			ct.ThrowIfCancellationRequested();
			if (receipts.Count == 0) throw new InvalidOperationException("処理対象がありません。一覧を再取得してください。");
			StatusMessage = $"移動受 {receipts.Count:N0} 伝票を一括登録しています...";
			// 送信後は取消・自動再送をしない。既存I/Fの結果不明による二重受入を防ぐ。
			writeSent = true;
			var reply = await CoreServiceClient.SendExecuteAsync(
				new InsertBulkParam(typeof(Tran11IdoIn), Common.SerializeObject(receipts)), CancellationToken.None);
			if (reply.Code < 0) {
				writeSent = false;
				throw new InvalidOperationException(string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option);
			}
			var saved = Common.DeserializeObject<List<Tran11IdoIn>>(reply.DataMsg ?? "[]");
			if (saved == null || saved.Count != receipts.Count || saved.Any(x => x.Id <= 0)) {
				throw new InvalidOperationException("登録応答が不正です。生成伝票とサーバ処理の完了を確認してください。");
			}
			writeConfirmed = true;
			await RefreshAsync(CancellationToken.None);
			if (snapshot.Any(x => ids.Contains(x.Id_Soko))) {
				throw new InvalidOperationException("登録後も対象倉庫の積送残があります。更新停止状態と在庫対象設定を確認してください。");
			}
			StatusMessage = $"{ids.Count:N0} 倉庫の積送残をクリアしました（移動受 {saved.Count:N0} 伝票）。";
			MessageEx.ShowInformationDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) when (!writeSent) {
			hasCurrentList = false;
			StatusMessage = "登録前に実行を中止しました。一覧を再取得してください。";
		}
		catch (Exception ex) {
			hasCurrentList = false;
			if (writeSent) HasUncertainResult = true;
			StatusMessage = writeSent
				? (writeConfirmed ? "移動受は保存済みですが、結果確認が未完了です。" : "実行結果が不明です。自動再送は行いません。")
					+ "サーバ処理と生成伝票を確認してから画面を開き直してください。" + ex.Message
				: $"何も登録していません。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
		}
	}

	async Task RefreshAsync(CancellationToken ct) {
		var balances = await ReadBalanceAsync(null, ct);
		var warehouses = await ReadWarehousesAsync(balances.Select(x => x.Id_Soko).ToHashSet(), ct);
		var map = warehouses.ToDictionary(x => x.Id);
		var newRows = balances.GroupBy(x => x.Id_Soko).Select(g => {
			map.TryGetValue(g.Key, out var warehouse);
			return new InTransitWarehouseRow {
				WarehouseId = g.Key, WarehouseCode = warehouse?.Code ?? $"(Id:{g.Key})",
				WarehouseName = warehouse?.Name ?? "マスタなし", TransitQty = g.Sum(x => (long)x.TransitQty),
				PositiveQty = g.Where(x => x.TransitQty > 0).Sum(x => (long)x.TransitQty),
				NegativeQty = g.Where(x => x.TransitQty < 0).Sum(x => (long)x.TransitQty), SkuCount = g.Count(),
			};
		}).OrderBy(x => x.WarehouseCode, StringComparer.Ordinal).ToList();
		foreach (var row in Rows) row.PropertyChanged -= TargetChanged;
		foreach (var row in newRows) row.PropertyChanged += TargetChanged;
		Rows = new ObservableCollection<InTransitWarehouseRow>(newRows);
		snapshot = balances;
		hasCurrentList = true;
		ExecuteCommand.NotifyCanExecuteChanged();
	}

	void TargetChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(InTransitWarehouseRow.IsTarget)) ExecuteCommand.NotifyCanExecuteChanged();
	}

	static (long, long, long, long) Key(SummaryStock x) => (x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz);

	static bool SameBalance(IEnumerable<SummaryStock> expected, IEnumerable<SummaryStock> actual) {
		var left = expected.ToDictionary(Key, x => x.TransitQty);
		var right = actual.ToDictionary(Key, x => x.TransitQty);
		return left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var qty) && qty == x.Value);
	}

	static string WarehouseWhere(string column, HashSet<long>? ids, out string[] parameters) {
		parameters = ids?.Order().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray() ?? [];
		return ids == null ? "1=1" : parameters.Length == 0 ? "1=0"
			: $"{column} in ({string.Join(",", Enumerable.Range(0, parameters.Length).Select(i => $"@{i}"))})";
	}

	static string BalanceSql(HashSet<long>? ids, out string[] parameters) => $"""
		select Id_Soko, Id_Shohin, Id_Col, Id_Siz, sum(TransitQty) TransitQty
		from SummaryStock where {WarehouseWhere("Id_Soko", ids, out parameters)}
		group by Id_Soko, Id_Shohin, Id_Col, Id_Siz having sum(TransitQty) <> 0
		""";

	static Task<List<SummaryStock>> ReadBalanceAsync(HashSet<long>? ids, CancellationToken ct) {
		var sql = BalanceSql(ids, out var parameters);
		return CoreServiceClient.QuerySqlListAsync<SummaryStock>(sql, parameters, ct);
	}

	static Task<List<MasterTokui>> ReadWarehousesAsync(HashSet<long> ids, CancellationToken ct) {
		var where = WarehouseWhere("Id", ids, out var parameters);
		return CoreServiceClient.QuerySqlListAsync<MasterTokui>(
			$"select Id, Code, Name, IsZaiko, TenType from MasterTokui where {where}", parameters, ct);
	}

	static async Task<Dictionary<long, List<Tran99Meisai>>> ReadDetailsAsync(HashSet<long> ids, CancellationToken ct) {
		Dictionary<long, List<Tran99Meisai>> result = [];
		foreach (var id in ids.Order()) {
			var balance = BalanceSql([id], out var parameters);
			// 派生SKUの重複行は名称だけMAXでまとめ、積送数を増やさない。
			var sql = $"""
				select b.Id_Shohin, b.Id_Col, b.Id_Siz, b.TransitQty Su,
				ms.Code Code_Shohin, ms.Name Mei_Shohin,
				max(ifnull(d.Code_Col,'')) Code_Col, max(ifnull(d.Mei_Col,'')) Mei_Col,
				max(ifnull(d.Code_Siz,'')) Code_Siz, max(ifnull(d.Mei_Siz,'')) Mei_Siz,
				max(ifnull(d.Jan1,'')) JanCode
				from ({balance}) b inner join MasterShohin ms on ms.Id=b.Id_Shohin
				left join DerivedShohinColSiz d on d.Id_Shohin=b.Id_Shohin and d.Id_Col=b.Id_Col and d.Id_Siz=b.Id_Siz
				group by b.Id_Shohin, b.Id_Col, b.Id_Siz, b.TransitQty, ms.Code, ms.Name
				order by b.Id_Shohin, b.Id_Col, b.Id_Siz
				""";
			result.Add(id, await CoreServiceClient.QuerySqlListAsync<Tran99Meisai>(sql, parameters, ct));
		}
		return result;
	}

	static List<Tran11IdoIn> BuildReceipts(Dictionary<long, List<Tran99Meisai>> details, List<MasterTokui> warehouses, DateTime executedAt) {
		var map = warehouses.ToDictionary(x => x.Id);
		var memo = "積送中クリア実行 " + executedAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
		List<Tran11IdoIn> result = [];
		foreach (var pair in details) {
			var warehouse = map[pair.Key];
			foreach (var chunk in pair.Value.Chunk(DetailsPerSlip)) {
				var lines = chunk.Select((x, i) => {
					var line = Common.CloneObject(x);
					line.No = i + 1;
					return line;
				}).ToList();
				result.Add(new Tran11IdoIn {
					DenDay = executedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
					Id_Soko = warehouse.Id, Id_Ido = warehouse.Id,
					VSoko = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
					VIdo = new CodeNameView(warehouse.Id, warehouse.Code, warehouse.Name),
					RelateNo1 = 0, SuTotal = checked((int)lines.Sum(x => (long)x.Su)),
					Memo = memo, Jmeisai = lines,
				});
			}
		}
		return result;
	}
}

public partial class InTransitWarehouseRow : ObservableObject {
	[ObservableProperty]
	public partial bool IsTarget { get; set; }
	public long WarehouseId { get; init; }
	public string WarehouseCode { get; init; } = string.Empty;
	public string WarehouseName { get; init; } = string.Empty;
	public long TransitQty { get; init; }
	public long PositiveQty { get; init; }
	public long NegativeQty { get; init; }
	public int SkuCount { get; init; }
}
