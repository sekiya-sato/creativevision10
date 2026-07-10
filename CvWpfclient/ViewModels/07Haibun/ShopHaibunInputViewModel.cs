using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Linq;

namespace CvWpfclient.ViewModels._07Haibun;

/// <summary>
/// 店舗配分入力 ViewModel。
///
/// 配分元倉庫を選び、対象商品を絞り込んで累計売上・現在庫・配分可能数などを一覧表示し（タブ1）、
/// 選択商品に対して商品・色・サイズ・店舗ごとの配分数を入力して配分データを生成する（タブ2）。
///
/// ※ テーブルは未作成のため、本 ViewModel は DB に依存せずサンプルデータで動作する。
///   確定時に生成する配分レコードのスキーマ案は <see cref="ShopHaibunResult"/> および
///   .omo/ShopHaibunInput_plan.md を参照。本番では倉庫/店舗/商品選択を
///   SelectServerTableView / ShowSelectDialog に、確定を Tran テーブル登録に差し替える想定。
/// </summary>
public partial class ShopHaibunInputViewModel : Helpers.BaseViewModel {

	// ---- 区分 --------------------------------------------------------------
	public const int KubunHatsukai = 0;   // 初回配分
	public const int KubunZaiko = 1;       // 在庫配分

	public sealed record HaibunKubunOption(int Value, string Name);

	public IReadOnlyList<HaibunKubunOption> KubunOptions { get; } = [
		new(KubunHatsukai, "初回配分"),
		new(KubunZaiko, "在庫配分"),
	];

