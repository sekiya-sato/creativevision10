using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using NCrontab;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysSchedulerCronEditViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string taskName = string.Empty;

	[ObservableProperty]
	string cronExpression = string.Empty;

	[ObservableProperty]
	string previewNextOccurrence = string.Empty;

	[ObservableProperty]
	string validationMessage = string.Empty;

	[ObservableProperty]
	bool isValid;

	partial void OnCronExpressionChanged(string value) => ValidateAndPreview();

	private void ValidateAndPreview() {
		ValidationMessage = string.Empty;
		PreviewNextOccurrence = string.Empty;
		IsValid = false;

		if (string.IsNullOrWhiteSpace(CronExpression)) {
			ValidationMessage = "Cron式を入力してください";
			return;
		}

		try {
			var schedule = CrontabSchedule.Parse(CronExpression);
			var next = schedule.GetNextOccurrence(DateTime.Now);
			PreviewNextOccurrence = $"次回実行: {next:yyyy/MM/dd HH:mm:ss}";
			IsValid = true;
		}
		catch (Exception ex) {
			ValidationMessage = $"Cron式が不正です: {ex.Message}";
		}
	}

	[RelayCommand]
	public void ApplyPreset(string preset) {
		CronExpression = preset;
	}

	[RelayCommand]
	public void Confirm() {
		ValidateAndPreview();
		if (!IsValid) {
			MessageEx.ShowWarningDialog(ValidationMessage, owner: Helpers.ClientLib.GetActiveView(this));
			return;
		}
		ExitWithResultTrue();
	}
}
