/*
# description
BaseLightMenteViewModel は詳細データの非同期読み込みと更新日時照合を備えた軽量なマスタ保守画面用 ViewModel 基底クラス群です。

# example
public partial class SampleMenteViewModel : BaseLightMenteViewModel<SampleEntity> { }
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase;
using CvBase.Share;
using NPoco;

namespace CvWpfclient.Helpers;

public abstract partial class BaseLightMenteViewModel<T> : BaseMenteViewModel<T> where T : BaseDbClass, new() {
	CancellationTokenSource? detailLoadCts;
	bool suppressCurrentChanged;

	[ObservableProperty]
	public partial bool IsDetailLoading { get; set; }

	protected virtual int DetailLoadDebounceMilliseconds => 200;

	protected override CvMsg CreateListMessage() => CreateLightListMessage();

	protected abstract CvMsg CreateLightListMessage();

	protected override void OnCurrentChangedCore(T? oldValue, T newValue) {
		if (suppressCurrentChanged) {
			ApplyCurrentToEditor(newValue);
			return;
		}
		if (newValue == null) {
			//CancelPendingDetailLoad();
			return;
		}
		ApplyCurrentToEditor(newValue);
		if (newValue.Id <= 0) {
			CancelPendingDetailLoad();
			return;
		}

		_ = ScheduleDetailLoadAsync(newValue.Id, newValue.Vdu);
	}

	protected virtual void ApplyCurrentToEditor(T item) {
		CurrentEdit = Common.CloneObject(item);
		Message = string.Empty;
	}

	/// <summary>
	/// 軽量一覧のクエリパラメータ。ListWhere が生成した @0 形式のプレースホルダに対応する
	/// SelectCodeWhereParameters を必ず添付するため、通常一覧と同じ生成処理を使う。
	/// </summary>
	protected virtual QueryListParam CreateLightListQueryParam() => CreateListQueryParam();

	protected CvMsg CreateSqlListMessage(string selectColumns, QueryListParam? listQuery = null) {
		var query = listQuery ?? CreateLightListQueryParam();
		var sql = $"select {selectColumns} From {ResolveTableName(Tabletype)} {query.AddWhereOrder()}";
		return new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
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

		try {
			IsDetailLoading = true;
			var reply = await SendMessageAsync(new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryByIdParam),
				DataMsg = Common.SerializeObject(new QueryByIdParam(Tabletype, id, vdu))
			}, ct);

			if (Current.Id != id || Current.Vdu != vdu) {
				return;
			}

			if (reply.Code < 0) {
				if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
					HandleConcurrentUpdate();
				}
				return;
			}

			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not T detail) {
				return;
			}

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

	protected override void HandleConcurrentUpdate() {
		CancelPendingDetailLoad();
		base.HandleConcurrentUpdate();
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

	protected override CvMsg CreateLightListMessage() {
		var query = CreateLightListQueryParam();
		if (AdditionalLightweightColumns.Length == 0) {
			return new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSimpleParam),
				DataMsg = Common.SerializeObject(new QueryListSimpleParam(
					itemType: query.ItemType,
					where: query.Where,
					order: query.Order,
					parameters: query.Parameters,
					maxCount: query.MaxCount
				))
			};
		}

		var selectColumns = string.Join(",", ["Id", "Vdc", "Vdu", "Code", "Name", "Ryaku", "Kana", .. AdditionalLightweightColumns]);
		return CreateSqlListMessage(selectColumns, query);
	}
}

public abstract partial class BasePlainLightMenteViewModel<T> : BaseLightMenteViewModel<T> where T : BaseDbClass, new() {
	protected abstract string LightweightSelectColumns { get; }

	protected override CvMsg CreateLightListMessage() => CreateSqlListMessage(LightweightSelectColumns);
}