	// ---- タブ / 状態 -------------------------------------------------------
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSearchCommand))]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	[NotifyCanExecuteChangedFor(nameof(AddShopCommand))]
	[NotifyCanExecuteChangedFor(nameof(RemoveShopCommand))]
	[NotifyCanExecuteChangedFor(nameof(DoKakuteiCommand))]
	public partial int SelectedTabIndex { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = "配分元倉庫と条件を指定して「一覧取得(F5)」を押してください。";

	[ObservableProperty]
	public partial int SearchCount { get; set; }

	// ---- 配分元倉庫 --------------------------------------------------------
	[ObservableProperty]
	public partial long Id_Soko { get; set; }

	[ObservableProperty]
	public partial string SokoCode { get; set; } = "";

	[ObservableProperty]
	public partial string SokoName { get; set; } = "";

	[ObservableProperty]
	public partial int Kubun { get; set; } = KubunHatsukai;

	// ---- 検索条件 (FROM-TO) ------------------------------------------------
	[ObservableProperty]
	public partial string SeasonFrom { get; set; } = "";
	[ObservableProperty]
	public partial string SeasonTo { get; set; } = "";
	[ObservableProperty]
	public partial string BrandFrom { get; set; } = "";
	[ObservableProperty]
	public partial string BrandTo { get; set; } = "";
	[ObservableProperty]
	public partial string ItemFrom { get; set; } = "";
	[ObservableProperty]
	public partial string ItemTo { get; set; } = "";
	[ObservableProperty]
	public partial DateTime? UriageDayFrom { get; set; }
	[ObservableProperty]
	public partial DateTime? UriageDayTo { get; set; }
	[ObservableProperty]
	public partial DateTime? NyukaDayFrom { get; set; }

	// ---- タブ1: 商品一覧 ---------------------------------------------------
	[ObservableProperty]
	public partial ObservableCollection<ShopHaibunSearchRow> SearchRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GoToEditCommand))]
	public partial ShopHaibunSearchRow? SelectedSearchRow { get; set; }

	// ---- タブ2: 配分対象商品 + 配分入力 ------------------------------------
	[ObservableProperty]
	public partial string TargetShohinCode { get; set; } = "";
	[ObservableProperty]
	public partial string TargetShohinName { get; set; } = "";
	[ObservableProperty]
	public partial string TargetBrand { get; set; } = "";
	[ObservableProperty]
	public partial string TargetItem { get; set; } = "";
	[ObservableProperty]
	public partial decimal TargetJodai { get; set; }

	[ObservableProperty]
	public partial DateTime? ShijiDay { get; set; } = DateTime.Today;
	[ObservableProperty]
	public partial DateTime? NouhinDay { get; set; } = DateTime.Today;
	[ObservableProperty]
	public partial string Nyuryokusha { get; set; } = "";

	[ObservableProperty]
	public partial ObservableCollection<ShopHaibunEntryRow> EntryRows { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(RemoveShopCommand))]
	public partial ShopHaibunEntryRow? SelectedEntryRow { get; set; }

	/// <summary>配分合計数（指示数の総和）。</summary>
	public int TotalSu => EntryRows.Sum(r => r.Su);

	bool IsSearchTab() => SelectedTabIndex == 0;
	bool IsEditTab() => SelectedTabIndex == 1;
	bool CanGoToEdit() => IsSearchTab() && SelectedSearchRow != null;
	bool CanRemoveShop() => IsEditTab() && SelectedEntryRow != null;

	// ======================================================================
	// コマンド
	// ======================================================================

	[RelayCommand]
	void Init() {
		LoadSampleSoko();
		// デザイン確認用のサンプル一覧。本番では DoSearch で DB から取得。
		DoSearch();
	}

	/// <summary>配分元倉庫を選択。（本番では SelectServerTableView / ShowSelectDialog）</summary>
	[RelayCommand]
	void DoSelectSoko() {
		// TODO: 本番は倉庫マスタ選択ダイアログに差し替え。
		LoadSampleSoko();
		Message = $"配分元倉庫: {SokoCode} {SokoName}";
	}

	void LoadSampleSoko() {
		Id_Soko = 703;
		SokoCode = "000703";
		SokoName = "703 メイン倉庫";
	}

	/// <summary>条件で商品一覧を取得（タブ1）。</summary>
	[RelayCommand(CanExecute = nameof(IsSearchTab))]
	void DoSearch() {
		SearchRows = [.. BuildSampleSearchRows()];
		SearchCount = SearchRows.Count;
		Message = SearchCount > 0
			? $"{SearchCount} 件の商品を取得しました。配分対象を選び「配分画面へ」を押してください。"
			: "対象商品がありません。";
	}

	/// <summary>選択商品を配分画面（タブ2）で開く。</summary>
	[RelayCommand(CanExecute = nameof(CanGoToEdit))]
	void GoToEdit() {
		var row = SelectedSearchRow;
		if (row == null) return;

		TargetShohinCode = row.ShohinCode;
		TargetShohinName = row.ShohinName;
		TargetBrand = row.Brand;
		TargetItem = row.Item;
		TargetJodai = row.Jodai;

		EntryRows = [.. BuildSampleEntryRows(row)];
		HookEntryRows();
		SelectedEntryRow = EntryRows.FirstOrDefault();
		SelectedTabIndex = 1;
		RefreshTotals();
		Message = $"{TargetShohinCode} {TargetShohinName} の配分数を入力してください。";
	}

	[RelayCommand]
	void GoToSearch() => SelectedTabIndex = 0;

	/// <summary>配分先店舗を追加。（本番では店舗マスタ選択ダイアログ）</summary>
	[RelayCommand(CanExecute = nameof(IsEditTab))]
	void AddShop() {
		// TODO: 本番は店舗マスタ選択ダイアログで複数店舗を選択。
		var sample = NextSampleShop();
		if (sample == null) {
			Message = "追加できるサンプル店舗がありません。";
			return;
		}
		foreach (var sku in DistinctSkus()) {
			var newRow = new ShopHaibunEntryRow {
				TenpoCode = sample.Value.Code,
				TenpoName = sample.Value.Name,
				ShohinCode = sku.ShohinCode,
				ColCode = sku.ColCode,
				ColName = sku.ColName,
				SizCode = sku.SizCode,
				SizName = sku.SizName,
				Zaiko = 0,
				KijunZaiko = 0,
				Nouhin = 0,
				Uriage = 0,
			};
			newRow.PropertyChanged += OnEntryRowChanged;
			EntryRows.Add(newRow);
		}
		RefreshTotals();
		Message = $"店舗 {sample.Value.Code} {sample.Value.Name} を追加しました。";
	}

	[RelayCommand(CanExecute = nameof(CanRemoveShop))]
	void RemoveShop() {
		var target = SelectedEntryRow;
		if (target == null) return;
		// 同一店舗の行をまとめて削除。
		var toRemove = EntryRows.Where(r => r.TenpoCode == target.TenpoCode).ToList();
		foreach (var r in toRemove) {
			r.PropertyChanged -= OnEntryRowChanged;
			EntryRows.Remove(r);
		}
		SelectedEntryRow = EntryRows.FirstOrDefault();
		RefreshTotals();
		Message = $"店舗 {target.TenpoCode} を削除しました。";
	}

	/// <summary>配分データを確定（生成）。</summary>
	[RelayCommand(CanExecute = nameof(IsEditTab))]
	void DoKakutei() {
		var results = BuildResults();
		if (results.Count == 0) {
			Message = "配分数(指示数)が入力されていません。";
			return;
		}
		// TODO: 本番は results を配分テーブル(例: Tran20ShopHaibun)へ登録。
		//       スキーマ案は ShopHaibunResult / .omo/ShopHaibunInput_plan.md を参照。
		Message = $"配分データを {results.Count} 件生成しました（合計 {results.Sum(r => r.Su):N0} 点）。※提案段階のため DB 登録は未実装。";
	}

	/// <summary>入力内容をクリアして検索画面へ戻る。</summary>
	[RelayCommand]
	void ClearAll() {
		UnhookEntryRows();
		EntryRows = [];
		SelectedEntryRow = null;
		TargetShohinCode = TargetShohinName = TargetBrand = TargetItem = "";
		TargetJodai = 0;
		SelectedTabIndex = 0;
		RefreshTotals();
		Message = "配分入力をクリアしました。";
	}

	// ======================================================================
	// 確定データ生成
	// ======================================================================

	/// <summary>指示数 &gt; 0 の行から配分レコードを生成する。</summary>
	List<ShopHaibunResult> BuildResults() =>
		EntryRows
			.Where(r => r.Su > 0)
			.Select(r => new ShopHaibunResult(
				HaibunDay: ToYmd8(ShijiDay),
				NouhinDay: ToYmd8(NouhinDay),
				Kubun: Kubun,
				Id_Soko: Id_Soko,
				SokoCode: SokoCode,
				SokoName: SokoName,
				TenpoCode: r.TenpoCode,
				TenpoName: r.TenpoName,
				ShohinCode: r.ShohinCode,
				ColCode: r.ColCode,
				SizCode: r.SizCode,
				Su: r.Su,
				KijunZaiko: r.KijunZaiko,
				Nyuryokusha: Nyuryokusha))
			.ToList();

	static string ToYmd8(DateTime? d) => d?.ToString("yyyyMMdd") ?? "";

	// ======================================================================
	// 明細変更フック（合計再計算）
	// ======================================================================

	void HookEntryRows() {
		foreach (var r in EntryRows) r.PropertyChanged += OnEntryRowChanged;
	}

	void UnhookEntryRows() {
		foreach (var r in EntryRows) r.PropertyChanged -= OnEntryRowChanged;
	}

	void OnEntryRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(ShopHaibunEntryRow.Su)) RefreshTotals();
	}

	void RefreshTotals() => OnPropertyChanged(nameof(TotalSu));

	IEnumerable<(string ShohinCode, string ColCode, string ColName, string SizCode, string SizName)> DistinctSkus() =>
		EntryRows
			.Select(r => (r.ShohinCode, r.ColCode, r.ColName, r.SizCode, r.SizName))
			.Distinct();

	// ======================================================================
	// サンプルデータ（デザイン確認用 / 本番では DB 取得に差し替え）
	// ======================================================================

	static IEnumerable<ShopHaibunSearchRow> BuildSampleSearchRows() => [
		new() { ShohinCode = "20112110001", ShohinName = "ストラップ付ロングキャミ", Brand = "201 axes femme", Item = "10 インナー", Jodai = 1900, UriageSu = 12, Zaiko = 48, GenshiSu = 20, NyukaSu = 60, HaibunKanou = 28 },
		new() { ShohinCode = "20112110002", ShohinName = "ドット柄アウターキャミ", Brand = "201 axes femme", Item = "10 インナー", Jodai = 2800, UriageSu = 5, Zaiko = 30, GenshiSu = 10, NyukaSu = 24, HaibunKanou = 20 },
		new() { ShohinCode = "20112110003", ShohinName = "メルヘン柄アウターキャミ", Brand = "201 axes femme", Item = "10 インナー", Jodai = 2800, UriageSu = 0, Zaiko = 0, GenshiSu = 0, NyukaSu = 36, HaibunKanou = 36 },
		new() { ShohinCode = "20113110002", ShohinName = "巻バラ&シフォンフリルキャミ", Brand = "201 axes femme", Item = "10 インナー", Jodai = 1900, UriageSu = 8, Zaiko = 15, GenshiSu = 6, NyukaSu = 0, HaibunKanou = 9 },
		new() { ShohinCode = "20118110001", ShohinName = "袖フリルレースインナーPO", Brand = "201 axes femme", Item = "10 インナー", Jodai = 2900, UriageSu = 3, Zaiko = 22, GenshiSu = 12, NyukaSu = 12, HaibunKanou = 22 },
	];

	// 商品ごとに 色×サイズ の SKU を作り、初期店舗 2 店分の配分入力行を生成。
	IEnumerable<ShopHaibunEntryRow> BuildSampleEntryRows(ShopHaibunSearchRow src) {
		(string Code, string Name)[] cols = [("003", "赤"), ("014", "紺"), ("015", "黒")];
		(string Code, string Name)[] sizs = [("24", "M")];
		(string Code, string Name)[] shops = [("000029", "イオンモール成田"), ("000031", "イオンモール檜原")];

		foreach (var shop in shops)
			foreach (var col in cols)
				foreach (var siz in sizs)
					yield return new ShopHaibunEntryRow {
						TenpoCode = shop.Code,
						TenpoName = shop.Name,
						ShohinCode = src.ShohinCode,
						ColCode = col.Code,
						ColName = col.Name,
						SizCode = siz.Code,
						SizName = siz.Name,
						Zaiko = 0,
						KijunZaiko = 2,
						Nouhin = 0,
						Uriage = 0,
					};
	}

	// AddShop 用のサンプル店舗（未追加の先頭を返す）。
	(string Code, string Name)? NextSampleShop() {
		(string Code, string Name)[] pool = [
			("000028", "イオンモール盛岡"),
			("000016", "エルパ"),
			("000002", "福井ロジスティクス"),
		];
		var used = EntryRows.Select(r => r.TenpoCode).ToHashSet();
		foreach (var s in pool) if (!used.Contains(s.Code)) return s;
		return null;
	}
}

