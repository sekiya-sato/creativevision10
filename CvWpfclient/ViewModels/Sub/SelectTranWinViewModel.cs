using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace CvWpfclient.ViewModels.Sub;

/// <summary>
/// 汎用の伝票選択ダイアログ（<see cref="Views.Sub.SelectTranWinView"/>）の ViewModel。
/// <para>
/// <see cref="TranAllHeader"/> / <see cref="TranKinHeader"/> を継承する伝票テーブルなら型を問わず一覧表示できる。
/// 既存の <see cref="SelectWinViewModel"/> は Id / Code / Name / Ryaku の 4 列固定で、
/// これらを持たない伝票テーブルには使えないため別ダイアログとして用意した。
/// </para>
/// <para>
/// 伝票ごとに取引先の列名が違う（VTokui / VTenpo / VShiire / VIdo / VTori / VCustomer）ため、
/// 表示は <see cref="TranSelectRow"/> への射影で吸収する。列は静的に定義できるので
/// 動的列生成は行わない。
/// </para>
/// </summary>
public partial class SelectTranWinViewModel : BaseViewModel {
	Type myType = typeof(TranAllHeader);
	string baseWhere = string.Empty;
	string order = "Id DESC";
	string[] baseParameters = [];
	long startPos;
	IReadOnlyDictionary<int, string>? kubunLabels;

	[ObservableProperty]
	public partial string Title { get; set; } = "伝票選択";

	[ObservableProperty]
	public partial ObservableCollection<TranSelectRow> ListData { get; set; } = [];

	[ObservableProperty]
	public partial TranSelectRow? Current { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	/// <summary>取引先列の見出し（伝票種別で意味が変わるので呼び出し側から差し替えられる）。</summary>
	[ObservableProperty]
	public partial string TorisakiHeader { get; set; } = "取引先";

	/// <summary>数量列を出すか。<see cref="TranKinHeader"/>（入金・支払）は数量を持たない。</summary>
	[ObservableProperty]
	public partial bool HasSuTotal { get; set; } = true;

	// ===== 絞り込み条件（ダイアログ内で直接指定する） =====

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial DateTime? DenDayFrom { get; set; }

	[ObservableProperty]
	public partial DateTime? DenDayTo { get; set; }

	[ObservableProperty]
	public partial int MaxCount { get; set; } = AppGlobal.Limit;

	/// <summary>
	/// 表示対象を設定する。
	/// </summary>
	/// <param name="tranType">
	/// 伝票テーブルの型。<see cref="TranAllHeader"/> または <see cref="TranKinHeader"/> の派生であること。
	/// </param>
	/// <param name="where">呼び出し側が固定する絞り込み条件（ダイアログ内の条件と AND で結合する）。</param>
	/// <param name="order">並び順。既定は伝票Noの降順。</param>
	/// <param name="parameters"><paramref name="where"/> で使うバインド値（@0 から）。</param>
	/// <param name="startPos">初期選択したい伝票Id。</param>
	/// <param name="title">ダイアログのタイトル。</param>
	/// <param name="torisakiHeader">取引先列の見出し（例: 仕入先 / 得意先 / 移動先）。</param>
	/// <param name="kubunLabels">
	/// 区分の表示名。伝票ごとに同じ値でも呼び方が違う（10 は発注では「発注」、仕入では「仕入」）ため、
	/// 正確に出したい場合は呼び出し側から渡す。省略時は enum 名からの既定表示になる。
	/// </param>
	public void SetParam(
		Type tranType,
		string where = "",
		string order = "Id DESC",
		string[]? parameters = null,
		long startPos = 0,
		string title = "伝票選択",
		string torisakiHeader = "取引先",
		IReadOnlyDictionary<int, string>? kubunLabels = null) {
		ArgumentNullException.ThrowIfNull(tranType);
		if (!typeof(TranAllHeader).IsAssignableFrom(tranType) && !typeof(TranKinHeader).IsAssignableFrom(tranType)) {
			throw new ArgumentException($"{tranType.Name} は伝票テーブル(TranAllHeader / TranKinHeader の派生)ではありません。", nameof(tranType));
		}

		myType = tranType;
		baseWhere = where;
		this.order = string.IsNullOrWhiteSpace(order) ? "Id DESC" : order;
		baseParameters = parameters ?? [];
		this.startPos = startPos;
		this.kubunLabels = kubunLabels;
		Title = title;
		TorisakiHeader = torisakiHeader;
		HasSuTotal = typeof(TranAllHeader).IsAssignableFrom(tranType);
		MaxCount = AppGlobal.Limit;
	}

	[RelayCommand]
	async Task Init(CancellationToken ct) => await DoSearch(ct);

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSearch(CancellationToken ct) {
		try {
			ClientLib.Cursor2Wait();
			List<string> parameters = [.. baseParameters];
			string? where = SelectDisplayConditionHelper.CombineWhere(baseWhere, BuildConditionWhere(parameters));

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var queryParam = new QueryListParam(
				itemType: myType, where: where, order: order,
				parameters: [.. parameters], maxCount: MaxCount > 0 ? MaxCount : null);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(queryParam),
			};
			CvMsg reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			if (reply.Code < 0 && reply.Code != -1) {
				throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
			}

			ObservableCollection<TranSelectRow> rows = [];
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				var accessor = TranRowAccessor.For(myType);
				foreach (object? item in list) {
					if (item != null) rows.Add(accessor.CreateRow(item, kubunLabels));
				}
			}
			ListData = rows;
			Count = rows.Count;
			Current = startPos != 0
				? rows.FirstOrDefault(x => x.Id == startPos) ?? rows.FirstOrDefault()
				: rows.FirstOrDefault();
			Message = $"{Count:N0} 件";
		}
		catch (OperationCanceledException) {
			Message = "取得を中断しました";
		}
		catch (Exception ex) {
			Message = $"データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand]
	void ClearConditions() {
		DenNoFrom = string.Empty;
		DenNoTo = string.Empty;
		DenDayFrom = null;
		DenDayTo = null;
		MaxCount = AppGlobal.Limit;
	}

	[RelayCommand]
	public void DoSelect() {
		if (Current == null) {
			MessageEx.ShowWarningDialog("選択されていません", owner: ClientLib.GetActiveView(this));
			return;
		}
		ClientLib.ExitDialogResult(this, true);
	}

	/// <summary>選択された伝票の実体を取り出す。</summary>
	public T? GetCurrent<T>() where T : class => Current?.Source as T;

	/// <summary>ダイアログ内で指定された条件を WHERE 断片へ組み立てる。</summary>
	string BuildConditionWhere(List<string> parameters) {
		List<string> clauses = [];
		if (long.TryParse(DenNoFrom.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long from)) {
			clauses.Add($"Id >= {AddParameter(parameters, from)}");
		}
		if (long.TryParse(DenNoTo.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long to)) {
			clauses.Add($"Id <= {AddParameter(parameters, to)}");
		}
		if (DenDayFrom is DateTime dayFrom) {
			clauses.Add($"DenDay >= {AddParameter(parameters, dayFrom.ToString("yyyyMMdd"))}");
		}
		if (DenDayTo is DateTime dayTo) {
			clauses.Add($"DenDay <= {AddParameter(parameters, dayTo.ToString("yyyyMMdd"))}");
		}
		return string.Join(" AND ", clauses);
	}

	/// <summary>呼び出し側のバインド値の後ろへ追加するので、番号は現在の要素数から採る。</summary>
	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}
}

