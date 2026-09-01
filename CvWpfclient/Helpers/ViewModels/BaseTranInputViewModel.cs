/*
# description
BaseTranInputViewModel は伝票入力画面（発注/受注/仕入/売上 等）の共通 ViewModel 基底クラスです。
明細コレクション(EditMeisai)の管理・合計集計(UpdateTotals)・明細行操作(追加/削除/採番)・
明細変更に伴う金額再計算(OnMeisaiPropertyChanged)・伝票⇔明細の同期(Apply/Sync)を集約します。

伝票固有の差分は以下のフックで各 VM が上書きします:
- OnTotalsUpdated()      : 消費税・総合計などヘッダ合計の再計算（Rate/Tax/Total は各伝票クラス固有のため）
- ResolveMeisaiKubun()   : 明細区分(P/S)の正規化ポリシー
- CreateNewMeisai()      : 新規明細行の既定値
- DetailStatusText       : ヘッダに表示する伝票状態テキスト（新規/伝票No 等）

# example
public partial class HachuInputViewModel : BaseTranInputViewModel<Tran13Hachu>, ITranInputTab { ... }
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace CvWpfclient.Helpers;

/// <summary>
/// 伝票入力画面の共通 ViewModel 基底。TranAllHeader 派生の伝票と Tran99Meisai 明細を扱う。
/// 合計集計・明細操作・状態通知の共通部分を提供し、伝票固有処理はフックで差し込む。
/// </summary>
public abstract partial class BaseTranInputViewModel<TDen> : BasePlainLightMenteViewModel<TDen>
	where TDen : TranAllHeader, new() {

	protected BaseTranInputViewModel() {
		EditMeisai.CollectionChanged += OnEditMeisaiCollectionChanged;
	}

	/// <summary>編集中の明細行。</summary>
	[ObservableProperty]
	public partial ObservableCollection<Tran99Meisai> EditMeisai { get; set; } = [];

	/// <summary>選択中の明細行。</summary>
	[ObservableProperty]
	public partial Tran99Meisai? SelectedMeisai { get; set; }

	/// <summary>明細行数（ヘッダのバッジ表示用）。</summary>
	public int DetailMeisaiCount => EditMeisai.Count;

	/// <summary>ヘッダに表示する伝票状態テキスト（例: "発注 No. 123" / "新規発注"）。</summary>
	public abstract string DetailStatusText { get; }

	partial void OnEditMeisaiChanged(ObservableCollection<Tran99Meisai> oldValue, ObservableCollection<Tran99Meisai> newValue) {
		if (oldValue != null) oldValue.CollectionChanged -= OnEditMeisaiCollectionChanged;
		newValue.CollectionChanged += OnEditMeisaiCollectionChanged;
		OnPropertyChanged(nameof(DetailMeisaiCount));
	}

	void OnEditMeisaiCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		OnPropertyChanged(nameof(DetailMeisaiCount));
	}

	/// <summary>明細行の変更に応じて金額(Su×Tanka)と合計を再計算する（5伝票共通）。</summary>
	protected void OnMeisaiPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (sender is Tran99Meisai m && e.PropertyName is nameof(Tran99Meisai.Su) or nameof(Tran99Meisai.Tanka)) {
			// Kingaku への代入で PropertyChanged が発生し、下の分岐へ再入して税額まで引き直される
			m.Kingaku = m.Su * m.Tanka;
			UpdateTotals();
		}
		else if (e.PropertyName is nameof(Tran99Meisai.Kingaku) or nameof(Tran99Meisai.Jodai) or nameof(Tran99Meisai.Gedai)) {
			UpdateTotals();
		}
		// 金額が変われば税額が、商品が変われば税区分が変わるため明細税額を引き直す
		if (IsMeisaiTaxEnabled && sender is Tran99Meisai target
			&& e.PropertyName is nameof(Tran99Meisai.Kingaku) or nameof(Tran99Meisai.Id_Shohin)) {
			_ = RecalcMeisaiTaxAsync(target, updateTotals: true);
		}
	}

	#region 明細別消費税（Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md 3.1〜3.7）

	/// <summary>
	/// 明細別の消費税計算を行うか。移動・棚卸など金額と税を持たない伝票は false のままにする。
	/// </summary>
	protected virtual bool IsMeisaiTaxEnabled => false;

	/// <summary>Id_Shohin → MasterShohin.Id_Tax のキャッシュ。明細を触るたびにマスタを引き直さないため。</summary>
	readonly Dictionary<long, long> shohinTaxIdCache = [];

	/// <summary>
	/// 伝票日付ごとの消費税区分(1-3)→税率(%)キャッシュ。<see cref="TaxCalculator.Apply"/> の rateOf に渡す。
	/// 伝票日付が変わるたびに区分1-3をまとめて先読みし直す（明細ごとに個別で引かない）。
	/// </summary>
	readonly Dictionary<long, int> taxRateCache = [];
	string? taxRateCacheDenDay;

	/// <summary>
	/// 伝票日付時点の消費税区分1-3の税率をまとめて先読みし、キャッシュを更新する。
	/// 既に同じ伝票日付でキャッシュ済みなら何もしない。
	/// </summary>
	protected async Task EnsureTaxRateCacheAsync(string denDay) {
		if (!IsMeisaiTaxEnabled || taxRateCacheDenDay == denDay) return;
		taxRateCache.Clear();
		for (long taxId = 1; taxId <= 3; taxId++) {
			taxRateCache[taxId] = await AppGlobal.LogicGetTax((int)taxId, denDay);
		}
		taxRateCacheDenDay = denDay;
	}

	/// <summary>
	/// キャッシュ済みの税率を返す。<see cref="TaxCalculator.Apply"/> の rateOf にそのまま渡せる。
	/// Id_Tax&lt;=0(非課税)は0を返す(<see cref="AppGlobal.LogicGetTax"/> は0を渡すと例外になるため呼ばない)。
	/// </summary>
	protected int TaxRateOf(long taxId) => taxId <= 0 ? 0 : taxRateCache.GetValueOrDefault(taxId);

	/// <summary>
	/// 明細1行の消費税区分を、商品マスタから解決し直す。適用税率・税額の確定は
	/// <see cref="TaxCalculator.Apply"/>（各VMの UpdateHeaderTotals）が行う。
	/// </summary>
	protected async Task RecalcMeisaiTaxAsync(Tran99Meisai m, bool updateTotals) {
		if (!IsMeisaiTaxEnabled) return;
		m.Id_Tax = await ResolveMeisaiTaxIdAsync(m.Id_Shohin);
		if (updateTotals) {
			await EnsureTaxRateCacheAsync(CurrentEdit.DenDay);
			UpdateTotals();
		}
	}

	/// <summary>明細全行の消費税区分を再解決してヘッダ合計へ反映する（伝票を開いた時・伝票日付変更時）。</summary>
	protected async Task RecalcAllMeisaiTaxAsync() {
		if (!IsMeisaiTaxEnabled) return;
		await EnsureTaxRateCacheAsync(CurrentEdit.DenDay);
		foreach (var m in EditMeisai) {
			m.Id_Tax = await ResolveMeisaiTaxIdAsync(m.Id_Shohin);
		}
		UpdateTotals();
	}

	/// <summary>
	/// 明細の商品から消費税区分を引く。商品マスタが引けない明細は標準税率(<see cref="TaxCalculator.StandardTaxId"/>)を既定とする。
	/// </summary>
	async Task<long> ResolveMeisaiTaxIdAsync(long idShohin) {
		if (idShohin <= 0) return TaxCalculator.StandardTaxId;
		if (shohinTaxIdCache.TryGetValue(idShohin, out var cached)) return cached;
		var shohin = await AppGlobal.LogicGetMasterById<MasterShohin>(idShohin);
		var taxId = shohin?.Id_Tax ?? TaxCalculator.StandardTaxId;
		shohinTaxIdCache[idShohin] = taxId;
		return taxId;
	}

	#endregion

	/// <summary>明細から数量計/金額計/上代計/下代計を集計する（TranAllHeader 共通フィールド）。</summary>
	protected void UpdateTotals() {
		CurrentEdit.SuTotal = EditMeisai.Sum(m => m.Su);
		CurrentEdit.KingakuTotal = EditMeisai.Sum(m => m.Kingaku);
		CurrentEdit.JodaiTotal = EditMeisai.Sum(m => m.Su * m.Jodai);
		CurrentEdit.GedaiTotal = EditMeisai.Sum(m => m.Su * m.Gedai);
		OnTotalsUpdated();
	}

	/// <summary>ヘッダ合計（消費税/総合計など）の再計算フック。既定は何もしない（Uriage 系）。</summary>
	protected virtual void OnTotalsUpdated() { }

	/// <summary>
	/// 保存した伝票が完了済みの発注/受注に紐付いていれば、気付き用の警告を出す（G0-4.3.1）。
	/// <para>
	/// 完了(<c>EndFlag=1</c>)は自動では解除されない（判断材料 4.3.1）。完了済みの発注・受注へ紐付く
	/// 仕入・出荷を編集しても残へ反映されないため、利用者が残完了設定で調整できるよう保存後に知らせる。
	/// 読み取り失敗は握りつぶす（保存は成功しており業務を妨げない）。
	/// </para>
	/// </summary>
	/// <param name="zanType">紐付く発注(<see cref="Tran13Hachu"/>)／受注(<see cref="Tran12Jyuchu"/>)の型</param>
	/// <param name="relateNo1">保存した伝票の <c>RelateNo1</c>（発注Id／受注Id）。0以下なら何もしない</param>
	/// <param name="denLabel">保存した伝票の名称（"仕入" / "出荷"）</param>
	/// <param name="zanLabel">紐付く残伝票の名称（"発注" / "受注"）</param>
	/// <param name="settingLabel">案内する残完了設定画面の名称</param>
	protected async Task WarnIfLinkedZanCompletedAsync(Type zanType, int relateNo1, string denLabel, string zanLabel, string settingLabel) {
		if (relateNo1 <= 0) {
			return;
		}
		try {
			// Id は long で数値以外を含み得ないためSQLへ直接埋め込む（既存の読み取り規約と同じ）
			var sql = $"SELECT * FROM {zanType.Name} WHERE Id = {relateNo1} AND EndFlag = 1";
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSqlParam),
				DataMsg = Common.SerializeObject(new QueryListSqlParam(zanType, sql, [])),
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(CancellationToken.None));
			if (reply.Code < 0 && reply.Code != -1) {
				return; // 通信・SQLエラーは黙って無視（保存は成功している）
			}
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list || list.Count == 0) {
				return; // 完了していない、または該当なし
			}
			MessageEx.ShowInformationDialog(
				$"この{denLabel}に紐付く{zanLabel} #{relateNo1} は完了済みです。\n"
				+ $"完了は自動では解除されません。数量を変更した場合は、必要に応じて{settingLabel}で確認してください。",
				owner: ActiveWindow);
		}
		catch {
			// 警告表示の失敗は業務に影響しないため無視する
		}
	}

	/// <summary>CurrentEdit.Jmeisai から編集用明細を再構築し、購読・区分正規化・集計を行う。</summary>
	protected void ApplyMeisaiFromCurrentEdit() {
		foreach (var m in EditMeisai) m.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai = new ObservableCollection<Tran99Meisai>(
			CurrentEdit.Jmeisai?.Select(Common.CloneObject) ?? []);
		foreach (var m in EditMeisai) {
			m.Kubun = ResolveMeisaiKubun(m);
			m.PropertyChanged += OnMeisaiPropertyChanged;
		}
		UpdateTotals();
	}

	/// <summary>編集用明細を CurrentEdit.Jmeisai へ書き戻し、区分正規化・集計を行う（保存前）。</summary>
	protected void SyncMeisaiToCurrentEdit() {
		foreach (var m in EditMeisai) m.Kubun = ResolveMeisaiKubun(m);
		CurrentEdit.Jmeisai = [.. EditMeisai];
		UpdateTotals();
	}

	/// <summary>明細区分の正規化ポリシー。既定はそのまま（Uriage 出荷系）。</summary>
	protected virtual int ResolveMeisaiKubun(Tran99Meisai m) => m.Kubun;

	[RelayCommand]
	void AddMeisai() {
		var nextNo = EditMeisai.Count > 0 ? EditMeisai.Max(m => m.No) + 1 : 1;
		var newMeisai = CreateNewMeisai(nextNo);
		newMeisai.PropertyChanged += OnMeisaiPropertyChanged;
		EditMeisai.Add(newMeisai);
		SelectedMeisai = newMeisai;
	}

	[RelayCommand]
	void DeleteMeisai() {
		if (SelectedMeisai == null) return;
		SelectedMeisai.PropertyChanged -= OnMeisaiPropertyChanged;
		EditMeisai.Remove(SelectedMeisai);
		RenumberMeisaiNo();
		SelectedMeisai = EditMeisai.LastOrDefault();
		UpdateTotals();
	}

	/// <summary>新規明細行の生成。既定は行Noのみ設定。伝票ごとに既定区分等を上書き。</summary>
	protected virtual Tran99Meisai CreateNewMeisai(int no) => new() { No = no };

	/// <summary>明細の行Noを 1 から振り直す。採番しない伝票（出荷売上等）は override で無効化する。</summary>
	protected virtual void RenumberMeisaiNo() {
		for (int i = 0; i < EditMeisai.Count; i++) EditMeisai[i].No = i + 1;
	}
}
