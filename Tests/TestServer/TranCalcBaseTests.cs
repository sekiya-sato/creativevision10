using CvBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="TranCalcBase.GetKubunCalcFlag"/> の符号判定を固定する。
/// 社販区分(14=UriShahan/24=HenShahan)を追加した際、20〜39を返品・値引とする既存規則
/// (<see cref="EnumUri01.HenShahan"/>=24 は範囲内、<see cref="EnumUri01.UriShahan"/>=14 は範囲外)が
/// そのまま社販にも正しく効くことを確認する。
/// </summary>
[TestClass]
public class TranCalcBaseTests {
	[TestMethod]
	[DataRow(14, 1, DisplayName = "社販売上(UriShahan)は+1")]
	[DataRow(24, -1, DisplayName = "社販返品(HenShahan)は-1")]
	[DataRow(99, 1, DisplayName = "税区分は+1")]
	[DataRow(15, 1, DisplayName = "消化仕入(SoldOnShiire)は+1")]
	[DataRow(25, -1, DisplayName = "消化仕入返品(SoldOnHenpin)は-1")]
	public void GetKubunCalcFlag_ReturnsSignByKubunRange(int kubun, int expected) {
		Assert.AreEqual(expected, TranCalcBase.GetKubunCalcFlag(kubun));
	}
}
