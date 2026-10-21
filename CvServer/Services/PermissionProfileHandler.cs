using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvServer.Services;

/// <summary>
/// 権限プロファイル設定（Doc/spec/2026-10-20_メニュー実行権限と個人メニュー_最小変更設計案.md §6.3）のサーバ処理。
/// </summary>
public partial class CoreService {

	/// <summary>
	/// 権限プロファイルの保存。プロファイル行の追加・更新と、メニュー実行(View)明細の洗い替えを1トランザクションで行う。
	/// <para>
	/// 書き込む前に入力を検査し、違反は <see cref="CvMsgErrorCode.InvalidParameter"/>、
	/// 行が無い・Vdu不一致は <see cref="CvMsgErrorCode.ConcurrentUpdate"/> で何も書かずに返す。
	/// FullAccess は明細を変えず、無効にもしない（ロックアウト防止）。Edit(2) の明細には触れない。
	/// </para>
	/// </summary>
	private CvMsg HandlePermissionProfileSave(CvFlag flag, PermissionProfileSaveParam save) {
		var profile = save.Profile;
		var rows = save.ViewRows ?? [];
		_logger.LogInformation("パラメータ PermissionProfileSaveParam Id={Id} Code={Code} 明細={Count}", profile?.Id, profile?.Code, rows.Length);
		if (ValidatePermissionProfileSave(profile, rows) is string invalid) {
			return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, invalid, typeof(string), invalid);
		}
		profile!.Code = profile.Code.Trim();
		profile.Name = profile.Name.Trim();
		var isFullAccess = profile.Id == SysPermissionProfile.FullAccessId;
		if (isFullAccess) {
			profile.IsActive = true;
		}
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			if (_db.Fetch<SysPermissionProfile>("where Code=@0 and Id<>@1", profile.Code, profile.Id).Count > 0) {
				_db.AbortTransaction();
				var duplicated = $"コード「{profile.Code}」は他のプロファイルで使われています。";
				return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, duplicated, typeof(string), duplicated);
			}
			var vdate = Common.GetVdate();
			if (profile.Id == 0) {
				profile.Vdc = vdate;
				profile.Vdu = vdate;
				_db.Insert(profile);
			}
			else {
				if (_db.Fetch<SysPermissionProfile>("where Id=@0", profile.Id).FirstOrDefault() is not SysPermissionProfile org || org.Vdu != profile.Vdu) {
					_db.AbortTransaction();
					_logger.LogInformation("権限プロファイル保存 競合検知 Id={Id} ExpectedVdu={ExpectedVdu}", profile.Id, profile.Vdu);
					return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, typeof(string), $"Id={profile.Id}");
				}
				profile.Vdc = org.Vdc;
				profile.Vdu = vdate;
				_db.Update(profile);
			}
			int deleted = 0, inserted = 0;
			if (!isFullAccess) {
				var current = _db.Fetch<SysPermissionProfileDetail>("where Id_PermissionProfile=@0 and PermissionType=@1",
					profile.Id, (int)EnumPermissionType.View);
				foreach (var old in current) {
					_db.Delete(old);
				}
				deleted = current.Count;
				foreach (var row in rows) {
					_db.Insert(new SysPermissionProfileDetail {
						Id_PermissionProfile = profile.Id,
						FunctionId = row.FunctionId.Trim(),
						PermissionType = (int)EnumPermissionType.View,
						IsAllowed = row.IsAllowed,
						Vdc = vdate,
						Vdu = vdate,
					});
				}
				inserted = rows.Length;
			}
			_db.CompleteTransaction();
			_logger.LogInformation("権限プロファイル保存 Id={Id} 明細削除={Deleted} 明細登録={Inserted}", profile.Id, deleted, inserted);
			return CreateSuccessResponse(flag, typeof(SysPermissionProfile), Common.SerializeObject(profile));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <returns>違反があればメッセージ、無ければ null</returns>
	private static string? ValidatePermissionProfileSave(SysPermissionProfile? profile, SysPermissionProfileDetail[] rows) {
		if (profile == null) {
			return "プロファイルが指定されていません。";
		}
		if (string.IsNullOrWhiteSpace(profile.Code) || profile.Code.Trim().Length > 30) {
			return "コードは1～30文字で入力してください。";
		}
		if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 60) {
			return "名称は1～60文字で入力してください。";
		}
		if (rows.Any(r => r == null)) {
			return "明細が不正です。";
		}
		profile.Memo ??= string.Empty;
		if (profile.Memo.Length > 120) {
			return "説明は120文字以内で入力してください。";
		}
		if (profile.Id == SysPermissionProfile.FullAccessId && rows.Length > 0) {
			return "全機能標準権限(FullAccess)のメニュー権限は変更できません。";
		}
		if (rows.Any(r => string.IsNullOrWhiteSpace(r.FunctionId) || r.FunctionId.Trim().Length > 60)) {
			return "機能Idが不正な明細があります。";
		}
		if (rows.Select(r => r.FunctionId.Trim()).Distinct(StringComparer.Ordinal).Count() != rows.Length) {
			return "同じ機能Idの明細が重複しています。";
		}
		return null;
	}
}