/// <summary>
/// 伝票型ごとのプロパティ位置を1度だけ解決して使い回すアクセサ。
/// 伝票1件ごとにリフレクション検索をやり直さないためにキャッシュする。
/// </summary>
sealed class TranRowAccessor {
	static readonly Dictionary<Type, TranRowAccessor> cache = [];
	static readonly Lock cacheLock = new();

	PropertyInfo? id;
	PropertyInfo? denDay;
	PropertyInfo? kubun;
	PropertyInfo? enKubun;
	PropertyInfo? torisaki;
	PropertyInfo? soko;
	PropertyInfo? shain;
	PropertyInfo? suTotal;
	PropertyInfo? kingakuTotal;
	PropertyInfo? memo;

	public static TranRowAccessor For(Type type) {
		lock (cacheLock) {
			if (cache.TryGetValue(type, out TranRowAccessor? found)) return found;
			var accessor = Build(type);
			cache[type] = accessor;
			return accessor;
		}
	}

	static TranRowAccessor Build(Type type) {
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
		var accessor = new TranRowAccessor {
			id = Find(properties, nameof(BaseDbClass.Id)),
			denDay = Find(properties, nameof(TranAllHeader.DenDay)),
			kubun = properties.FirstOrDefault(x => x.Name == "Kubun" && x.PropertyType == typeof(int)),
			enKubun = properties.FirstOrDefault(x => x.Name == "EnKubun" && x.PropertyType.IsEnum),
			soko = Find(properties, nameof(TranAllHeader.VSoko)),
			shain = Find(properties, nameof(TranAllHeader.VShain)),
			suTotal = Find(properties, nameof(TranAllHeader.SuTotal)),
			kingakuTotal = Find(properties, nameof(TranAllHeader.KingakuTotal)),
			memo = Find(properties, nameof(TranAllHeader.Memo)),
		};
		accessor.torisaki = ResolveTorisaki(properties);
		return accessor;
	}

	static PropertyInfo? Find(PropertyInfo[] properties, string name) =>
		properties.FirstOrDefault(x => x.Name == name);

