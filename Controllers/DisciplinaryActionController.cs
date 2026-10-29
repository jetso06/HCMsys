
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System;

namespace HCMSys.Controllers
{
    [Route("Master/DisciplinaryAction")]
    public class DisciplinaryActionController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public DisciplinaryActionController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataPath(params string[] parts)
        {
            return Path.Combine(
                new[] { _environment.WebRootPath, "data" }
                    .Concat(parts)
                    .ToArray());
        }

        private string ConfigPath =>
            DataPath("DisciplinaryAction", "disciplinary-action-config.json");

        private string RecordsPath =>
            DataPath("DisciplinaryAction", "disciplinary-actions.json");

        private string EmployeesPath =>
            DataPath("PatternAssignment", "employees.json");

        private DisciplinaryActionConfig ReadConfig()
        {
            if (!System.IO.File.Exists(ConfigPath))
                throw new FileNotFoundException(
                    "Disciplinary Action configuration JSON was not found.",
                    ConfigPath);

            var json = System.IO.File.ReadAllText(ConfigPath);

            return JsonSerializer.Deserialize<DisciplinaryActionConfig>(
                json, _jsonOptions) ?? new DisciplinaryActionConfig();
        }

        private DisciplinaryActionStore ReadRecords()
        {
            if (!System.IO.File.Exists(RecordsPath))
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(RecordsPath)!);

