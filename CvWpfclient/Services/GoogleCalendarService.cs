using CvWpfclient.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using NLog;
using System.IO;

namespace CvWpfclient.Services;

public interface IGoogleCalendarService {
	Task<List<CalendarEventItem>> GetUpcomingEventsAsync(int maxResults = 10, CancellationToken ct = default);
	bool IsAuthenticated { get; }
}

public sealed class GoogleCalendarService(IConfiguration config) : IGoogleCalendarService, IDisposable {
	private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
	private static readonly string[] Scopes = [CalendarService.Scope.CalendarReadonly];
	private CalendarService? _calendarService;

	public bool IsAuthenticated => _calendarService != null;

	public async Task<List<CalendarEventItem>> GetUpcomingEventsAsync(int maxResults = 10, CancellationToken ct = default) {
		try {
			await EnsureAuthenticatedAsync(ct);
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

	private async Task EnsureAuthenticatedAsync(CancellationToken ct) {
		if (_calendarService != null) return;

		var clientId = config["Application:GoogleOAuthId"];
		var clientSecret = config["Application:GoogleOAuthSecret"];
		if (string.IsNullOrWhiteSpace(clientId) || clientId == "dummy") {
			_logger.Info("Google OAuth未設定のためカレンダー機能をスキップ");
			return;
		}

		var tokenDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"CreativeVision10", "google-tokens");

		var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
			new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret ?? "" },
			Scopes,
			"user",
			ct,
			new FileDataStore(tokenDir, true));

		_calendarService = new CalendarService(new BaseClientService.Initializer {
			HttpClientInitializer = credential,
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
