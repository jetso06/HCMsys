
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace HCMSys.Controllers
{
    [Route("Master/Timesheet")]
    public class TimesheetController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        public TimesheetController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataPath(string fileName)
        {
            return Path.Combine(
                _environment.WebRootPath,
                "data",
                "Timesheet",
                fileName
            );
        }

        private JsonObject ReadJson(string fileName)
        {
            string path = DataPath(fileName);

            if (!System.IO.File.Exists(path))
                return new JsonObject();

            return JsonNode.Parse(
                System.IO.File.ReadAllText(path)
            ) as JsonObject ?? new JsonObject();
        }

        private JsonArray ReadRecords(string fileName)
        {
            return ReadJson(fileName)["records"] as JsonArray
                   ?? new JsonArray();
        }

        private void WriteJson(string fileName, JsonObject data)
        {
            string path = DataPath(fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            System.IO.File.WriteAllText(
                path,
                data.ToJsonString(_jsonOptions)
            );
        }

        // Open Timesheet page
        [HttpGet("")]
        public IActionResult Index()
        {
            ViewData["TimesheetConfig"] =
                ReadJson("timesheet-config.json");

            ViewData["GridConfig"] =
                ReadJson("attendance-grid-config.json");

            ViewData["Employees"] =
                ReadRecords("employees.json");

            ViewData["Clients"] =
                ReadRecords("clients.json");

            ViewData["Agreements"] =
                ReadRecords("main-frame-agreements.json");

            ViewData["Departments"] =
                ReadRecords("departments.json");

            return View("~/Views/Master/Timesheet.cshtml");
        }


        [HttpGet("/Master/TimesheetIndex")]
        public IActionResult TimesheetIndex()
        {
            // Read list column configuration
            ViewData["TimesheetListConfig"] =
                ReadJson("timesheet-list-config.json");

            // Read actual timesheet records
            var records = ReadRecords("timesheets.json")
                .Select(x => x?.DeepClone())
                .Where(x => x != null)
                .Cast<JsonNode>()
                .ToList();

            return View(
                "~/Views/Master/TimesheetIndex.cshtml",
                records
            );
        }


        // Get agreements belonging to selected client
        [HttpGet("GetAgreements")]
        public IActionResult GetAgreements(int clientId)
        {
            var agreements = ReadRecords(
                "main-frame-agreements.json"
            );

            var result = agreements
                .Where(a =>
                    (int?)a?["clientId"] == clientId &&
                    (bool?)a?["active"] != false
                );

            return Json(result);
        }

        // Load attendance grid
        [HttpPost("Load")]
        public IActionResult Load([FromBody] JsonObject request)
        {
            int clientId = (int?)request["clientId"] ?? 0;
            int agreementId =
                (int?)request["mainFrameAgreementId"] ?? 0;

            DateTime startDate =
                DateTime.TryParse(
                    request["startDate"]?.ToString(),
                    out var start
                ) ? start : DateTime.MinValue;

            DateTime endDate =
                DateTime.TryParse(
                    request["endDate"]?.ToString(),
                    out var end
                ) ? end : DateTime.MaxValue;

            if (startDate > endDate)
            {
                return BadRequest(new
                {
                    message = "Start Date cannot be after End Date."
                });
            }

            var departmentIds =
                request["departmentIds"] as JsonArray
                ?? new JsonArray();

            var selectedDepartments = departmentIds
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet();

            var employees = ReadRecords("employees.json");
            var attendance = ReadRecords("attendance.json");
            var leaves = ReadRecords("leave-records.json");
            var claims = ReadRecords("claims.json");

            var result = new JsonArray();

            var filteredEmployees = employees.Where(employee =>
            {
                bool matchesClient =
                    (int?)employee?["clientId"] == clientId;

                bool matchesAgreement =
                    (int?)employee?["mainFrameAgreementId"]
                    == agreementId;

                bool matchesDepartment =
                    selectedDepartments.Count == 0 ||
                    selectedDepartments.Contains(
                        employee?["department"]?.ToString()
                    );

                bool active =
                    (bool?)employee?["active"] != false;

                return matchesClient &&
                       matchesAgreement &&
                       matchesDepartment &&
                       active;
            });

            foreach (var employee in filteredEmployees)
            {
                int employeeId =
                    (int?)employee?["id"] ?? 0;

                var employeeAttendance = attendance
                    .Where(a =>
                        (int?)a?["employeeId"] == employeeId &&
                        DateTime.TryParse(
                            a?["date"]?.ToString(),
                            out var attendanceDate
                        ) &&
                        attendanceDate.Date >= startDate.Date &&
                        attendanceDate.Date <= endDate.Date
                    )
                    .ToList();

                double SumAttendance(string field)
                {
                    return employeeAttendance.Sum(a =>
                        (double?)a?[field] ?? 0
                    );
                }

                double approvedLeaves = leaves
                    .Where(l =>
                        (int?)l?["employeeId"] == employeeId &&
                        string.Equals(
                            l?["status"]?.ToString(),
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        ) &&
                        DateTime.TryParse(
                            l?["startDate"]?.ToString(),
                            out var leaveStart
                        ) &&
                        DateTime.TryParse(
                            l?["endDate"]?.ToString(),
                            out var leaveEnd
                        ) &&
                        leaveStart.Date <= endDate.Date &&
                        leaveEnd.Date >= startDate.Date
                    )
                    .Sum(l => (double?)l?["days"] ?? 0);

                double ClaimAmount(string claimType)
                {
                    return claims
                        .Where(c =>
                            (int?)c?["employeeId"] == employeeId &&
                            c?["claimType"]?.ToString() == claimType &&
                            DateTime.TryParse(
                                c?["date"]?.ToString(),
                                out var claimDate
                            ) &&
                            claimDate.Date >= startDate.Date &&
                            claimDate.Date <= endDate.Date
                        )
                        .Sum(c => (double?)c?["amount"] ?? 0);
                }

                // Store the calculated values using their configured column IDs.
                var computedValues = new Dictionary<string, JsonNode?>
                {
                    ["empCode"] = employee?["empCode"]?.DeepClone(),
                    ["empName"] = employee?["empName"]?.DeepClone(),
                    ["contractType"] = employee?["contractType"]?.DeepClone(),

                    ["workingDays"] = JsonValue.Create(SumAttendance("workingDays")),
                    ["daysWorked"] = JsonValue.Create(SumAttendance("daysWorked")),
                    ["otHours"] = JsonValue.Create(SumAttendance("otHours")),
                    ["hotHours"] = JsonValue.Create(SumAttendance("hotHours")),
                    ["nightOtHours"] = JsonValue.Create(SumAttendance("nightOtHours")),
                    ["weeklyPublicOffs"] = JsonValue.Create(SumAttendance("weeklyPublicOffs")),

                    ["approvedLeaves"] = JsonValue.Create(approvedLeaves),

                    ["menaClaim"] = JsonValue.Create(ClaimAmount("Mena Claim")),
                    ["reimbursableClaim1"] = JsonValue.Create(ClaimAmount("Reimbursable Claim-1")),
                    ["reimbursableClaim2"] = JsonValue.Create(ClaimAmount("Reimbursable Claim-2")),

                    ["remarks"] = JsonValue.Create("")
                };

                // Read the attendance grid configuration.
                var gridConfig = ReadJson("attendance-grid-config.json");
                var configuredColumns = gridConfig["columns"] as JsonArray;

                // Keep employeeId for identifying the employee.
                var row = new JsonObject
                {
                    ["employeeId"] = employeeId
                };

                // Generate the row using configured columns.
                if (configuredColumns != null)
                {
                    foreach (var column in configuredColumns)
                    {
                        string? columnId = column?["id"]?.ToString();

                        if (string.IsNullOrWhiteSpace(columnId))
                            continue;

                        row[columnId] = computedValues.TryGetValue(columnId, out var value)
                            ? value?.DeepClone()
                            : null;
                    }
                }

                result.Add(row);
            }

            return Json(result);
        }

        // Save Timesheet
        [HttpPost("Save")]
        public IActionResult Save([FromBody] JsonObject timesheet)
        {
            var data = ReadJson("timesheets.json");

            if (data["records"] is not JsonArray records)
            {
                records = new JsonArray();
                data["records"] = records;
            }

            int id = (int?)timesheet["id"] ?? 0;

            if (id == 0)
            {
                id = ((int?)data["lastId"] ?? 0) + 1;
                data["lastId"] = id;
                timesheet["id"] = id;
            }

            var existing = records
                .FirstOrDefault(r => (int?)r?["id"] == id);

            if (existing != null)
            {
                int index = records.IndexOf(existing);
                records[index] = timesheet.DeepClone();
            }
            else
            {
                records.Add(timesheet.DeepClone());
            }

            WriteJson("timesheets.json", data);

            return Json(new
            {
                success = true,
                id,
                message = "Timesheet saved successfully."
            });
        }

        // List saved Timesheets
        [HttpGet("List")]
        public IActionResult List()
        {
            return Json(ReadRecords("timesheets.json"));
        }

        // Delete Timesheet
        [HttpPost("Delete/{id:int}")]
        public IActionResult Delete(int id)
        {
            var data = ReadJson("timesheets.json");

            if (data["records"] is not JsonArray records)
                return NotFound();

            var record = records.FirstOrDefault(
                r => (int?)r?["id"] == id
            );

            if (record == null)
                return NotFound();

            records.Remove(record);

            WriteJson("timesheets.json", data);

            return Json(new
            {
                success = true,
                message = "Timesheet deleted successfully."
            });
        }
    }
}
