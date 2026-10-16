using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Calendar")]
    public class CalendarController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly JsonSerializerOptions _jsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };

        public CalendarController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataDirectory =>
            Path.Combine(
                _environment.WebRootPath,
                "data",
                "Calendar");

        private string CalendarsPath =>
            Path.Combine(DataDirectory, "calendars.json");

        private string DefinitionsPath =>
            Path.Combine(DataDirectory, "definitions.json");

        private string EventsPath =>
            Path.Combine(DataDirectory, "calendar.json");

        private async Task<List<T>> ReadJsonAsync<T>(string path)
        {
            if (!System.IO.File.Exists(path))
                return new List<T>();

            var json = await System.IO.File.ReadAllTextAsync(path);

            return JsonSerializer.Deserialize<List<T>>(
                json, _jsonOptions) ?? new List<T>();
        }

        private async Task WriteJsonAsync<T>(
            string path, List<T> data)
        {
            Directory.CreateDirectory(DataDirectory);

            var json = JsonSerializer.Serialize(data, _jsonOptions);

            await System.IO.File.WriteAllTextAsync(path, json);
        }

        [HttpGet("Calendars")]
        public async Task<IActionResult> GetCalendars()
        {
            return Json(await ReadJsonAsync<CalendarConfig>(
                CalendarsPath));
        }

        [HttpPost("Calendars")]
        public async Task<IActionResult> SaveCalendars(
            [FromBody] List<CalendarConfig> calendars)
        {
            if (calendars == null)
                return BadRequest("Calendar data is required.");

            if (calendars.Any(c =>
                string.IsNullOrWhiteSpace(c.Id) ||
                string.IsNullOrWhiteSpace(c.Code) ||
                string.IsNullOrWhiteSpace(c.Name)))
            {
                return BadRequest(
                    "Every calendar needs an ID, code and name.");
            }

            if (calendars.GroupBy(c => c.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            {
                return BadRequest("Calendar IDs must be unique.");
            }

            if (calendars.GroupBy(c => c.Code,
                    StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            {
                return BadRequest("Calendar codes must be unique.");
            }

            if (calendars.Any(c =>
                c.WeeklyOffDays.Any(d => d < 0 || d > 6)))
            {
                return BadRequest(
                    "Weekdays must be between 0 and 6.");
            }

            await WriteJsonAsync(CalendarsPath, calendars);
            return Ok(new { message = "Calendars saved." });
        }

        [HttpGet("Definitions")]
        public async Task<IActionResult> GetDefinitions()
        {
            return Json(await ReadJsonAsync<CalendarDefinition>(
                DefinitionsPath));
        }

        [HttpPost("Definitions")]
        public async Task<IActionResult> SaveDefinitions(
            [FromBody] List<CalendarDefinition> definitions)
        {
            if (definitions == null)
                return BadRequest("Definitions are required.");

            var calendars =
                await ReadJsonAsync<CalendarConfig>(CalendarsPath);

            foreach (var item in definitions)
            {
                if (!calendars.Any(c => c.Id == item.CalendarId))
                    return BadRequest(
                        $"Unknown calendar: {item.CalendarId}");

                if (string.IsNullOrWhiteSpace(item.DocNo))
                    return BadRequest("Doc No is required.");

                if (item.Type != "Holiday" &&
                    item.Type != "WeeklyOff")
                {
                    return BadRequest(
                        "Type must be Holiday or WeeklyOff.");
                }

                if (!DateOnly.TryParse(item.FromDate, out var from) ||
                    !DateOnly.TryParse(item.ToDate, out var to) ||
                    from > to)
                {
                    return BadRequest("Invalid date range.");
                }

                if (item.Weekdays.Any(d => d < 0 || d > 6))
                    return BadRequest("Invalid weekday.");
            }

            await WriteJsonAsync(DefinitionsPath, definitions);
            return Ok(new { message = "Definitions saved." });
        }

        [HttpGet("Events")]
        public async Task<IActionResult> GetEvents()
        {
            return Json(await ReadJsonAsync<CalendarEvent>(
                EventsPath));
        }

        [HttpPost("Events")]
        public async Task<IActionResult> SaveEvents(
            [FromBody] List<CalendarEvent> events)
        {
            if (events == null)
                return BadRequest("Event data is required.");

            var calendars =
                await ReadJsonAsync<CalendarConfig>(CalendarsPath);

            foreach (var item in events)
            {
                if (string.IsNullOrWhiteSpace(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Title))
                    return BadRequest("Event ID and title are required.");

                if (!calendars.Any(c => c.Id == item.CalendarId))
                    return BadRequest(
                        $"Unknown calendar: {item.CalendarId}");

                if (item.Type != "leave" &&
                    item.Type != "holiday")
                    return BadRequest(
                        "Event type must be leave or holiday.");

                if (!DateOnly.TryParse(item.Start, out var start) ||
                    !DateOnly.TryParse(item.End, out var end) ||
                    start > end)
                    return BadRequest("Invalid event date range.");

                if (item.Type == "leave" &&
                    string.IsNullOrWhiteSpace(item.Employee))
                    return BadRequest(
                        "Employee is required for leave.");
            }

            await WriteJsonAsync(EventsPath, events);
            return Ok(new { message = "Events saved." });
        }
    }

    public class CalendarConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Country { get; set; } = "";
        public List<int> WeeklyOffDays { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public class CalendarDefinition
    {
        public string Id { get; set; } = "";
        public string CalendarId { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string Date { get; set; } = "";
        public string Type { get; set; } = "Holiday";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public List<int> Weekdays { get; set; } = new();
        public string HolidayName { get; set; } = "";
    }

    public class CalendarEvent
    {
        public string Id { get; set; } = "";
        public string CalendarId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "leave";
        public string Employee { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public string Reason { get; set; } = "";
    }
}