
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/Grievances")]
    public class GrievancesController : Controller
    {
        private readonly IWebHostEnvironment _env;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public GrievancesController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string DataPath(params string[] parts)
        {
            return Path.Combine(
                new[] { _env.WebRootPath, "data" }.Concat(parts).ToArray()
            );
        }

        private string EmployeesPath =>
            DataPath("PatternAssignment", "employees.json");

        private string ConfigPath =>
            DataPath("Grievances", "grievance-config.json");

        private string RecordsPath =>
            DataPath("Grievances", "grievances.json");

        private List<Employee> ReadEmployees()
        {
            if (!System.IO.File.Exists(EmployeesPath))
                throw new FileNotFoundException(
                    "Employee JSON file was not found.", EmployeesPath);

            var json = System.IO.File.ReadAllText(EmployeesPath);

            return JsonSerializer.Deserialize<List<Employee>>(
                json, _jsonOptions) ?? new List<Employee>();
        }

        private GrievanceConfig ReadConfig()
        {
            if (!System.IO.File.Exists(ConfigPath))
                throw new FileNotFoundException(
                    "Grievance configuration JSON was not found.",
                    ConfigPath);

            var json = System.IO.File.ReadAllText(ConfigPath);

            return JsonSerializer.Deserialize<GrievanceConfig>(
                json, _jsonOptions) ?? new GrievanceConfig();
        }

        private GrievanceStore ReadRecords()
        {
            if (!System.IO.File.Exists(RecordsPath))
                return new GrievanceStore();

            var json = System.IO.File.ReadAllText(RecordsPath);

            return JsonSerializer.Deserialize<GrievanceStore>(
                json, _jsonOptions) ?? new GrievanceStore();
        }

        private void WriteRecords(GrievanceStore store)
        {
            var directory = Path.GetDirectoryName(RecordsPath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(store, _jsonOptions);

            System.IO.File.WriteAllText(RecordsPath, json);
        }

        private GrievanceViewModel BuildViewModel(GrievanceInput? input = null)
        {
            return new GrievanceViewModel
            {
                Employees = ReadEmployees()
                    .Where(e => e.Active)
                    .OrderBy(e => e.Name)
                    .ToList(),

                Config = ReadConfig(),

                Form = input ?? new GrievanceInput
                {
                    Date = DateTime.Today,
                    HrStatus = ReadConfig().Options.HrStatus
                        .FirstOrDefault() ?? string.Empty
                }
            };
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            var model = BuildViewModel();
            return View("~/Views/Master/Grievances.cshtml", model);
        }

        [HttpGet("/Master/GrievancesIndex")]
        public IActionResult GrievancesIndex()
        {
            var store = ReadRecords();

            var records = store.Records
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.DocumentNo)
                .ToList();

            return View("~/Views/Master/GrievancesIndex.cshtml", records);
        }


        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(GrievanceViewModel model)
        {
            var input = model.Form ?? new GrievanceInput();
            var employees = ReadEmployees()
                .Where(e => e.Active)
                .ToList();

            var config = ReadConfig();

            var employee = employees.FirstOrDefault(
                e => e.Id == input.EmployeeId);

            if (employee == null)
            {
                ModelState.AddModelError(
                    "Form.EmployeeId", "Select a valid active employee.");
            }

            if (!config.Options.Against.Contains(input.Against))
            {
                ModelState.AddModelError(
                    "Form.Against", "Select a valid category.");
            }

            if (!config.Options.HrStatus.Contains(input.HrStatus))
            {
                ModelState.AddModelError(
                    "Form.HrStatus", "Select a valid HR status.");
            }

            if (!string.IsNullOrWhiteSpace(input.EmpStatus) &&
                !config.Options.EmpStatus.Contains(input.EmpStatus))
            {
                ModelState.AddModelError(
                    "Form.EmpStatus", "Select a valid employee status.");
            }

            if (input.HrStatus == config.Validation
                    .ClosingCommentsRequiredWhenHrStatus &&
                string.IsNullOrWhiteSpace(input.ClosingComments))
            {
                ModelState.AddModelError(
                    "Form.ClosingComments",
                    "Closing Comments are required when HR Status is Closed.");
            }

            if (!ModelState.IsValid)
            {
                model.Employees = employees;
                model.Config = config;
                model.Form = input;

                return View("~/Views/Master/Grievances.cshtml", model);
            }

            var store = ReadRecords();

            // Reject duplicate document numbers.
            if (store.Records.Any(r =>
                string.Equals(
                    r.DocumentNo,
                    input.DocumentNo.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(
                    "Form.DocumentNo",
                    "This document number already exists.");

                model.Employees = employees;
                model.Config = config;
                model.Form = input;

                return View("~/Views/Master/Grievances.cshtml", model);
            }

            store.Records.Add(new GrievanceRecord
            {
                DocumentNo = input.DocumentNo.Trim(),
                Date = input.Date,
                PostDate = null,
                EmployeeId = employee!.Id,
                EmployeeCode = employee.Code,
                EmployeeName = employee.Name,
                Designation = input.Designation?.Trim() ?? string.Empty,
                Department = employee.Department,
                ReportingManager =
                    input.ReportingManager?.Trim() ?? string.Empty,
                Against = input.Against,
                Comments = input.Comments?.Trim() ?? string.Empty,
                HrStatus = input.HrStatus,
                ClosingComments =
                    input.ClosingComments?.Trim() ?? string.Empty,
                EmpStatus = input.EmpStatus ?? string.Empty,
                EmployeeClosingComments =
                    input.EmployeeClosingComments?.Trim() ?? string.Empty
            });

            WriteRecords(store);

            TempData["SuccessMessage"] = "Grievance saved successfully.";

            return RedirectToAction(nameof(Index));
        }

        public class Employee
        {
            [JsonPropertyName("id")]
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
            public bool Active { get; set; }
        }

        public class GrievanceOptions
        {
            public List<string> Against { get; set; } = new();
            public List<string> HrStatus { get; set; } = new();
            public List<string> EmpStatus { get; set; } = new();
        }

        public class GrievanceValidation
        {
            public int DocumentNoMaxLength { get; set; } = 60;
            public int DesignationMaxLength { get; set; } = 60;
            public int DepartmentMaxLength { get; set; } = 60;
            public int ReportingManagerMaxLength { get; set; } = 60;
            public int CommentsMaxLength { get; set; } = 200;
            public int ClosingCommentsMaxLength { get; set; } = 200;
            public int EmployeeClosingCommentsMaxLength { get; set; } = 200;

            public string ClosingCommentsRequiredWhenHrStatus { get; set; }
                = "Closed";
        }

        public class GrievanceConfig
        {
            public int ModuleId { get; set; }
            public GrievanceOptions Options { get; set; } = new();
            public GrievanceValidation Validation { get; set; } = new();
        }

        public class GrievanceStore
        {
            public List<GrievanceRecord> Records { get; set; } = new();
        }

        public class GrievanceRecord
        {
            public string DocumentNo { get; set; } = "";
            public DateTime Date { get; set; }
            public DateTime? PostDate { get; set; }
            public string EmployeeId { get; set; } = "";
            public string EmployeeCode { get; set; } = "";
            public string EmployeeName { get; set; } = "";
            public string Designation { get; set; } = "";
            public string Department { get; set; } = "";
            public string ReportingManager { get; set; } = "";
            public string Against { get; set; } = "";
            public string Comments { get; set; } = "";
            public string HrStatus { get; set; } = "";
            public string ClosingComments { get; set; } = "";
            public string EmpStatus { get; set; } = "";
            public string EmployeeClosingComments { get; set; } = "";
        }

        public class GrievanceInput
        {
            [Required]
            [StringLength(60)]
            public string DocumentNo { get; set; } = "";

            [Required]
            public DateTime Date { get; set; } = DateTime.Today;

            [Required]
            public string EmployeeId { get; set; } = "";

            [StringLength(60)]
            public string? Designation { get; set; }

            [StringLength(60)]
            public string? ReportingManager { get; set; }

            [Required]
            public string Against { get; set; } = "";

            [StringLength(200)]
            public string? Comments { get; set; }

            [Required]
            public string HrStatus { get; set; } = "";

            [StringLength(200)]
            public string? ClosingComments { get; set; }

            public string? EmpStatus { get; set; }

            [StringLength(200)]
            public string? EmployeeClosingComments { get; set; }
        }

        public class GrievanceViewModel
        {
            public List<Employee> Employees { get; set; } = new();
            public GrievanceConfig Config { get; set; } = new();
            public GrievanceInput Form { get; set; } = new();
        }
    }
}
