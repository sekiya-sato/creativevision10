using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 仕入返品入力 — 「返品する倉庫にある、その仕入先の商品」を一覧で引き、
/// 返品数を埋めて仕入伝票(<see cref="Tran03Shiire"/> 区分20 仕入返品)を1件作る画面。
/// <para>
/// 【一覧方式にした理由】返品は「今その倉庫にある在庫を仕入先へ送り返す」作業なので、
/// 商品を1行ずつ選ぶ伝票明細方式より、対象SKUを先に全部並べて数量だけ直す方が実務に合う。
/// そのため棚卸入力(一覧方式)・在庫移動入力と同じ <see cref="BaseStockSheetInputViewModel{TDen}"/> を使う。
/// 作成後の修正・削除は【商品仕入入力】(<see cref="ShiireInputViewModel"/>)で行う。
/// </para>
/// <para>
/// 【仕入先で商品を絞る仕組み】商品マスタのメーカー(<c>MasterShohin.Id_Maker</c> → <c>MasterMeisho.Kubun='MKR'</c>)の
/// コードが仕入先コードと一致するものを「その仕入先の商品」とみなす。Id での関連は張られていないため
/// コード一致で突き合わせる（旧システムからの引き継ぎ仕様）。
/// </para>
/// <para>
/// 【減算になる仕組み】<c>Tran03Shiire.Kubun</c> を 20(仕入返品)にすると <c>OnKubunChanged</c> が
/// <c>CalcFlag = -1</c> を立てる。在庫集計(<c>SummaryDb.CreateSummaryStockSql</c>)は
/// <c>Su * CalcFlag * calcFlag</c> で積むので、**数量はプラスのまま登録する**。
/// マイナスを入れると符号が二重に反転して在庫が増えてしまう。
/// </para>
/// </summary>
public partial class HenpinInputViewModel : BaseStockSheetInputViewModel<Tran03Shiire> {
	/// <summary>取引区分の選択肢。現状は 20 仕入返品 固定（画面上は選択済みの1件のみ）。</summary>
	public sealed record KubunOption(EnumShiire Value, string Name);

	/// <summary>コンボボックス用のマスタ1件。表示は「コード 名称」。</summary>
	public sealed record MasterOption(long Id, string Code, string Name) {
		public string Display => CodeNameDisplay.Format(Id, Code, Name, withId: false);
	}

	protected override string QueryTitle => "仕入返品入力";
	protected override string InputSuHeader => "数量";

	public IReadOnlyList<KubunOption> KubunOptions { get; } = [
		new(EnumShiire.Henpin, "20 仕入返品"),
	];

	[ObservableProperty]
	public partial EnumShiire SelectedKubun { get; set; } = EnumShiire.Henpin;

