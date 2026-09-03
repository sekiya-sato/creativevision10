using CvBase;
using CvServer.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Tests.CvServer;

[TestClass]
public class AutoExecMailHistoryTests {
	private const int Success = 0;

	[TestMethod]
	public void BuildAutoExecMailMessage_正常終了_件名にタスク名と正常終了を含む() {
		var history = new SysHistAutoexec {
			TaskName = "WALチェックポイント",
			StartTime = "20260901120000",
			EndTime = "20260901120005",
			ElapsedTime = 5.5,
			ReturnCode = Success,
			Count = 3,
			Memo = "正常終了",
		};

		var message = SchedulerService.BuildAutoExecMailMessage(history);

		Assert.IsTrue(message.Subject.Contains("WALチェックポイント", StringComparison.Ordinal));
		Assert.IsTrue(message.Subject.Contains("正常終了", StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildAutoExecMailMessage_異常終了_件名に異常終了を含む() {
		var history = new SysHistAutoexec {
			TaskName = "WALチェックポイント",
			StartTime = "20260901120000",
			EndTime = "20260901120005",
			ElapsedTime = 5.5,
			ReturnCode = 9,
			Count = 0,
			Memo = "例外: テストエラー",
		};

		var message = SchedulerService.BuildAutoExecMailMessage(history);

		Assert.IsTrue(message.Subject.Contains("異常終了", StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildAutoExecMailMessage_本文に各項目の値を含む() {
		var history = new SysHistAutoexec {
			TaskName = "商品名称マスタ再構築",
			StartTime = "20260901120000",
			EndTime = "20260901120010",
			ElapsedTime = 10.25,
			ReturnCode = Success,
			Count = 42,
			Memo = "商品名称マスタ再構築: 更新件数=42",
		};

		var message = SchedulerService.BuildAutoExecMailMessage(history);

		Assert.IsTrue(message.Body.Contains("商品名称マスタ再構築", StringComparison.Ordinal));
		Assert.IsTrue(message.Body.Contains("20260901120000", StringComparison.Ordinal));
		Assert.IsTrue(message.Body.Contains("20260901120010", StringComparison.Ordinal));
		Assert.IsTrue(message.Body.Contains(Success.ToString(), StringComparison.Ordinal));
		Assert.IsTrue(message.Body.Contains("42", StringComparison.Ordinal));
		Assert.IsTrue(message.Body.Contains("更新件数=42", StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildAutoExecMailMessage_historyがnull_ArgumentNullExceptionになる() {
		Assert.ThrowsExactly<ArgumentNullException>(() => SchedulerService.BuildAutoExecMailMessage(null!));
	}

	[TestMethod]
	public void BuildAppendedAutoexecMemo_短いMemoへの追記_区切りで連結される() {
		var result = SchedulerService.BuildAppendedAutoexecMemo("元のMemo", "追記文");

		Assert.AreEqual("元のMemo / 追記文", result);
	}

	[TestMethod]
	public void BuildAppendedAutoexecMemo_元Memoが長い_元Memo側を切り詰めて追記文を残す() {
		var longMemo = new string('あ', 300);
		var appendText = "追記文";

		var result = SchedulerService.BuildAppendedAutoexecMemo(longMemo, appendText);

		Assert.IsTrue(result.Length <= 250);
		Assert.IsTrue(result.EndsWith(" / " + appendText, StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildAppendedAutoexecMemo_currentMemoがnull_例外にならず追記文が入る() {
		var result = SchedulerService.BuildAppendedAutoexecMemo(null, "追記文");

		Assert.IsTrue(result.StartsWith(" / ", StringComparison.Ordinal));
		Assert.IsTrue(result.Contains("追記文", StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildAppendedAutoexecMemo_追記文が極端に長い_結果が250文字になる() {
		var appendText = new string('い', 300);

		var result = SchedulerService.BuildAppendedAutoexecMemo("元のMemo", appendText);

		Assert.AreEqual(250, result.Length);
	}
}
