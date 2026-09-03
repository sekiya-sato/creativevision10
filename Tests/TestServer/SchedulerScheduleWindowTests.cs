using CvServer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NCrontab;
using System;

namespace Tests.CvServer;

[TestClass]
public class SchedulerScheduleWindowTests {
	private static readonly TimeSpan Late = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan Early = TimeSpan.FromSeconds(1);

	[TestMethod]
	public void IsWithinScheduleWindow_予定時刻ちょうど_true() {
		var schedule = CrontabSchedule.Parse("0 2 * * *");
		var now = new DateTime(2026, 9, 3, 2, 0, 0);

		Assert.IsTrue(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_遅れ猶予30秒以内_true() {
		var schedule = CrontabSchedule.Parse("0 2 * * *");
		var now = new DateTime(2026, 9, 3, 2, 0, 20);

		Assert.IsTrue(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_遅れ猶予超過_false() {
		var schedule = CrontabSchedule.Parse("0 2 * * *");
		var now = new DateTime(2026, 9, 3, 2, 0, 45);

		Assert.IsFalse(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_タイマ誤差で予定より早い_true() {
		var schedule = CrontabSchedule.Parse("0 2 * * *");
		var now = new DateTime(2026, 9, 3, 1, 59, 59, 500);

		Assert.IsTrue(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_深夜2時cronが15時41分に起動した不正実行の再現_false() {
		var schedule = CrontabSchedule.Parse("0 2 * * *");
		var now = new DateTime(2026, 9, 3, 15, 41, 25);

		Assert.IsFalse(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_0時12時cronが14時31分に起動した不正実行の再現_false() {
		var schedule = CrontabSchedule.Parse("30 0,12 * * *");
		var now = new DateTime(2026, 9, 3, 14, 31, 8);

		Assert.IsFalse(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_0時12時cronの正規の起動_true() {
		var schedule = CrontabSchedule.Parse("30 0,12 * * *");
		var now = new DateTime(2026, 9, 3, 12, 30, 0);

		Assert.IsTrue(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_毎分実行cronは遅れ猶予内の秒なら予定内_true() {
		// 毎分cronは分境界(秒=00)ちょうどに発生する。遅れ猶予(30秒)以内の秒であれば
		// 直前の分境界の発生が窓に入るため予定内と判定される。
		var schedule = CrontabSchedule.Parse("* * * * *");
		var now = new DateTime(2026, 9, 3, 10, 15, 29);

		Assert.IsTrue(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}

	[TestMethod]
	public void IsWithinScheduleWindow_毎分実行cronでも遅れ猶予を超えた秒は予定外_false() {
		// 毎分cronであっても、分境界(秒=00)から遅れ猶予(30秒)を超えて経過した時刻は
		// 直前・次の発生のどちらも窓に入らないため予定外と判定される。
		var schedule = CrontabSchedule.Parse("* * * * *");
		var now = new DateTime(2026, 9, 3, 10, 15, 45);

		Assert.IsFalse(SchedulerService.IsWithinScheduleWindow(schedule, now, Late, Early));
	}
}
