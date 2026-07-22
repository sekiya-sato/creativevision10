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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
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
			m.Kingaku = m.Su * m.Tanka;
			UpdateTotals();
		}
		else if (e.PropertyName is nameof(Tran99Meisai.Kingaku) or nameof(Tran99Meisai.Jodai) or nameof(Tran99Meisai.Gedai)) {
			UpdateTotals();
		}
	}

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