	/// <summary>
	/// 仕入日。基底が持つ <see cref="BaseStockSheetInputViewModel{TDen}.DenDayText"/>(文字列)を
	/// DatePicker 用に見せる。日付入力は他の仕入伝票画面(商品仕入入力)と同じ DatePicker に揃える。
	/// </summary>
	public DateTime? DenDay {
		get => DateTime.TryParseExact(DenDayText.Trim(), "yyyy/MM/dd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
		set {
			DenDayText = value?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? string.Empty;
			OnPropertyChanged();
		}
	}

	// ---- マスタ選択（Id を選び「コード 名称」を表示する） ------------------------

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> ShiireOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> SokoOptions { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<MasterOption> ShainOptions { get; set; } = [];

	/// <summary>仕入先。返品先であり、対象商品の絞り込み条件でもある。</summary>
	[ObservableProperty]
	public partial MasterOption? SelectedShiire { get; set; }

	/// <summary>倉庫。返品する在庫の置き場所。基底は SokoCode 起点で解決するので同期させる。</summary>
	[ObservableProperty]
	public partial MasterOption? SelectedSoko { get; set; }

	/// <summary>入力者。伝票の担当者(Id_Shain)になる。</summary>
	[ObservableProperty]
	public partial MasterOption? SelectedShain { get; set; }

	partial void OnSelectedSokoChanged(MasterOption? value) {
		// 基底(BaseStockSheetInputViewModel)は SokoCode から倉庫Idを解決するので、選択を必ず反映する
		SokoCode = value?.Code ?? string.Empty;
		SokoName = value?.Name ?? string.Empty;
	}

	partial void OnSelectedShainChanged(MasterOption? value) {
		ShainCode = value?.Code ?? string.Empty;
		ShainName = value?.Name ?? string.Empty;
	}

	/// <summary>登録時に使う掛計上日(yyyyMMdd)。ValidateBeforeRegisterAsync で確定する。</summary>
	string kakeDay = string.Empty;

	/// <summary>
	/// 登録時に使う Id_Shohin 別の消費税区分と税率。ValidateBeforeRegisterAsync で確定する。
	/// <para>
	/// BuildDenpyo が同期メソッドのため、マスタ参照が要る解決は先にここへ集めておく。
	/// </para>
	/// </summary>
	readonly Dictionary<long, (long TaxId, int Rate)> meisaiTaxByShohin = [];

	/// <summary>登録時に使う仕入先掛率(%)。Tran03Shiire.Rate は掛率であり消費税率ではない。ValidateBeforeRegisterAsync で確定する。</summary>
	int shiireRatePercent = 100;

	public HenpinInputViewModel() {
		// 返品対象は「在庫のある行」だけ。数量の初期値は在庫数（全数返品が既定で、減らして使う）
		IsZeroExcluded = true;
		IsPrefillTheoretical = true;
	}

	/// <summary>View の ContentRendered から呼ばれる。マスタのコンボボックスを埋める。</summary>
	protected override void Init() => LoadMastersCommand.Execute(null);

	protected override void OnClearConditions() {
		base.OnClearConditions();
		SelectedKubun = EnumShiire.Henpin;
		SelectedShiire = null;
		SelectedSoko = null;
		SelectedShain = null;
		IsZeroExcluded = true;
		IsPrefillTheoretical = true;
		DenDay = DateTime.Now.Date;
	}

	// ---- マスタ読み込み ----------------------------------------------------------

	[RelayCommand]
	async Task LoadMasters(CancellationToken ct) {
		try {
			ShiireOptions = new ObservableCollection<MasterOption>(
				await LoadOptionsAsync<MasterShiire>("MasterShiire", string.Empty, ct));
			SokoOptions = new ObservableCollection<MasterOption>(
				await LoadOptionsAsync<MasterTokui>("MasterTokui", "WHERE TenType IN (0, 3, 6)", ct));
			ShainOptions = new ObservableCollection<MasterOption>(
				await LoadOptionsAsync<MasterShain>("MasterShain", string.Empty, ct));
			Message = "仕入先・倉庫・入力者を選び、[在庫取得] で返品対象を表示してください";
		}
		catch (OperationCanceledException) {
			// 画面を閉じた等。何もしない
		}
		catch (Exception ex) {
			Message = $"マスタ取得失敗: {ex.Message}";
		}
	}

	async Task<List<MasterOption>> LoadOptionsAsync<T>(string tableName, string where, CancellationToken ct)
		where T : BaseDbClass, IBaseCodeName {
		var sql = $@"
SELECT Id, Vdc, Vdu, Code, Name, Ryaku, Kana
FROM {tableName}
{where}
ORDER BY Code";
		var list = await QuerySqlListAsync<T>(sql, [], ct);
		return [.. list.Select(x => new MasterOption(x.Id, x.Code ?? string.Empty, x.Name ?? string.Empty))];
	}

	// ---- 選択ダイアログ（コンボが長いときのコード検索用） ------------------------

	[RelayCommand]
	void SelectShiireDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterShiire>(this, typeof(MasterShiire), "", "Code",
			startPos: SelectedShiire?.Id ?? 0);
		if (selected == null) return;
		SelectedShiire = FindOrAdd(ShiireOptions, selected.Id, selected.Code, selected.Name);
	}

	[RelayCommand]
	void SelectSokoDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterTokui>(this, typeof(MasterTokui), "TenType in (0,3,6)", "Code",
			startPos: SelectedSoko?.Id ?? 0);
		if (selected == null) return;
		SelectedSoko = FindOrAdd(SokoOptions, selected.Id, selected.Code, selected.Name);
	}

