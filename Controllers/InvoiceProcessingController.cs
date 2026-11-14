using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/InvoiceProcessing")]
    public class InvoiceProcessingController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public InvoiceProcessingController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string TimesheetDirectory =>
            Path.Combine(_environment.WebRootPath, "data", "Timesheet");

        private string InvoiceDirectory =>
            Path.Combine(_environment.WebRootPath, "data", "InvoiceProcessing");

        private string TimesheetPath(string fileName) =>
            Path.Combine(TimesheetDirectory, fileName);

        private string InvoicePath(string fileName) =>
            Path.Combine(InvoiceDirectory, fileName);

        private JsonObject ReadJson(string path)
        {
            if (!System.IO.File.Exists(path))
                return new JsonObject();

            return JsonNode.Parse(System.IO.File.ReadAllText(path))
                as JsonObject ?? new JsonObject();
        }

        private JsonObject ReadTimesheetJson(string fileName) =>
            ReadJson(TimesheetPath(fileName));

        private JsonArray ReadTimesheetRecords(string fileName) =>
            ReadTimesheetJson(fileName)["records"] as JsonArray ?? new JsonArray();

        private JsonObject ReadInvoiceData() =>
            ReadJson(InvoicePath("invoice-processing-data.json"));

        private void WriteInvoiceData(JsonObject data)
        {
            Directory.CreateDirectory(InvoiceDirectory);
            System.IO.File.WriteAllText(
                InvoicePath("invoice-processing-data.json"),
                data.ToJsonString(_jsonOptions)
            );
        }

        private static JsonArray ReadInvoiceArray(JsonObject data, string name) =>
            data[name] as JsonArray ?? new JsonArray();

        [HttpGet("")]
        public IActionResult Index(int? id)
        {
            ViewData["Config"] = ReadJson(InvoicePath("invoice-processing-config.json"));
            ViewData["Lookups"] = ReadJson(InvoicePath("invoice-processing-lookups.json"));
            ViewData["Clients"] = ReadTimesheetRecords("clients.json");
            ViewData["Departments"] = ReadTimesheetRecords("departments.json");
            ViewData["Invoice"] = new JsonObject();

            if (id.HasValue)
            {
                var invoice = ReadInvoiceArray(ReadInvoiceData(), "invoices")
                    .FirstOrDefault(x => (int?)x?["id"] == id.Value);

                if (invoice != null)
                    ViewData["Invoice"] = invoice.DeepClone();
            }

            return View("~/Views/Master/InvoiceProcessing.cshtml");
        }

        [HttpGet("/Master/InvoiceProcessingIndex")]
        public IActionResult InvoiceProcessingIndex()
        {
            var invoices = ReadInvoiceArray(ReadInvoiceData(), "invoices")
                .Select(x => x?.DeepClone())
                .Where(x => x != null)
                .Cast<JsonNode>()
                .ToList();

            ViewData["Config"] = ReadJson(InvoicePath("invoice-processing-config.json"));

            return View("~/Views/Master/InvoiceProcessingIndex.cshtml", invoices);
        }

        // Same source and filtering rule as the Timesheet agreement dropdown.
        [HttpGet("GetAgreements")]
        public IActionResult GetAgreements(int clientId)
        {
            var agreements = ReadTimesheetRecords("main-frame-agreements.json");

            var result = agreements.Where(a =>
                (int?)a?["clientId"] == clientId &&
                (bool?)a?["active"] != false
            );

            return Json(result);
        }

        // Load attendance using the same source files and calculation rules as Timesheet.
        // The selected Date supplies the year; PayMonth supplies the month.
        [HttpPost("Load")]
        [ValidateAntiForgeryToken]
        public IActionResult Load([FromBody] JsonObject? request)
        {
            if (request == null)
                return BadRequest(new { message = "Invalid request." });

            int clientId = (int?)request["clientId"] ?? 0;
            int agreementId = (int?)request["mainFrameAgreementId"] ?? 0;

            string payMonth = request["payMonth"]?.ToString() ?? "";
            if (!DateTime.TryParse(request["date"]?.ToString(), out var invoiceDate))
                return BadRequest(new { message = "Please select a valid Date." });

            if (clientId <= 0 || agreementId <= 0 || string.IsNullOrWhiteSpace(payMonth))
                return BadRequest(new { message = "Please select Client, Main Frame Agreement and PayMonth." });

            if (!DateTime.TryParseExact(
                    payMonth,
                    "MMMM",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var monthDate))
            {
                return BadRequest(new { message = "Invalid PayMonth selected." });
            }

            int year = invoiceDate.Year;
            DateTime startDate;
            DateTime endDate;

            try
            {
                startDate = new DateTime(year, monthDate.Month, 1);
                endDate = startDate.AddMonths(1).AddDays(-1);
            }
            catch
            {
                return BadRequest(new { message = "Unable to calculate the PayMonth date range." });
            }

            var departmentIds = request["departmentIds"] as JsonArray ?? new JsonArray();
            var selectedDepartments = departmentIds
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var employees = ReadTimesheetRecords("employees.json");
            var attendance = ReadTimesheetRecords("attendance.json");
            var leaves = ReadTimesheetRecords("leave-records.json");
            var claims = ReadTimesheetRecords("claims.json");

            var result = new JsonArray();

            var filteredEmployees = employees.Where(employee =>
            {
                bool matchesClient = (int?)employee?["clientId"] == clientId;
                bool matchesAgreement = (int?)employee?["mainFrameAgreementId"] == agreementId;
                bool matchesDepartment =
                    selectedDepartments.Count == 0 ||
                    selectedDepartments.Contains(employee?["department"]?.ToString() ?? "");
                bool active = (bool?)employee?["active"] != false;

                return matchesClient && matchesAgreement && matchesDepartment && active;
            });

            foreach (var employee in filteredEmployees)
            {
                int employeeId = (int?)employee?["id"] ?? 0;

                var employeeAttendance = attendance.Where(a =>
                    (int?)a?["employeeId"] == employeeId &&
                    DateTime.TryParse(a?["date"]?.ToString(), out var attendanceDate) &&
                    attendanceDate.Date >= startDate.Date &&
                    attendanceDate.Date <= endDate.Date
                ).ToList();

                double SumAttendance(string field) =>
                    employeeAttendance.Sum(a => (double?)a?[field] ?? 0);

                double approvedLeaves = leaves.Where(l =>
                    (int?)l?["employeeId"] == employeeId &&
                    string.Equals(l?["status"]?.ToString(), "Approved",
                        StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParse(l?["startDate"]?.ToString(), out var leaveStart) &&
                    DateTime.TryParse(l?["endDate"]?.ToString(), out var leaveEnd) &&
                    leaveStart.Date <= endDate.Date &&
                    leaveEnd.Date >= startDate.Date
                ).Sum(l => (double?)l?["days"] ?? 0);

                double ClaimAmount(string claimType) =>
                    claims.Where(c =>
                        (int?)c?["employeeId"] == employeeId &&
                        c?["claimType"]?.ToString() == claimType &&
                        DateTime.TryParse(c?["date"]?.ToString(), out var claimDate) &&
                        claimDate.Date >= startDate.Date &&
                        claimDate.Date <= endDate.Date
                    ).Sum(c => (double?)c?["amount"] ?? 0);

                result.Add(new JsonObject
                {
                    ["payMonth"] = payMonth,
                    ["batchNo"] = request["batchNo"]?.DeepClone(),
                    ["empCode"] = employee?["empCode"]?.DeepClone(),
                    ["empName"] = employee?["empName"]?.DeepClone(),
                    ["contractType"] = employee?["contractType"]?.DeepClone(),
                    ["workingDaysHrs"] = JsonValue.Create(SumAttendance("workingDays")),
                    ["daysHrsWorked"] = JsonValue.Create(SumAttendance("daysWorked")),
                    ["otHrs"] = JsonValue.Create(SumAttendance("otHours")),
                    ["hotHrs"] = JsonValue.Create(SumAttendance("hotHours")),
                    ["nightOtHrs"] = JsonValue.Create(SumAttendance("nightOtHours")),
                    ["weeklyPublicOffs"] = JsonValue.Create(SumAttendance("weeklyPublicOffs")),
                    ["approvedLeaves"] = JsonValue.Create(approvedLeaves),
                    ["menaClaim"] = JsonValue.Create(ClaimAmount("Mena Claim")),
                    ["reimbursableClaim1"] = JsonValue.Create(ClaimAmount("Reimbursable Claim-1")),
                    ["reimbursableClaim2"] = JsonValue.Create(ClaimAmount("Reimbursable Claim-2")),
                    ["startDate"] = JsonValue.Create(startDate.ToString("yyyy-MM-dd")),
                    ["endDate"] = JsonValue.Create(endDate.ToString("yyyy-MM-dd")),
                    ["remarks"] = JsonValue.Create("")
                });
            }

            return Json(new
            {
                success = true,
                startDate = startDate.ToString("yyyy-MM-dd"),
                endDate = endDate.ToString("yyyy-MM-dd"),
                attendanceRows = result
            });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save([FromBody] JsonObject? invoice)
        {
            if (invoice == null)
                return BadRequest(new { message = "Invalid invoice data." });

            var data = ReadInvoiceData();

            if (data["invoices"] is not JsonArray invoices)
            {
                invoices = new JsonArray();
                data["invoices"] = invoices;
            }

            int id = (int?)invoice["id"] ?? 0;
            if (id == 0)
            {
                id = ((int?)data["lastId"] ?? 0) + 1;
                data["lastId"] = id;
            }

            invoice["id"] = id;

            var existing = invoices.FirstOrDefault(x => (int?)x?["id"] == id);
            if (existing != null)
                invoices[invoices.IndexOf(existing)] = invoice.DeepClone();
            else
                invoices.Add(invoice.DeepClone());

            WriteInvoiceData(data);

            return Json(new { success = true, id, message = "Invoice saved successfully." });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var data = ReadInvoiceData();

            if (data["invoices"] is not JsonArray invoices)
                return NotFound();

            var invoice = invoices.FirstOrDefault(x => (int?)x?["id"] == id);
            if (invoice == null)
                return NotFound();

            invoices.Remove(invoice);
            WriteInvoiceData(data);

            return Json(new { success = true, message = "Invoice deleted successfully." });
        }
    }
}
