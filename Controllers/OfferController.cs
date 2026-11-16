using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/Offer")]
    public class OfferController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public OfferController(IWebHostEnvironment env) => _env = env;

        private string Dir => Path.Combine(_env.WebRootPath, "data", "Offer");
        private string ConfigPath => Path.Combine(Dir, "offer-config.json");
        private string RecordsPath => Path.Combine(Dir, "offers.json");

        private async Task<T> Read<T>(string path, T fallback)
        {
            if (!System.IO.File.Exists(path)) return fallback;
            return JsonSerializer.Deserialize<T>(await System.IO.File.ReadAllTextAsync(path), _json) ?? fallback;
        }

        private async Task Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Dir);
            await System.IO.File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, _json));
        }

        private Task<OfferPageConfig> Config() => Read(ConfigPath, new OfferPageConfig());
        private Task<List<OfferRecord>> Records() => Read(RecordsPath, new List<OfferRecord>());

        private async Task<Dictionary<string, List<OfferOption>>> LoadLookups(OfferPageConfig config)
        {
            var result = new Dictionary<string, List<OfferOption>>(StringComparer.OrdinalIgnoreCase);
            var sources = config.Sections.SelectMany(x => x.Fields)
                .Concat(config.Grids.SelectMany(x => x.Columns))
                .Where(x => !string.IsNullOrWhiteSpace(x.Source))
                .Select(x => x.Source!).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                var localPath = Path.Combine(Dir, Path.GetFileName(source));
                var callOffPath = Path.Combine(_env.WebRootPath, "data", "CallOff", Path.GetFileName(source));
                var path = System.IO.File.Exists(localPath) ? localPath : callOffPath;
                result[source] = await ReadOptions(path);
            }

            // Same Classification master categories already used by CallOff.
            var classificationDir = Path.Combine(_env.WebRootPath, "data", "Classification");
            var typePath = Path.Combine(classificationDir, "classification-types.json");
            var recordPath = Path.Combine(classificationDir, "classifications.json");
            if (System.IO.File.Exists(typePath) && System.IO.File.Exists(recordPath))
            {
                var types = await Read(typePath, new List<OfferMasterType>());
                var records = await Read(recordPath, new List<OfferMasterRecord>());
                AddMasterOptions("salary-categories.json", "Salary Category", types, records, result);
                AddMasterOptions("benefit-types.json", "Benefit", types, records, result);
                AddMasterOptions("additional-checks.json", "AdditionalChecks", types, records, result);
            }

            // Use existing CallOff records for the CallOff Number dropdown.
            var callOffRecords = Path.Combine(_env.WebRootPath, "data", "CallOff", "calloffs.json");
            if (System.IO.File.Exists(callOffRecords))
            {
                using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(callOffRecords));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("calloffs", out var nested))
                    root = nested;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    result["calloff-numbers.json"] = root.EnumerateArray()
                        .Select(x => TryText(x, "callOffNumber") ?? TryText(x, "docNo"))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => new OfferOption { Value = x!, Text = x! })
                        .GroupBy(x => x.Value).Select(x => x.First()).ToList();
                }
            }
            return result;
        }

        private async Task<List<OfferOption>> ReadOptions(string path)
        {
            var list = new List<OfferOption>();
            if (!System.IO.File.Exists(path)) return list;
            using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString() ?? "";
                    list.Add(new OfferOption { Value = value, Text = value });
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var value = TryText(item, "value") ?? TryText(item, "id") ?? "";
                    var text = TryText(item, "text") ?? TryText(item, "name") ?? value;
                    list.Add(new OfferOption { Value = value, Text = text });
                }
            }
            return list;
        }

        private static string? TryText(JsonElement item, string name)
        {
            if (item.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in item.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    return p.Value.ToString();
            return null;
        }

        private static void AddMasterOptions(string source, string masterName,
            List<OfferMasterType> types, List<OfferMasterRecord> records,
            Dictionary<string, List<OfferOption>> result)
        {
            var type = types.FirstOrDefault(x => x.Name.Equals(masterName, StringComparison.OrdinalIgnoreCase));
            if (type == null) return;
            result[source] = records.Where(x => x.MasterType == type.Id && x.IsActive)
                .Select(x => new OfferOption { Value = x.Id.ToString(), Text = x.Name }).ToList();
        }

        [HttpGet("")]
        [HttpGet("Form")]
        public async Task<IActionResult> Form(int? id, bool clone = false)
        {
            var config = await Config();
            var records = await Records();
            var existing = id.HasValue ? records.FirstOrDefault(x => x.Id == id.Value) : null;
            if (id.HasValue && existing == null) return NotFound();

            var model = existing == null ? new OfferRecord() :
                JsonSerializer.Deserialize<OfferRecord>(JsonSerializer.Serialize(existing, _json), _json)!;
            if (clone && existing != null) { model.Id = 0; model.OfferNo = ""; model.Status = "Pending"; }
            ViewBag.Config = config;
            ViewBag.Lookups = await LoadLookups(config);
            ViewBag.IsReadOnly = existing != null && !clone;
            return View("~/Views/Master/Offer.cshtml", model);
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(OfferRecord model)
        {
            model.Duties ??= new(); model.SalaryBreakdown ??= new(); model.Benefits ??= new();
            model.MedicalInsuranceRows ??= new(); model.TravelBenefits ??= new(); model.ComplianceConditions ??= new();
            var rows = await Records();
            if (model.Id == 0)
            {
                model.Id = rows.Count == 0 ? 1 : rows.Max(x => x.Id) + 1;
                if (string.IsNullOrWhiteSpace(model.OfferNo)) model.OfferNo = $"OFF-{model.Id:00000}";
                model.CreatedBy = "admin"; model.CreatedDateTime = DateTime.Now; model.Status = "Pending";
                rows.Add(model);
            }
            else
            {
                var old = rows.FirstOrDefault(x => x.Id == model.Id);
                if (old == null) return NotFound();
                model.OfferNo = old.OfferNo; model.CreatedBy = old.CreatedBy;
                model.CreatedDateTime = old.CreatedDateTime; model.Status = old.Status;
                model.ModifiedBy = "admin"; model.ModifiedDateTime = DateTime.Now;
                rows[rows.IndexOf(old)] = model;
            }
            await Write(RecordsPath, rows);
            TempData["Success"] = "Offer saved successfully.";
            return RedirectToAction(nameof(Form), new { id = model.Id });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rows = await Records(); rows.RemoveAll(x => x.Id == id); await Write(RecordsPath, rows);
            TempData["Success"] = "Offer deleted.";
            return RedirectToAction(nameof(Form));
        }

        [HttpPost("Approve/{id:int}")] [ValidateAntiForgeryToken]
        public Task<IActionResult> Approve(int id) => SetStatus(id, "Approved");
        [HttpPost("Reject/{id:int}")] [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id) => SetStatus(id, "Rejected");
        [HttpPost("UnPost/{id:int}")] [ValidateAntiForgeryToken]
        public Task<IActionResult> UnPost(int id) => SetStatus(id, "Pending");

        private async Task<IActionResult> SetStatus(int id, string status)
        {
            var rows = await Records(); var row = rows.FirstOrDefault(x => x.Id == id);
            if (row == null) return NotFound();
            row.Status = status; row.ModifiedBy = "admin"; row.ModifiedDateTime = DateTime.Now;
            await Write(RecordsPath, rows);
            TempData["Success"] = $"Offer status changed to {status}.";
            return RedirectToAction(nameof(Form), new { id });
        }
    }

    public class OfferPageConfig { public OfferPageInfo Page { get; set; } = new(); public OfferButtonGroup Buttons { get; set; } = new(); public List<OfferSection> Sections { get; set; } = new(); public List<OfferGridConfig> Grids { get; set; } = new(); public List<OfferCommentConfig> Comments { get; set; } = new(); }
    public class OfferPageInfo { public string Title { get; set; } = "Offer"; public int PageId { get; set; } = 402; public string Icon { get; set; } = "fa-file-signature"; }
    public class OfferButtonGroup { public List<OfferButton> Form { get; set; } = new(); }
    public class OfferButton { public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string Icon { get; set; } = ""; public string Style { get; set; } = "secondary"; public string Action { get; set; } = ""; public bool Visible { get; set; } = true; }
    public class OfferSection { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public List<OfferField> Fields { get; set; } = new(); }
    public class OfferField { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public string Type { get; set; } = "text"; public string? Source { get; set; } public string? Remarks { get; set; } public bool Required { get; set; } public bool Readonly { get; set; } public string? Step { get; set; } }
    public class OfferGridConfig { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Subtitle { get; set; } = ""; public string Property { get; set; } = ""; public List<OfferField> Columns { get; set; } = new(); }
    public class OfferCommentConfig { public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string Grid { get; set; } = ""; }
    public class OfferOption { public string Value { get; set; } = ""; public string Text { get; set; } = ""; }
    public class OfferMasterType { public int Id { get; set; } public string Name { get; set; } = ""; }
    public class OfferMasterRecord { public int Id { get; set; } public int MasterType { get; set; } public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }

    public class OfferRecord
    {
        public int Id { get; set; } public string? OfferNo { get; set; } public DateTime? OfferDate { get; set; }
        public string? OfferType { get; set; } public string? ClientName { get; set; } public string? SupplierName { get; set; }
        public string? Currency { get; set; } public string? CallOffNumber { get; set; } public DateTime? PostDate { get; set; }
        public string? MainFrameAgreement { get; set; } public string? WorkOrderNo { get; set; } public string? ContractType { get; set; }
        public string? RecruitmentType { get; set; } public string? NationalityType { get; set; } public string? EmploymentStatus { get; set; }
        public string? PersonnelName { get; set; } public string? PositionTitle { get; set; }
        public string? AssignmentType { get; set; } public string? VisaStatus { get; set; } public DateTime? CommencementDate { get; set; }
        public DateTime? EndDate { get; set; } public decimal? DurationMonths { get; set; }
        public List<OfferDutyRow> Duties { get; set; } = new(); public string? DutiesComments { get; set; }
        public List<OfferSalaryRow> SalaryBreakdown { get; set; } = new(); public string? SalaryBreakdownComments { get; set; }
        public List<OfferBenefitRow> Benefits { get; set; } = new();
        public string? WorkSchedule { get; set; } public bool TimesheetRequired { get; set; } public string? EndOfServiceType { get; set; }
        public string? LeaveEntitlement { get; set; } public string? ProbationPeriod { get; set; } public string? NoticePeriod { get; set; }
        public bool OvertimeApplicable { get; set; } public string? OvertimeRemarks { get; set; } public string? AnnualLeaveDays { get; set; }
        public decimal? WeeklyWorkingHours { get; set; } public decimal? WorkingDaysPerWeek { get; set; }
        public bool MedicalInsuranceApplicable { get; set; } public DateTime? CoverageStartDate { get; set; }
        public string? CoveragePerson { get; set; } public int? NoOfPersonCovered { get; set; }
        public List<OfferMedicalRow> MedicalInsuranceRows { get; set; } = new(); public string? MedicalInsuranceComments { get; set; }
        public bool MobilizationTicket { get; set; } public bool DemobilizationTicket { get; set; } public bool AnnualLeaveTravelAllowance { get; set; }
        public List<OfferTravelRow> TravelBenefits { get; set; } = new(); public string? TravelBenefitsComments { get; set; }
        public List<OfferComplianceRow> ComplianceConditions { get; set; } = new(); public string? ComplianceComments { get; set; }
        public string? AdditionalTerms { get; set; } public string Status { get; set; } = "Pending";
        public string CreatedBy { get; set; } = "admin"; public DateTime? CreatedDateTime { get; set; }
        public string? ModifiedBy { get; set; } public DateTime? ModifiedDateTime { get; set; }
    }
    public class OfferDutyRow { public bool Main { get; set; } public bool Sub { get; set; } public string? Description { get; set; } }
    public class OfferSalaryRow { public string? Component { get; set; } public decimal? Amount { get; set; } }
    public class OfferBenefitRow { public string? BenefitType { get; set; } public decimal? Amount { get; set; } public string? Frequency { get; set; } }
    public class OfferMedicalRow { public string? Remarks { get; set; } }
    public class OfferTravelRow { public string? Covered { get; set; } public string? TravelClass { get; set; } public string? TravelType { get; set; } public int? NoOfTickets { get; set; } public int? ServiceDays { get; set; } }
    public class OfferComplianceRow { public string? AdditionalInfo { get; set; } public bool Required { get; set; } }
}
