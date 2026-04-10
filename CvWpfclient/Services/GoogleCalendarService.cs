using CvWpfclient.Models;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using NLog;

namespace CvWpfclient.Services;

public interface IGoogleCalendarService {
	Task<List<CalendarEventItem>> GetUpcomingEventsAsync(int maxResults = 10, CancellationToken ct = default);
	bool IsAuthenticated { get; }
}

public sealed class GoogleCalendarService(IConfiguration config) : IGoogleCalendarService, IDisposable {
	private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
	private CalendarService? _calendarService;

	public bool IsAuthenticated => _calendarService != null;

	public async Task<List<CalendarEventItem>> GetUpcomingEventsAsync(int maxResults = 10, CancellationToken ct = default) {
		try {
			EnsureInitialized();
			if (_calendarService == null) return [];

			var request = _calendarService.Events.List("primary");
			request.TimeMinDateTimeOffset = DateTimeOffset.Now;
			request.TimeMaxDateTimeOffset = DateTimeOffset.Now.AddDays(7);
			request.ShowDeleted = false;
			request.SingleEvents = true;
			request.MaxResults = maxResults;
			request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

			var events = await request.ExecuteAsync(ct);
			return ConvertEvents(events);
		}
		catch (Exception ex) {
			_logger.Warn(ex, "Googleカレンダーの取得に失敗");
			return [];
		}
	}

	private void EnsureInitialized() {
		if (_calendarService != null) return;

		var apiKey = config["Application:GoogleApiKey"];
		if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "dummy") {
			_logger.Info("Google API Key未設定のためカレンダー機能をスキップ");
			return;
		}

		_calendarService = new CalendarService(new BaseClientService.Initializer {
			ApiKey = apiKey,
			ApplicationName = "Creative Vision 10"
		});
	}

	private static List<CalendarEventItem> ConvertEvents(Events events) {
		if (events.Items == null) return [];

		var result = new List<CalendarEventItem>();
		foreach (var ev in events.Items) {
			var isAllDay = ev.Start?.DateTimeDateTimeOffset == null;
			var start = ev.Start?.DateTimeDateTimeOffset?.LocalDateTime
				?? (DateTime.TryParse(ev.Start?.Date, out var d) ? d : DateTime.MinValue);
			var end = ev.End?.DateTimeDateTimeOffset?.LocalDateTime
				?? (DateTime.TryParse(ev.End?.Date, out var ed) ? ed : start);

			result.Add(new CalendarEventItem {
				Summary = ev.Summary ?? "(無題)",
				StartTime = start,
				EndTime = end,
				Location = ev.Location ?? "",
				IsAllDay = isAllDay
			});
		}
		return result;
	}

	public void Dispose() {
		_calendarService?.Dispose();
	}
}
