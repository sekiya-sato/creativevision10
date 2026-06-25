using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

internal static class SelectDisplayConditionHelper {
	public static bool TryShowConditionDialog(
		Type itemType,
		string? baseWhere,
		string? order,
		SelectParameter? currentParameter,
		object ownerViewModel,
		string displayName,
		out SelectParameter parameter,
		out string? conditionWhere,
		out int? maxCount) {
		return itemType == typeof(MasterShohin)
			? TryShowShohinConditionDialog(currentParameter, ownerViewModel, displayName, out parameter, out conditionWhere, out maxCount)
			: TryShowRangeConditionDialog(itemType, baseWhere, order, currentParameter, ownerViewModel, displayName, out parameter, out conditionWhere, out maxCount);
	}

	public static string? CombineWhere(string? baseWhere, string? conditionWhere) {
		string? normalizedBase = NormalizeNullableText(baseWhere);
		string? normalizedCondition = NormalizeNullableText(conditionWhere);
		return (normalizedBase, normalizedCondition) switch {
			(null, null) => null,
			({ } left, null) => left,
			(null, { } right) => right,
			({ } left, { } right) => $"({left}) AND ({right})"
		};
	}

	public static string GetDisplayName(Type itemType, string title) {
		string normalizedTitle = title.Replace("画面", string.Empty, StringComparison.Ordinal)
			.Replace("選択", string.Empty, StringComparison.Ordinal)
			.Trim();
		if (!string.IsNullOrWhiteSpace(normalizedTitle)) return normalizedTitle;

		if (itemType == typeof(MasterShohin)) return "商品";
		return itemType.Name.StartsWith("Master", StringComparison.Ordinal)
			? itemType.Name["Master".Length..]
			: itemType.Name;
	}

	static bool TryShowRangeConditionDialog(
		Type itemType,
		string? baseWhere,
		string? order,
		SelectParameter? currentParameter,
		object ownerViewModel,
		string displayName,
		out SelectParameter parameter,
		out string? conditionWhere,
		out int? maxCount) {
		var selWin = new Views.Sub.RangeParamView();
		if (selWin.DataContext is not RangeParamViewModel vm) {
			parameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { DisplayName = displayName }, displayName);
			conditionWhere = BuildGenericWhere(parameter);
			maxCount = parameter.MaxCount;
			return true;
		}