	[RelayCommand]
	void SelectShainDialog() {
		var selected = PrintPdfHelper.ShowSelectDialog<MasterShain>(this, typeof(MasterShain), "", "Code",
			startPos: SelectedShain?.Id ?? 0);
		if (selected == null) return;
		SelectedShain = FindOrAdd(ShainOptions, selected.Id, selected.Code, selected.Name);
	}

	// コンボの ItemsSource に無いレコードを選んでも SelectedItem が外れないよう、必要なら足してから選ぶ
	static MasterOption FindOrAdd(ObservableCollection<MasterOption> options, long id, string? code, string? name) {
		var found = options.FirstOrDefault(x => x.Id == id);
		if (found != null) return found;
		var added = new MasterOption(id, code ?? string.Empty, name ?? string.Empty);
		options.Add(added);
		return added;
	}

	// ---- 在庫取得 ----------------------------------------------------------------

	protected override async Task OnSearchAsync(CancellationToken ct) {
		if (SelectedShiire == null) {
			MessageEx.ShowWarningDialog("仕入先を指定してください。", owner: ActiveWindow);
			return;
		}
		await base.OnSearchAsync(ct);
		if (Rows.Count == 0) return;

		// 対象は仕入先でも絞っているので、基底のメッセージ(倉庫のみ)に仕入先を足す。
		// 上限で切られた可能性がある場合は、取りこぼしに気付けるよう明示する。
		var shiire = $"仕入先 {SelectedShiire?.Display}";
		var capped = TryGetMaxCount(out var maxCount) && RowCount >= maxCount
			? $" ※取得件数上限({maxCount})に達しています"
			: string.Empty;
		Message = $"{Message} / {shiire}{capped}";
	}