	/// <summary>
	/// 取引先にあたる <see cref="CodeNameView"/> 列を決める。
	/// 既知の名前を優先し、見つからなければ入力者・倉庫以外の CodeNameView を採用する。
	/// </summary>
	static PropertyInfo? ResolveTorisaki(PropertyInfo[] properties) {
		PropertyInfo[] codeNameViews = [.. properties.Where(x => x.PropertyType == typeof(CodeNameView))];
		foreach (string name in SelectTranWinViewModelNames.Torisaki) {
			PropertyInfo? found = codeNameViews.FirstOrDefault(x => x.Name == name);
			if (found != null) return found;
		}
		return codeNameViews.FirstOrDefault(x => !SelectTranWinViewModelNames.NonTorisaki.Contains(x.Name));
	}

	public TranSelectRow CreateRow(object source, IReadOnlyDictionary<int, string>? kubunLabels) {
		int? kubunValue = kubun?.GetValue(source) as int?;
		return new TranSelectRow(source) {
			Id = id?.GetValue(source) as long? ?? 0,
			DenDayDisplay = FormatYmd8(denDay?.GetValue(source) as string),
			KubunDisplay = FormatKubun(kubunValue, enKubun?.GetValue(source), kubunLabels),
			TorisakiDisplay = FormatCodeName(torisaki?.GetValue(source) as CodeNameView),
			SokoDisplay = FormatCodeName(soko?.GetValue(source) as CodeNameView),
			ShainDisplay = FormatCodeName(shain?.GetValue(source) as CodeNameView),
			SuTotal = suTotal?.GetValue(source) as int? ?? 0,
			KingakuTotal = kingakuTotal?.GetValue(source) as int? ?? 0,
			Memo = memo?.GetValue(source) as string ?? string.Empty,
		};
	}

	/// <summary>
	/// 区分の表示。呼び出し側の指定を最優先し、次に enum 名、最後に数値のみとする。
	/// 同じ 10 でも伝票により「発注」「仕入」「売上」と呼び名が違うので、
	/// 正確さが要るときは <c>kubunLabels</c> を渡してもらう前提にしている。
	/// </summary>
	static string FormatKubun(int? kubun, object? enKubun, IReadOnlyDictionary<int, string>? kubunLabels) {
		if (kubun is not int value) return string.Empty;
		if (kubunLabels != null && kubunLabels.TryGetValue(value, out string? label)) return $"{value} {label}";
		string name = enKubun switch {
			null => string.Empty,
			_ => enKubun.ToString() ?? string.Empty,
		};
		string japanese = name switch {
			"Uriage" => "売上",
			"UriSale" => "売上(セール)",
			"Shiire" => "仕入",
			"Henpin" => "返品",
			"HenSale" => "返品(セール)",
			"Nebiki" => "値引",
			"Other" => "その他",
			_ => name,
		};
		return japanese.Length == 0 ? value.ToString(CultureInfo.InvariantCulture) : $"{value} {japanese}";
	}

	static string FormatYmd8(string? value) =>
		DateTime.TryParseExact(value, "yyyyMMdd", null, DateTimeStyles.None, out DateTime result)
			? result.ToString("yyyy/MM/dd")
			: string.Empty;

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatCodeName(CodeNameView? value) =>
		value == null ? string.Empty : CodeNameDisplay.Format(value.Sid, value.Cd, value.Mei);
}

/// <summary>取引先列を解決するための列名（<see cref="TranRowAccessor"/> から参照する）。</summary>
static class SelectTranWinViewModelNames {
	public static readonly string[] Torisaki = [
		nameof(Tran00Uriage.VTokui),
		nameof(Tran01Tenuri.VTenpo),
		nameof(Tran03Shiire.VShiire),
		nameof(Tran05Ido.VIdo),
		"VTori",
		nameof(Tran01Tenuri.VCustomer),
	];

	public static readonly string[] NonTorisaki = [
		nameof(TranAllHeader.VShain),
		nameof(TranAllHeader.VSoko),
	];
}

/// <summary>伝票一覧の1行。伝票種別によらない共通項目へ射影したもの。</summary>
public sealed class TranSelectRow(object source) {
	/// <summary>元の伝票エンティティ。<see cref="SelectTranWinViewModel.GetCurrent{T}"/> で取り出す。</summary>
	public object Source { get; } = source;

	/// <summary>伝票No（= 各伝票テーブルの Id）</summary>
	public long Id { get; init; }
	public string DenDayDisplay { get; init; } = string.Empty;
	public string KubunDisplay { get; init; } = string.Empty;
	public string TorisakiDisplay { get; init; } = string.Empty;
	public string SokoDisplay { get; init; } = string.Empty;
	public string ShainDisplay { get; init; } = string.Empty;
	public int SuTotal { get; init; }
	public int KingakuTotal { get; init; }
	public string Memo { get; init; } = string.Empty;
}