// ==========================================================================
// 画面内モデル（DB 非依存）
// ==========================================================================

/// <summary>タブ1 商品一覧行。</summary>
public partial class ShopHaibunSearchRow : ObservableObject {
	[ObservableProperty]
	public partial string ShohinCode { get; set; } = "";
	[ObservableProperty]
	public partial string ShohinName { get; set; } = "";
	[ObservableProperty]
	public partial string Brand { get; set; } = "";
	[ObservableProperty]
	public partial string Item { get; set; } = "";
	[ObservableProperty]
	public partial decimal Jodai { get; set; }
	[ObservableProperty]
	public partial int UriageSu { get; set; }    // 累計売上数
	[ObservableProperty]
	public partial int Zaiko { get; set; }        // 現在庫
	[ObservableProperty]
	public partial int GenshiSu { get; set; }     // 現在指示数
	[ObservableProperty]
	public partial int NyukaSu { get; set; }      // 入荷予定数
	[ObservableProperty]
	public partial int HaibunKanou { get; set; }  // 配分可能数
}

/// <summary>タブ2 配分入力行（SKU × 店舗）。指示数(<see cref="Su"/>)を編集。</summary>
public partial class ShopHaibunEntryRow : ObservableObject {
	[ObservableProperty]
	public partial string TenpoCode { get; set; } = "";
	[ObservableProperty]
	public partial string TenpoName { get; set; } = "";
	[ObservableProperty]
	public partial string ShohinCode { get; set; } = "";
	[ObservableProperty]
	public partial string ColCode { get; set; } = "";
	[ObservableProperty]
	public partial string ColName { get; set; } = "";
	[ObservableProperty]
	public partial string SizCode { get; set; } = "";
	[ObservableProperty]
	public partial string SizName { get; set; } = "";
	[ObservableProperty]
	public partial int Su { get; set; }           // 指示数（配分数）
	[ObservableProperty]
	public partial int Zaiko { get; set; }        // 在庫数（参考）
	[ObservableProperty]
	public partial int KijunZaiko { get; set; }   // 基準在庫（参考）
	[ObservableProperty]
	public partial int Nouhin { get; set; }       // 納品数（参考）
	[ObservableProperty]
	public partial int Uriage { get; set; }       // 売上数（参考）
}

/// <summary>
/// 確定時に生成する配分レコード（提案スキーマ）。
/// 1 レコード = 倉庫 × 店舗 × 商品 × 色 × サイズ の配分明細。
/// 本番テーブル案: Tran20ShopHaibun（Id / HaibunDay / NouhinDay / Kubun /
/// Id_Soko+VSoko / Id_Tenpo+VTenpo / Id_Shohin+VShohin / Id_Col+VCol / Id_Siz+VSiz /
/// Su / KijunZaiko / Id_Shain+VShain / Memo）。
/// </summary>
public sealed record ShopHaibunResult(
	string HaibunDay,
	string NouhinDay,
	int Kubun,
	long Id_Soko,
	string SokoCode,
	string SokoName,
	string TenpoCode,
	string TenpoName,
	string ShohinCode,
	string ColCode,
	string SizCode,
	int Su,
	int KijunZaiko,
	string Nyuryokusha);
