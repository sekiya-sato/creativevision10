using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Share;
using NPoco;

namespace Cvnet10Wpfclient.Helpers;

public abstract partial class BaseLightMenteViewModel<T> : BaseMenteViewModel<T> where T : BaseDbClass, new() {
	CancellationTokenSource? detailLoadCts;
	readonly Dictionary<long, T> detailCache = [];
	bool suppressCurrentChanged;

	[ObservableProperty]
	bool isDetailLoading;

	protected virtual int DetailLoadDebounceMilliseconds => 200;

	protected override CvnetMsg CreateListMessage() => CreateLightListMessage();

	protected abstract CvnetMsg CreateLightListMessage();

	protected override void OnCurrentChangedCore(T? oldValue, T newValue) {
		if (suppressCurrentChanged) {
			ApplyCurrentToEditor(newValue);
			return;
		}

		ApplyCurrentToEditor(newValue);
		if (newValue.Id <= 0) {
			CancelPendingDetailLoad();
			return;
		}

		if (TryGetCachedDetail(newValue, out var cachedDetail)) {
			ApplyLoadedDetail(cachedDetail);
			return;
		}

		_ = ScheduleDetailLoadAsync(newValue.Id, newValue.Vdu);
	}

	protected override void AfterInsert(T item) {
		StoreDetailCache(item);
		base.AfterInsert(item);
	}

	protected override void AfterUpdate(T item) {
		StoreDetailCache(Current);
		base.AfterUpdate(item);
	}

	protected override void AfterDelete(T removedItem) {
		RemoveDetailCache(removedItem.Id);
		base.AfterDelete(removedItem);
	}

	protected virtual void ApplyCurrentToEditor(T item) {
		CurrentEdit = Common.CloneObject(item);
		Message = string.Empty;
	}

	protected virtual void StoreDetailCache(T item) {
		detailCache[item.Id] = Common.CloneObject(item);
	}

	protected virtual void RemoveDetailCache(long id) {
		detailCache.Remove(id);
	}

	protected virtual bool TryGetCachedDetail(T item, out T detail) {
		if (detailCache.TryGetValue(item.Id, out var cached) && cached.Vdu == item.Vdu) {
			detail = Common.CloneObject(cached);
			return true;
		}

		detail = new T();
		return false;
	}

	protected virtual QueryListParam CreateLightListQueryParam() =>
		new(
			itemType: Tabletype,
			where: ListWhere,
			order: ListOrder,
			parameters: ListParams,
			maxCount: ListMaxCount
		);

	protected CvnetMsg CreateSqlListMessage(string selectColumns) {
		var query = CreateLightListQueryParam();
		var sql = $"select {selectColumns} From {ResolveTableName(Tabletype)} {query.AddWhereOrder()}";
		return new CvnetMsg {
			Code = 0,
			Flag = CvnetFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(Tabletype, sql, query.Parameters))
		};
	}

	static string ResolveTableName(Type itemType) =>
		itemType.GetCustomAttributes(typeof(TableNameAttribute), true).FirstOrDefault() is TableNameAttribute attr
			? attr.Value
			: itemType.Name;

	async Task ScheduleDetailLoadAsync(long id, long vdu) {
		CancelPendingDetailLoad();
		var cts = new CancellationTokenSource();
		detailLoadCts = cts;

		try {
			await Task.Delay(DetailLoadDebounceMilliseconds, cts.Token);
			await LoadDetailAsync(id, vdu, cts.Token);
		}
		catch (OperationCanceledException) {
		}
		catch (ObjectDisposedException) {
		}
		finally {
			if (ReferenceEquals(detailLoadCts, cts)) {
				detailLoadCts = null;
			}

			try {
				cts.Dispose();
			}
			catch (ObjectDisposedException) {
			}
		}
	}

	async Task LoadDetailAsync(long id, long vdu, CancellationToken ct) {
		if (Current.Id != id || Current.Vdu != vdu) {
			return;
		}

		if (TryGetCachedDetail(Current, out var cachedDetail)) {
			ApplyLoadedDetail(cachedDetail);
			return;
		}

		try {
			IsDetailLoading = true;
			var reply = await SendMessageAsync(new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QuerybyIdParam),
				DataMsg = Common.SerializeObject(new QuerybyIdParam(Tabletype, id))
			}, ct);

			if (reply.Code < 0) {
				return;
			}

			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not T detail) {
				return;
			}

			if (Current.Id != id) {
				return;
			}

			StoreDetailCache(detail);
			ApplyLoadedDetail(detail);
		}
		catch (OperationCanceledException) {
		}
		catch (Exception ex) {
			Message = $"詳細取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			IsDetailLoading = false;
		}
	}

	void ApplyLoadedDetail(T detail) {
		suppressCurrentChanged = true;
		try {
			var target = ListData.FirstOrDefault(x => x.Id == detail.Id);
			if (target != null) {
				Common.DeepCopyValue(Tabletype, detail, target);
				Current = target;
			}
			else {
				Current = Common.CloneObject(detail);
			}

			CurrentEdit = Common.CloneObject(Current);
		}
		finally {
			suppressCurrentChanged = false;
		}
	}

	void CancelPendingDetailLoad() {
		if (detailLoadCts == null) {
			return;
		}

		try {
			detailLoadCts.Cancel();
		}
		catch (ObjectDisposedException) {
		}

		detailLoadCts = null;
	}
}

public abstract partial class BaseCodeNameLightMenteViewModel<T> : BaseLightMenteViewModel<T> where T : BaseDbClass, IBaseCodeName, new() {
	protected virtual string[] AdditionalLightweightColumns => [];

	protected override CvnetMsg CreateLightListMessage() {
		if (AdditionalLightweightColumns.Length == 0) {
			return new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSimpleParam),
				DataMsg = Common.SerializeObject(new QueryListSimpleParam(
					itemType: Tabletype,
					where: ListWhere,
					order: ListOrder,
					parameters: ListParams,
					maxCount: ListMaxCount
				))
			};
		}

		var selectColumns = string.Join(",", ["Id", "Vdc", "Vdu", "Code", "Name", "Ryaku", "Kana", .. AdditionalLightweightColumns]);
		return CreateSqlListMessage(selectColumns);
	}
}

public abstract partial class BasePlainLightMenteViewModel<T> : BaseLightMenteViewModel<T> where T : BaseDbClass, new() {
	protected abstract string LightweightSelectColumns { get; }

	protected override CvnetMsg CreateLightListMessage() => CreateSqlListMessage(LightweightSelectColumns);
}