                System.IO.File.WriteAllText(
                    RecordsPath,
                    JsonSerializer.Serialize(
                        new DisciplinaryActionStore(), _jsonOptions));
            }

            var json = System.IO.File.ReadAllText(RecordsPath);

            return JsonSerializer.Deserialize<DisciplinaryActionStore>(
                json, _jsonOptions) ?? new DisciplinaryActionStore();
        }

        private void WriteRecords(DisciplinaryActionStore store)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(RecordsPath)!);

            var json = JsonSerializer.Serialize(store, _jsonOptions);

            System.IO.File.WriteAllText(RecordsPath, json);
        }

        private List<DisciplinaryActionEmployee> ReadEmployees()
        {
            if (!System.IO.File.Exists(EmployeesPath))
                return new List<DisciplinaryActionEmployee>();

            var json = System.IO.File.ReadAllText(EmployeesPath);

            // Supports either a JSON array or an object containing "employees".
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<DisciplinaryActionEmployee>>(
                    json, _jsonOptions) ?? new List<DisciplinaryActionEmployee>();
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(
                    "employees", out var employeesElement))
            {
                return JsonSerializer.Deserialize<List<DisciplinaryActionEmployee>>(
                    employeesElement.GetRawText(), _jsonOptions)
                    ?? new List<DisciplinaryActionEmployee>();
            }

            return new List<DisciplinaryActionEmployee>();
        }

        // Entry page: /Master/DisciplinaryAction
        [HttpGet("")]
        public IActionResult Index(string? documentNo = null)
        {
            try
            {
                var model = new DisciplinaryActionViewModel
                {
                    Config = ReadConfig(),
                    Employees = ReadEmployees(),
                    Form = new DisciplinaryActionInput
                    {
                        Date = DateTime.Today,
                        HrStatus = "New"
                    }
                };

                if (!string.IsNullOrWhiteSpace(documentNo))
                {
                    var record = ReadRecords().Records.FirstOrDefault(
                        r => string.Equals(
                            r.DocumentNo,
                            documentNo,
                            StringComparison.OrdinalIgnoreCase));

                    if (record == null)
                        return NotFound("Disciplinary Action record was not found.");

                    model.Form = new DisciplinaryActionInput
                    {
                        DocumentNo = record.DocumentNo,
                        Date = record.Date,
                        PostDate = record.PostDate,
                        EmployeeId = record.EmployeeId,
                        Designation = record.Designation,
                        Department = record.Department,
                        ReportingManager = record.ReportingManager,
                        DiscpActionType = record.DiscpActionType,
                        IncidentDateTime = record.IncidentDateTime,
                        IncidentLocation = record.IncidentLocation,
                        Comments = record.Comments,
                        HrStatus = record.HrStatus,
                        WarningType = record.WarningType,
                        WarningAction = record.WarningAction,
                        WarningComments = record.WarningComments
                    };
                }

                return View("~/Views/Master/DisciplinaryAction.cshtml", model);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to load Disciplinary Action");
            }
        }

        // Index/grid page: /Master/DisciplinaryActionIndex
        [HttpGet("/Master/DisciplinaryActionIndex")]
        public IActionResult DisciplinaryActionIndex()
        {
            try
            {
                var records = ReadRecords().Records
                    .OrderByDescending(r => r.Date)
                    .ThenByDescending(r => r.DocumentNo)
                    .ToList();

                return View(
                    "~/Views/Master/DisciplinaryActionIndex.cshtml",
                    records);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to load Disciplinary Action records");
            }
        }

        // Save a new record or update an existing record.
        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(DisciplinaryActionViewModel model)
        {
            try
            {
                model.Config = ReadConfig();
                model.Employees = ReadEmployees();

                if (!ModelState.IsValid)
                {
                    return View(
                        "~/Views/Master/DisciplinaryAction.cshtml",
                        model);
                }

                var input = model.Form;
                var store = ReadRecords();

                var existing = store.Records.FirstOrDefault(
                    r => string.Equals(
                        r.DocumentNo,
                        input.DocumentNo,
                        StringComparison.OrdinalIgnoreCase));

                var employee = model.Employees.FirstOrDefault(
                    e => string.Equals(
                        e.Id,
                        input.EmployeeId,
                        StringComparison.OrdinalIgnoreCase));

                // Use employee data for department when available.
                if (employee != null &&
                    !string.IsNullOrWhiteSpace(employee.Department))
                {
                    input.Department = employee.Department;
                }

                var record = new DisciplinaryActionRecord
                {
                    DocumentNo = input.DocumentNo.Trim(),
                    Date = input.Date,
                    PostDate = input.PostDate,
                    EmployeeId = input.EmployeeId,
                    EmployeeCode = employee?.Code ?? "",
                    EmployeeName = employee?.Name ?? "",
                    Designation = input.Designation,
                    Department = input.Department,
                    ReportingManager = input.ReportingManager,
                    DiscpActionType = input.DiscpActionType,
                    IncidentDateTime = input.IncidentDateTime,
                    IncidentLocation = input.IncidentLocation,
                    Comments = input.Comments,
                    HrStatus = input.HrStatus,
                    WarningType = input.WarningType,
                    WarningAction = input.WarningAction,
                    WarningComments = input.WarningComments
                };

                if (existing == null)
                {
                    store.Records.Add(record);
                }
                else
                {
                    store.Records.Remove(existing);
                    store.Records.Add(record);
                }

                WriteRecords(store);

                TempData["SuccessMessage"] =
                    existing == null
                        ? "Disciplinary Action saved successfully."
                        : "Disciplinary Action updated successfully.";

                return RedirectToAction(nameof(DisciplinaryActionIndex));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to save the record: " + ex.Message);

                try
                {
                    model.Config = ReadConfig();
                    model.Employees = ReadEmployees();
                }
                catch
                {
                    // Preserve the original save error.
                }

                return View(
                    "~/Views/Master/DisciplinaryAction.cshtml",
                    model);
            }
        }

        // Delete a record from the JSON file.
        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string documentNo)
        {
            if (string.IsNullOrWhiteSpace(documentNo))
                return BadRequest("Document number is required.");

            var store = ReadRecords();

            var record = store.Records.FirstOrDefault(
                r => string.Equals(
                    r.DocumentNo,
                    documentNo,
                    StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                TempData["ErrorMessage"] =
                    "Disciplinary Action record was not found.";
            }
            else
            {
                store.Records.Remove(record);
                WriteRecords(store);

                TempData["SuccessMessage"] =
                    "Disciplinary Action deleted successfully.";
            }

            return RedirectToAction(nameof(DisciplinaryActionIndex));
        }
    }

    // Configuration models
    public class DisciplinaryActionConfig
    {
        public int ModuleId { get; set; } = 502;
        public string ModuleName { get; set; } = "Disciplinary Action Master";

        public DisciplinaryActionOptions Options { get; set; } = new();
        public DisciplinaryActionValidation Validation { get; set; } = new();
    }

    public class DisciplinaryActionOptions
    {
        public List<string> HrStatus { get; set; } =
            new() { "New", "Open", "Closed" };

        public List<string> WarningType { get; set; } =
            new() { "Verbal", "Written" };

        public List<string> WarningAction { get; set; } =
            new()
            {
                "Counselling",
                "Warning Letter & Memo",
                "Suspension",
                "Termination"
            };
    }

    public class DisciplinaryActionValidation
    {
        public int DocumentNoMaxLength { get; set; } = 60;
        public int DesignationMaxLength { get; set; } = 60;
        public int DepartmentMaxLength { get; set; } = 60;
        public int ReportingManagerMaxLength { get; set; } = 60;
        public int DisciplinaryActionTypeMaxLength { get; set; } = 60;
        public int IncidentLocationMaxLength { get; set; } = 60;
        public int CommentsMaxLength { get; set; } = 200;
        public int WarningCommentsMaxLength { get; set; } = 200;
    }

    // Employee JSON model
    public class DisciplinaryActionEmployee
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string Id { get; set; } = "";

        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("department")]
        public string Department { get; set; } = "";

        [JsonPropertyName("company")]
        public string Company { get; set; } = "";

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;
    }

    // Entry-page view model
    public class DisciplinaryActionViewModel
    {
        public DisciplinaryActionConfig Config { get; set; } = new();
        public List<DisciplinaryActionEmployee> Employees { get; set; } = new();
        public DisciplinaryActionInput Form { get; set; } = new();
    }

    public class DisciplinaryActionInput
    {
        public string DocumentNo { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Today;
        public DateTime? PostDate { get; set; }
        public string EmployeeId { get; set; } = "";
        public string Designation { get; set; } = "";
        public string Department { get; set; } = "";
        public string ReportingManager { get; set; } = "";
        public string DiscpActionType { get; set; } = "";
        public DateTime? IncidentDateTime { get; set; }
        public string IncidentLocation { get; set; } = "";
        public string Comments { get; set; } = "";
        public string ClosingComments { get; set; } = "";
        public string HrStatus { get; set; } = "New";
        public string WarningType { get; set; } = "";
        public string WarningAction { get; set; } = "";
        public string WarningComments { get; set; } = "";
    }

    // JSON store
    public class DisciplinaryActionStore
    {
        public List<DisciplinaryActionRecord> Records { get; set; } = new();
    }

    public class DisciplinaryActionRecord : DisciplinaryActionInput
    {
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string ApprovalStatus { get; set; } = "Pending";
    }
    public class StringOrNumberConverter : JsonConverter<string>
    {
        public override string Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? "",
                JsonTokenType.Number => reader.GetInt64().ToString(),
                JsonTokenType.Null => "",
                _ => throw new JsonException("Invalid employee ID format.")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            string value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
