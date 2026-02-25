using System;
using System.Collections.Generic;
using System.Text;

namespace Cvnet10Base.Share;

/// <summary>
/// 性別 [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
public enum EnumGenger:int {
	Unknown = 0,
	Woman = 1,
	Man = 2
}

/// <summary>
/// する,しない [property: ColumnSizeDml(ctype:ColumnType.Enum)]
/// </summary>
public enum EnumYesNo:int {
	No = 0,
	Yes = 1
}
/// <summary>
/// 締め日
/// </summary>
public enum EnumShime:int {
	Day01 = 1,
	Day02 = 2,
	Day03 = 3,
	Day04 = 4,
	Day05 = 5,
	Day06 = 6,
	Day07 = 7,
	Day08 = 8,
	Day09 = 9,
	Day10 = 10,
	Day11 = 11,
	Day12 = 12,
	Day13 = 13,
	Day14 = 14,
	Day15 = 15,
	Day16 = 16,
	Day17 = 17,
	Day18 = 18,
	Day19 = 19,
	Day20 = 20,
	Day21 = 21,
	Day22 = 22,
	Day23 = 23,
	Day24 = 24,
	Day25 = 25,
	Day26 = 26,
	Day27 = 27,
	Day28 = 28,
	DayLast = 99
}

/// <summary>
/// 得意先種別
/// </summary>
public enum EnumTokui:int {
	/// <summary>
	/// 倉庫
	/// </summary>
	_0_Soko = 0,
	/// <summary>
	/// 卸先
	/// </summary>
	_1_Oroshi = 1,
	/// <summary>
	/// 売仕店
	/// </summary>
	_3_UriShi = 3,
	/// <summary>
	/// 直営店
	/// </summary>
	_6_Tenpo = 6,
}