		var initialParameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { DisplayName = displayName, MaxCount = AppGlobal.Application.Limit }, displayName);
		vm.Initialize(initialParameter, itemType, baseWhere ?? string.Empty, order ?? "Code");
		if (ClientLib.ShowDialogView(selWin, ownerViewModel, true) != true) {
			parameter = initialParameter;
			conditionWhere = BuildGenericWhere(parameter);
			maxCount = parameter.MaxCount;
			return false;
		}

		parameter = NormalizeSelectParameter(vm.Parameter, displayName);
		conditionWhere = BuildGenericWhere(parameter);
		maxCount = parameter.MaxCount;
		return true;
	}

	static bool TryShowShohinConditionDialog(
		SelectParameter? currentParameter,
		object ownerViewModel,
		string displayName,
		out SelectParameter parameter,
		out string? conditionWhere,
		out int? maxCount) {
		var selWin = new Views.Sub.SelectShohinView();
		if (selWin.DataContext is not SelectShohinViewModel vm) {
			parameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { DisplayName = displayName, IdsDisplayName = "ブランド" }, displayName);
			conditionWhere = BuildShohinWhere(parameter);
			maxCount = parameter.MaxCount;
			return true;
		}

		var initialParameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { DisplayName = displayName, IdsDisplayName = "ブランド", MaxCount = AppGlobal.Application.Limit }, displayName) with {
			IdsDisplayName = "ブランド"
		};
		vm.IsConditionOnlyMode = true;
		vm.ApplySelectParameter(initialParameter);
		if (ClientLib.ShowDialogView(selWin, ownerViewModel, true) != true) {
			parameter = initialParameter;
			conditionWhere = BuildShohinWhere(parameter);
			maxCount = parameter.MaxCount;
			return false;
		}

		parameter = NormalizeSelectParameter(vm.CreateSelectParameter(displayName), displayName) with {
			IdsDisplayName = "ブランド"
		};
		conditionWhere = BuildShohinWhere(parameter);
		maxCount = parameter.MaxCount;
		return true;
	}

	static SelectParameter NormalizeSelectParameter(SelectParameter parameter, string displayName) =>
		new() {
			FromId = parameter.FromId,
			ToId = parameter.ToId,
			Ids = NormalizeSelectedIds(parameter.Ids),
			IdsText = NormalizeSelectedIdsText(parameter.Ids, parameter.IdsText),
			IdsDisplayName = NormalizeNullableText(parameter.IdsDisplayName) ?? displayName,
			IsToriVisible = parameter.IsToriVisible,
			ToriLabel = string.IsNullOrWhiteSpace(parameter.ToriLabel) ? "取引先Id" : parameter.ToriLabel,
			ToriSearchWhere = NormalizeNullableText(parameter.ToriSearchWhere),
			ToriIds = NormalizeSelectedIds(parameter.ToriIds),
			ToriIdsText = NormalizeSelectedIdsText(parameter.ToriIds, parameter.ToriIdsText),
			AdditionalIds1Label = string.IsNullOrWhiteSpace(parameter.AdditionalIds1Label) ? "複数Id 1" : parameter.AdditionalIds1Label,
			AdditionalIds1Column = NormalizeNullableText(parameter.AdditionalIds1Column),
			AdditionalIds1 = NormalizeSelectedIds(parameter.AdditionalIds1),
			AdditionalIds1Text = NormalizeSelectedIdsText(parameter.AdditionalIds1, parameter.AdditionalIds1Text),
			AdditionalIds2Label = string.IsNullOrWhiteSpace(parameter.AdditionalIds2Label) ? "複数Id 2" : parameter.AdditionalIds2Label,
			AdditionalIds2Column = NormalizeNullableText(parameter.AdditionalIds2Column),
			AdditionalIds2 = NormalizeSelectedIds(parameter.AdditionalIds2),
			AdditionalIds2Text = NormalizeSelectedIdsText(parameter.AdditionalIds2, parameter.AdditionalIds2Text),
			ItemIds = NormalizeSelectedIds(parameter.ItemIds),
			ItemIdsText = NormalizeSelectedIdsText(parameter.ItemIds, parameter.ItemIdsText),
			FromCode = NormalizeNullableText(parameter.FromCode),
			ToCode = NormalizeNullableText(parameter.ToCode),
			DisplayName = NormalizeNullableText(parameter.DisplayName) ?? displayName,
			Name = NormalizeNullableText(parameter.Name),
			Jan = NormalizeNullableText(parameter.Jan),
			MaxCount = parameter.MaxCount
		};

	static string? BuildGenericWhere(SelectParameter parameter) {
		List<string> clauses = [];
		AddSelectedIdInClause(clauses, "Id", parameter.Ids);
		AddIdRange(clauses, "Id", parameter.FromId, parameter.ToId);
		AddCodeRange(clauses, "Code", parameter.FromCode, parameter.ToCode);
		AddLike(clauses, "Name", parameter.Name);
		AddOptionalSelectedIdInClause(clauses, parameter.AdditionalIds1Column, parameter.AdditionalIds1);
		AddOptionalSelectedIdInClause(clauses, parameter.AdditionalIds2Column, parameter.AdditionalIds2);
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static string? BuildShohinWhere(SelectParameter parameter) {
		List<string> clauses = [];
		AddSelectedIdInClause(clauses, "Id_Brand", parameter.Ids);
		AddSelectedIdInClause(clauses, "Id_Item", parameter.ItemIds);
		AddIdRange(clauses, "Id", parameter.FromId, parameter.ToId);
		AddCodeRange(clauses, "Code", parameter.FromCode, parameter.ToCode);
		AddLike(clauses, "Name", parameter.Name);
		if (!string.IsNullOrWhiteSpace(parameter.Jan)) {
			string jan = EscapeSqlLiteral(parameter.Jan.Trim());
			clauses.Add($"""
				EXISTS (
					SELECT 1
					FROM DerivedShohinColSiz D
					WHERE D.Id_Shohin = MasterShohin.Id
						AND (D.Jan1 LIKE '%{jan}%' OR D.Jan2 LIKE '%{jan}%' OR D.Jan3 LIKE '%{jan}%')
				)
				""");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static void AddIdRange(List<string> clauses, string column, long? from, long? to) {
		if (from.HasValue) {
			clauses.Add($"{column} >= {from.Value.ToString(CultureInfo.InvariantCulture)}");
		}
		if (to.HasValue) {
			clauses.Add($"{column} <= {to.Value.ToString(CultureInfo.InvariantCulture)}");
		}
	}

	static void AddCodeRange(List<string> clauses, string column, string? from, string? to) {
		if (!string.IsNullOrWhiteSpace(from)) {
			clauses.Add($"{column} >= '{EscapeSqlLiteral(from.Trim())}'");
		}
		if (!string.IsNullOrWhiteSpace(to)) {
			clauses.Add($"{column} <= '{EscapeSqlLiteral(to.Trim())}'");
		}
	}

	static void AddLike(List<string> clauses, string column, string? value) {
		if (string.IsNullOrWhiteSpace(value)) return;
		clauses.Add($"{column} LIKE '%{EscapeSqlLiteral(value.Trim())}%'");
	}

	static void AddSelectedIdInClause(List<string> clauses, string column, IEnumerable<long>? ids) {
		string[] values = NormalizeSelectedIds(ids)
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray();
		if (values.Length == 0) return;

		clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	static void AddOptionalSelectedIdInClause(List<string> clauses, string? column, IEnumerable<long>? ids) {
		if (string.IsNullOrWhiteSpace(column)) return;
		AddSelectedIdInClause(clauses, column, ids);
	}

	static List<long> NormalizeSelectedIds(IEnumerable<long>? ids) =>
		ids?.Where(id => id > 0).Distinct().ToList() ?? [];

	static string NormalizeSelectedIdsText(IEnumerable<long>? ids, string? text) {
		int count = NormalizeSelectedIds(ids).Count;
		if (count == 0) return "未選択";
		return string.IsNullOrWhiteSpace(text) || text == "未選択" ? $"{count}件" : text;
	}

	static string? NormalizeNullableText(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
