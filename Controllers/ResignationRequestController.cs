
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/ResignationRequest")]
    public class ResignationRequestController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ResignationRequestController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataPath(params string[] parts) =>
            Path.Combine(
                new[] { _environment.WebRootPath, "data" }
                    .Concat(parts).ToArray());

        private string ConfigPath =>
            DataPath("ResignationRequest", "resignation-request-config.json");

        private string RecordsPath =>
            DataPath("ResignationRequest", "resignation-requests.json");

        private string EmployeesPath =>
            DataPath("PatternAssignment", "employees.json");

        private ResignationConfig ReadConfig()
        {
            var json = System.IO.File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<ResignationConfig>(
                json, _jsonOptions) ?? new ResignationConfig();
        }

        private ResignationStore ReadRecords()
        {
            if (!System.IO.File.Exists(RecordsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecordsPath)!);
                System.IO.File.WriteAllText(
                    RecordsPath,
                    JsonSerializer.Serialize(new ResignationStore(), _jsonOptions));
            }

            var json = System.IO.File.ReadAllText(RecordsPath);
            return JsonSerializer.Deserialize<ResignationStore>(
                json, _jsonOptions) ?? new ResignationStore();
        }

        private void WriteRecords(ResignationStore store)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordsPath)!);
            System.IO.File.WriteAllText(
                RecordsPath,
                JsonSerializer.Serialize(store, _jsonOptions));
        }

        private List<ResignationEmployee> ReadEmployees()
        {
            if (!System.IO.File.Exists(EmployeesPath))
                return new();

            using var document = JsonDocument.Parse(
                System.IO.File.ReadAllText(EmployeesPath));

            var element = document.RootElement;

            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("employees", out var employees))
                element = employees;

            return element.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<ResignationEmployee>>(
                    element.GetRawText(), _jsonOptions) ?? new()
                : new();
        }

        [HttpGet("")]
        public IActionResult Index(string? documentNo = null)
        {
            var model = new ResignationViewModel
            {
                Config = ReadConfig(),
                Employees = ReadEmployees(),
                Form = new ResignationInput { Date = DateTime.Today }
            };

            if (!string.IsNullOrWhiteSpace(documentNo))
            {
                var record = ReadRecords().Records.FirstOrDefault(r =>
                    string.Equals(r.DocumentNo, documentNo,
                        StringComparison.OrdinalIgnoreCase));

                if (record == null) return NotFound();

                model.Form = new ResignationInput
                {
                    DocumentNo = record.DocumentNo,
                    Date = record.Date,
                    PostDate = record.PostDate,
                    EmployeeId = record.EmployeeId,
                    Designation = record.Designation,
                    Department = record.Department,
                    ReportingManager = record.ReportingManager,
                    NoticePeriod = record.NoticePeriod,
                    Comments = record.Comments,
                    LastWorkingDay = record.LastWorkingDay,
                    ClosingComments = record.ClosingComments,
                    ApprovalStatus = record.ApprovalStatus
                };
            }

            return View("~/Views/Master/ResignationRequest.cshtml", model);
        }


        [HttpGet("/Master/ResignationRequestIndex")]
        public IActionResult ResignationRequestIndex()
        {
            var records = ReadRecords().Records
                .OrderByDescending(r => r.Date)
                .ToList();

            ViewBag.ResignationConfig = ReadConfig();

            return View(
                "~/Views/Master/ResignationRequestIndex.cshtml",
                records);
        }


        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(ResignationViewModel model)
        {
            model.Config = ReadConfig();
            model.Employees = ReadEmployees();

            if (!ModelState.IsValid)
                return View("~/Views/Master/ResignationRequest.cshtml", model);

            var input = model.Form;
            var store = ReadRecords();

            var existing = store.Records.FirstOrDefault(r =>
                string.Equals(r.DocumentNo, input.DocumentNo,
                    StringComparison.OrdinalIgnoreCase));

            var employee = model.Employees.FirstOrDefault(e =>
                string.Equals(e.Id, input.EmployeeId,
                    StringComparison.OrdinalIgnoreCase));

            if (employee != null)
            {
                input.Department = string.IsNullOrWhiteSpace(input.Department)
                    ? employee.Department : input.Department;

                input.Designation = string.IsNullOrWhiteSpace(input.Designation)
                    ? employee.Designation : input.Designation;

                input.ReportingManager =
                    string.IsNullOrWhiteSpace(input.ReportingManager)
                        ? employee.ReportingManager : input.ReportingManager;
            }

            var record = new ResignationRecord
            {
                DocumentNo = input.DocumentNo.Trim(),
                Date = input.Date,
                PostDate = input.PostDate,
                EmployeeId = input.EmployeeId,
                EmployeeCode = employee?.Code ?? existing?.EmployeeCode ?? "",
                EmployeeName = employee?.Name ?? existing?.EmployeeName ?? "",
                Designation = input.Designation,
                Department = input.Department,
                ReportingManager = input.ReportingManager,
                NoticePeriod = input.NoticePeriod,
                Comments = input.Comments,
                LastWorkingDay = input.LastWorkingDay,
                ClosingComments = input.ClosingComments,
                ApprovalStatus = existing?.ApprovalStatus ?? "Pending"
            };

            if (existing != null)
                store.Records.Remove(existing);

            store.Records.Add(record);
            WriteRecords(store);

            TempData["SuccessMessage"] = ReadConfig().Messages.SaveSuccess;
            return RedirectToAction(nameof(ResignationRequestIndex));
        }

        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string documentNo)
        {
            var store = ReadRecords();
            var record = store.Records.FirstOrDefault(r =>
                string.Equals(r.DocumentNo, documentNo,
                    StringComparison.OrdinalIgnoreCase));

            if (record == null)
                TempData["ErrorMessage"] = "Resignation request was not found.";
            else
            {
                store.Records.Remove(record);
                WriteRecords(store);
                TempData["SuccessMessage"] = ReadConfig().Messages.DeleteSuccess;
            }

            return RedirectToAction(nameof(ResignationRequestIndex));
        }
    }

    public class ResignationConfig
    {
        public int ModuleId { get; set; } = 503;
        public string ModuleName { get; set; } = "Resignation Request";
        public Dictionary<string, string> Sections { get; set; } = new();
        public Dictionary<string, string> Labels { get; set; } = new();
        public List<ResignationField> Fields { get; set; } = new();
        public Dictionary<string, string> Buttons { get; set; } = new();
        public Dictionary<string, string> Placeholders { get; set; } = new();
        public Dictionary<string, List<string>> Options { get; set; } = new();
        public ResignationValidation Validation { get; set; } = new();
        public ResignationMessages Messages { get; set; } = new();
        public ResignationIndexConfig Index { get; set; } = new();
    }

    public class ResignationField
    {
        public string Key { get; set; } = "";
        public string LabelKey { get; set; } = "";
        public string Type { get; set; } = "text";
        public string Section { get; set; } = "mainDetails";
        public bool Required { get; set; }
        public bool Readonly { get; set; }
        public int? MaxLength { get; set; }
        public string? PlaceholderKey { get; set; }
    }

    public class ResignationValidation
    {
        public int DocumentNoMaxLength { get; set; } = 60;
        public int DesignationMaxLength { get; set; } = 60;
        public int DepartmentMaxLength { get; set; } = 60;
        public int ReportingManagerMaxLength { get; set; } = 60;
        public int NoticePeriodMaxLength { get; set; } = 60;
        public int CommentsMaxLength { get; set; } = 200;
        public int ClosingCommentsMaxLength { get; set; } = 200;
    }

    public class ResignationMessages
    {
        public string ConfirmDelete { get; set; } =
            "Are you sure you want to delete this resignation request?";
        public string SaveSuccess { get; set; } =
            "Resignation request saved successfully.";
        public string DeleteSuccess { get; set; } =
            "Resignation request deleted successfully.";
        public string NoRecords { get; set; } =
            "No resignation requests found.";
    }

    public class ResignationIndexConfig
    {
        public string Title { get; set; } = "Resignation Request Index";
        public string SearchPlaceholder { get; set; } = "";
        public string EmptyMessage { get; set; } = "";
        public List<ResignationColumn> Columns { get; set; } = new();
    }

    public class ResignationColumn
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public class ResignationEmployee
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Designation { get; set; } = "";
        public string ReportingManager { get; set; } = "";
    }

    public class ResignationViewModel
    {
        public ResignationConfig Config { get; set; } = new();
        public List<ResignationEmployee> Employees { get; set; } = new();
        public ResignationInput Form { get; set; } = new();
    }

    public class ResignationInput
    {
        public string DocumentNo { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Today;
        public DateTime? PostDate { get; set; }
        public string EmployeeId { get; set; } = "";
        public string Designation { get; set; } = "";
        public string Department { get; set; } = "";
        public string ReportingManager { get; set; } = "";
        public string NoticePeriod { get; set; } = "";
        public string Comments { get; set; } = "";
        public DateTime? LastWorkingDay { get; set; }
        public string ClosingComments { get; set; } = "";
        public string ApprovalStatus { get; set; } = "Pending";
    }

    public class ResignationRecord : ResignationInput
    {
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
    }

    public class ResignationStore
    {
        public List<ResignationRecord> Records { get; set; } = new();
    }
}