	/// <summary>
	/// 該当倉庫の在庫のうち、商品のメーカーコードが仕入先コードと一致するSKUだけを返す。
	/// 在庫が無い(0以下の)行は返品対象にならないので SQL 側で落とす。
	/// </summary>
	protected override async Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) {
		var shiireCode = SelectedShiire?.Code ?? string.Empty;
		List<string> parameters = [];
		var sql = $@"
SELECT s.Id, s.Vdc, s.Vdu, s.Id_Soko, s.Id_Shohin, s.Id_Col, s.Id_Siz, s.Su
FROM SummaryRealStock s
     INNER JOIN MasterShohin sh ON sh.Id = s.Id_Shohin
     INNER JOIN MasterMeisho mk ON mk.Id = sh.Id_Maker AND mk.Kubun = 'MKR'
WHERE s.Id_Soko = {AddSqlParameter(parameters, IdSoko)}
  AND mk.Code = {AddSqlParameter(parameters, shiireCode)}
  AND s.Su > 0";
		var stock = await QuerySqlListAsync<SummaryRealStock>(sql, parameters, ct);

		var stockMap = stock
			.GroupBy(s => (s.Id_Shohin, s.Id_Col, s.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		var shohinMap = await LoadShohinMapAsync(stockMap.Keys.Select(k => k.Id_Shohin), ct);
		var skuMap = await LoadSkuMapAsync(stockMap.Keys.Select(k => k.Id_Shohin), ct);

		List<StockSheetRow> rows = [];
		foreach (var (key, su) in stockMap) {
			shohinMap.TryGetValue(key.Id_Shohin, out var shohin);
			skuMap.TryGetValue(key, out var sku);
			rows.Add(CreateRow(key.Id_Shohin, key.Id_Col, key.Id_Siz, su, shohin, sku));
		}
		return rows;
	}

	// ---- 実行（仕入返品伝票の登録） ----------------------------------------------

	protected override async Task<bool> ValidateBeforeRegisterAsync(CancellationToken ct) {
		if (SelectedShiire == null) {
			MessageEx.ShowWarningDialog("仕入先を指定してください。", owner: ActiveWindow);
			return false;
		}
		if (!TryParseDate(DenDayText, out var denDay)) return false;

		// 返品数は在庫を減らす向きに CalcFlag=-1 で計上されるため、必ずプラスで入力する
		var minus = Rows.Where(r => r.InputSu < 0).ToList();
		if (minus.Count > 0) {
			MessageEx.ShowWarningDialog(
				$"数量がマイナスの行が {minus.Count} 件あります。仕入返品の数量はプラスで入力してください。",
				owner: ActiveWindow);
			return false;
		}

		// 在庫を超える返品も伝票としては作れてしまうので、ここで気付けるよう警告する
		var over = Rows.Where(r => r.InputSu > r.TheoreticalSu).ToList();
		if (over.Count > 0) {
			var head = string.Join("\n", over.Take(5).Select(r =>
				$"{r.Code_Shohin} {r.Code_Col} {r.Code_Siz}: 数量{r.InputSu} > 在庫{r.TheoreticalSu}"));
			var more = over.Count > 5 ? $"\n… 他 {over.Count - 5} 件" : string.Empty;
			if (MessageEx.ShowQuestionDialog(
					$"在庫数を超える数量の行が {over.Count} 件あります。続行しますか？\n{head}{more}",
					owner: ActiveWindow) != System.Windows.MessageBoxResult.Yes) {
				return false;
			}
		}

		// 掛計上日は仕入日と同じ。税率は仕入日時点のものを取る（商品仕入入力と同じ扱い）
		kakeDay = ToDenDay(denDay);
		await LoadMeisaiTaxAsync();
		// 掛率はコンボ(MasterOption)にCode/Nameしか無いためIdで1件取得し直す
		var fullShiire = await AppGlobal.LogicGetMasterById<MasterShiire>(SelectedShiire?.Id ?? 0);
		if (fullShiire != null) shiireRatePercent = fullShiire.RateProper;
		return true;
	}

	/// <summary>
	/// 返品対象行の商品から消費税区分を引き、掛計上日時点の税率と併せて保持する。
	/// 商品マスタが引けない明細は標準税率(1)を既定とする。
	/// </summary>
	async Task LoadMeisaiTaxAsync() {
		const long standardTaxId = 1;
		meisaiTaxByShohin.Clear();
		var idList = Rows.Where(r => r.InputSu != 0).Select(r => r.Id_Shohin).Distinct();
		foreach (var idShohin in idList) {
			var shohin = idShohin > 0 ? await AppGlobal.LogicGetMasterById<MasterShohin>(idShohin) : null;
			var taxId = shohin?.Id_Tax ?? standardTaxId;
			// Id_Tax=0 は非課税。LogicGetTax(0,...) は MasterSysTax を引けず例外になるため呼ばない
			var rate = taxId <= 0 ? 0 : await AppGlobal.LogicGetTax((int)taxId, kakeDay);
			meisaiTaxByShohin[idShohin] = (taxId, rate);
		}
	}

	protected override Tran03Shiire BuildDenpyo(List<Tran99Meisai> meisai) {
		var shiire = SelectedShiire;
		var kingakuTotal = meisai.Sum(m => m.Kingaku);
		// 消費税・総合計の積み方は商品仕入入力(ShiireInputViewModel.UpdateHeaderTotals)と揃える。
		// 明細Taxは常に正値で持ち、返品のマイナス計上は Kubun=20 が立てる CalcFlag=-1 が担う
		foreach (var m in meisai) {
			var (taxId, rate) = meisaiTaxByShohin.TryGetValue(m.Id_Shohin, out var found) ? found : (1L, 0);
			m.Id_Tax = taxId;
			m.TaxRate = rate;
			m.Tax = (int)Math.Round(Math.Abs(m.Kingaku) * rate / 100.0);
		}
		var absKingakuTotal = Math.Abs(kingakuTotal);
		var tax = meisai.Sum(m => m.Tax);
		return new Tran03Shiire {
			// Kubun に 20 を入れると OnKubunChanged が CalcFlag = -1 を立てる（在庫・買掛が減算になる）
			Kubun = (int)SelectedKubun,
			KakeDay = kakeDay,
			Id_Shiire = shiire?.Id ?? 0,
			VShiire = new CodeNameView {
				Sid = shiire?.Id ?? 0,
				Cd = shiire?.Code ?? string.Empty,
				Mei = shiire?.Name ?? string.Empty,
			},
			Rate = shiireRatePercent,
			Tax = tax,
			Total = absKingakuTotal + tax,
		};
	}
}
