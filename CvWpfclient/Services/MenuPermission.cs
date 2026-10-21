using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using System.Collections;

namespace CvWpfclient.Services;

/// <summary>
/// ログインユーザがメニューを実行できるかの判定（Doc/spec/2026-10-20_メニュー実行権限と個人メニュー_最小変更設計案.md §5）。
/// <para>
/// ログイン時に1回読み込み、次回ログインまで変えない。業務メニューの出し分けでありセキュリティ境界ではない（J1）。
/// 判定順: FullAccess → 明示禁止 → 明示許可 → 既定ポリシー（Audit/Warn=許可、Deny=不可）。
/// 未割当・無効・存在しないプロファイルは明細なしとして既定ポリシーに従う（J4）。
/// </para>
/// </summary>
public sealed class MenuPermission {
	/// <summary>判定しない（全メニュー実行可）。ログイン前・権限情報の読込失敗時に使う</summary>
	public static MenuPermission AllowAll { get; } = new(SysPermissionProfile.FullAccessId, EnumPermissionDefaultMode.Audit, new Dictionary<string, bool>());

	/// <summary>判定に使ったプロファイルId（0=未割当）</summary>
	public long ProfileId { get; }
	/// <summary>明細に無いメニューの既定ポリシー</summary>
	public EnumPermissionDefaultMode DefaultMode { get; }
	readonly IReadOnlyDictionary<string, bool> viewRules;

	public MenuPermission(long profileId, EnumPermissionDefaultMode defaultMode, IReadOnlyDictionary<string, bool> viewRules) {
		ProfileId = profileId;
		DefaultMode = defaultMode;
		this.viewRules = viewRules;
	}

	/// <summary>FullAccess（明細・既定ポリシーに関係なく全許可）か</summary>
	public bool IsFullAccess => ProfileId == SysPermissionProfile.FullAccessId;

	/// <summary>メニューを実行できるか。FunctionIdを持たない項目は設定できないので常に実行できる</summary>
	public bool CanOpen(string? functionId) {
		if (IsFullAccess || string.IsNullOrEmpty(functionId)) {
			return true;
		}
		if (viewRules.TryGetValue(functionId, out var allowed)) {
			return allowed;
		}
		return DefaultMode != EnumPermissionDefaultMode.Deny;
	}

	/// <summary>明細が無く既定ポリシーで決まるか（Warn の警告ログに使う）</summary>
	public bool IsByDefault(string? functionId) =>
		!IsFullAccess && !string.IsNullOrEmpty(functionId) && !viewRules.ContainsKey(functionId);

	/// <summary>
	/// プロファイルの明細と既定ポリシーをサーバから読み込む。通信・サーバのエラーは例外にする（呼び出し側で全許可に戻す）。
	/// </summary>
	public static async Task<MenuPermission> LoadAsync(long profileId, CancellationToken ct) {
		if (profileId == SysPermissionProfile.FullAccessId) {
			return new MenuPermission(profileId, EnumPermissionDefaultMode.Audit, new Dictionary<string, bool>());
		}
		var sysman = (await QueryAsync<MasterSysman>(new QueryListParam(typeof(MasterSysman)), ct)).FirstOrDefault();
		var mode = sysman?.EnPermissionDefaultMode ?? EnumPermissionDefaultMode.Audit;
		var rules = new Dictionary<string, bool>(StringComparer.Ordinal);
		if (profileId > 0) {
			// Idは数値なので条件へ直接埋め込む（文字列パラメータでは数値列と一致しないため）
			var profile = (await QueryAsync<SysPermissionProfile>(new QueryListParam(typeof(SysPermissionProfile), $"Id={profileId}"), ct)).FirstOrDefault();
			if (profile?.IsActive == true) {
				var details = await QueryAsync<SysPermissionProfileDetail>(new QueryListParam(typeof(SysPermissionProfileDetail),
					$"Id_PermissionProfile={profileId} and PermissionType={(int)EnumPermissionType.View}"), ct);
				foreach (var row in details) {
					rules[row.FunctionId] = row.IsAllowed;
				}
			}
		}
		return new MenuPermission(profileId, mode, rules);
	}

	static async Task<List<T>> QueryAsync<T>(QueryListParam param, CancellationToken ct) {
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param),
		}, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code == CvMsgErrorCode.NotFound) {
			return [];
		}
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? $"サーバエラー {reply.Code}");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? [.. list.Cast<T>()] : [];
	}
}
