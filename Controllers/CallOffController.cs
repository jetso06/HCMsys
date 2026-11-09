using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/CallOff")]
    public class CallOffController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public CallOffController(IWebHostEnvironment env) => _env = env;

        private string Dir => Path.Combine(_env.WebRootPath, "data", "CallOff");
        private string ConfigPath => Path.Combine(Dir, "calloff-config.json");
        private string RecordsPath => Path.Combine(Dir, "calloffs.json");

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

        private Task<CallOffPageConfig> Config() => Read(ConfigPath, new CallOffPageConfig());
        private Task<List<CallOffRecord>> Records() => Read(RecordsPath, new List<CallOffRecord>());

        private async Task<Dictionary<string, List<CallOffOption>>> LoadLookups(CallOffPageConfig config)
        {
            var result = new Dictionary<string, List<CallOffOption>>(StringComparer.OrdinalIgnoreCase);
            var sources = config.Sections.SelectMany(x => x.Fields)
                .Concat(config.Grids.SelectMany(x => x.Columns))
                .Where(x => !string.IsNullOrWhiteSpace(x.Source))
                .Select(x => x.Source!).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                var path = Path.Combine(Dir, Path.GetFileName(source));
                var options = new List<CallOffOption>();
                if (System.IO.File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var text = item.GetString() ?? "";
                                options.Add(new CallOffOption { Value = text, Text = text });
                            }
                            else if (item.ValueKind == JsonValueKind.Object)
                            {
                                var value = item.TryGetProperty("value", out var v) ? v.ToString()
                                    : item.TryGetProperty("id", out v) ? v.ToString() : "";
                                var text = item.TryGetProperty("text", out var t) ? t.ToString()
                                    : item.TryGetProperty("name", out t) ? t.ToString() : value;
                                options.Add(new CallOffOption { Value = value, Text = text });
                            }
                        }
                    }
                }
                result[source] = options;
            }

            // These four lists are sourced from the existing Classification master.
            var classificationDir = Path.Combine(_env.WebRootPath, "data", "Classification");
            var typePath = Path.Combine(classificationDir, "classification-types.json");
            var recordPath = Path.Combine(classificationDir, "classifications.json");
            if (System.IO.File.Exists(typePath) && System.IO.File.Exists(recordPath))
            {
                var types = await Read(typePath, new List<CallOffMasterType>());
                var records = await Read(recordPath, new List<CallOffMasterRecord>());
                AddMasterOptions("billing-categories.json", "Billing Category", types, records, result);
                AddMasterOptions("salary-categories.json", "Salary Category", types, records, result);
                AddMasterOptions("additional-checks.json", "AdditionalChecks", types, records, result);
                AddMasterOptions("benefit-types.json", "Benefit", types, records, result);
            }
            return result;
        }

        private static void AddMasterOptions(string source, string masterName,
            List<CallOffMasterType> types, List<CallOffMasterRecord> records,
            Dictionary<string, List<CallOffOption>> result)
        {
            var type = types.FirstOrDefault(x => x.Name.Equals(masterName, StringComparison.OrdinalIgnoreCase));
            if (type == null) return;
            result[source] = records.Where(x => x.MasterType == type.Id && x.IsActive)
                .Select(x => new CallOffOption { Value = x.Id.ToString(), Text = x.Name }).ToList();
        }

        [HttpGet("")]
        [HttpGet("Form")]
        public async Task<IActionResult> Form(int? id, bool clone = false)
        {
            var config = await Config();
            var records = await Records();
            var existing = id.HasValue ? records.FirstOrDefault(x => x.Id == id.Value) : null;
            if (id.HasValue && existing == null) return NotFound();

            var model = existing == null ? new CallOffRecord() : Copy(existing);
            if (clone) { model.Id = 0; model.DocNo = ""; model.CallOffNumber = ""; model.Status = "Pending"; }
            ViewBag.Config = config;
            ViewBag.Lookups = await LoadLookups(config);
            ViewBag.IsNew = model.Id == 0;
            ViewBag.IsReadOnly = existing != null && !clone;
            return View("~/Views/Master/CallOff.cshtml", model);
        }

        private static CallOffRecord Copy(CallOffRecord x) => new()
        {
            Id=x.Id, DocNo=x.DocNo, DocDate=x.DocDate, CallOffNumber=x.CallOffNumber,
            AgreementReferenceDate=x.AgreementReferenceDate, ClientName=x.ClientName, SupplierName=x.SupplierName,
            Currency=x.Currency, ContractNumberReference=x.ContractNumberReference, ContractTitleReference=x.ContractTitleReference,
            PostDate=x.PostDate, MainFrameAgreement=x.MainFrameAgreement, WorkOrderNo=x.WorkOrderNo, ContractType=x.ContractType,
            PersonnelName=x.PersonnelName, PositionTitle=x.PositionTitle, PointOfOrigin=x.PointOfOrigin,
            ReportingTo=x.ReportingTo, WorkBaseLocation=x.WorkBaseLocation, AssignmentType=x.AssignmentType,
            VisaStatus=x.VisaStatus, CommencementDate=x.CommencementDate, EndDate=x.EndDate, DurationMonths=x.DurationMonths,
            WorkSchedule=x.WorkSchedule, TimesheetRequired=x.TimesheetRequired, EndOfServiceType=x.EndOfServiceType,
            LeaveEntitlement=x.LeaveEntitlement, ProbationPeriod=x.ProbationPeriod, NoticePeriod=x.NoticePeriod,
            OvertimeApplicable=x.OvertimeApplicable, OvertimeRemarks=x.OvertimeRemarks, AdditionalTerms=x.AdditionalTerms,
            BillingHeads=x.BillingHeads.Select(y=>new BillingHeadRow(y)).ToList(),
            Duties=x.Duties.Select(y=>new DutyRow(y)).ToList(),
            AdditionalInfo=x.AdditionalInfo.Select(y=>new AdditionalInfoRow(y)).ToList(),
            SalaryBreakdown=x.SalaryBreakdown.Select(y=>new SalaryRow(y)).ToList(),
            Benefits=x.Benefits.Select(y=>new BenefitRow(y)).ToList(),
            Status=x.Status, CreatedBy=x.CreatedBy, CreatedDateTime=x.CreatedDateTime,
            ModifiedBy=x.ModifiedBy, ModifiedDateTime=x.ModifiedDateTime
        };

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(CallOffRecord model)
        {
            model.BillingHeads ??= new(); model.Duties ??= new(); model.AdditionalInfo ??= new();
            model.SalaryBreakdown ??= new(); model.Benefits ??= new();
            var records = await Records();
            if (model.Id == 0)
            {
                model.Id = records.Count == 0 ? 1 : records.Max(x => x.Id) + 1;
                model.DocNo = string.IsNullOrWhiteSpace(model.DocNo) ? $"CO-{model.Id:00000}" : model.DocNo;
                model.CallOffNumber = string.IsNullOrWhiteSpace(model.CallOffNumber) ? model.DocNo : model.CallOffNumber;
                model.CreatedBy = "admin"; model.CreatedDateTime = DateTime.Now; model.Status = "Pending";
                records.Add(model);
            }
            else
            {
                var old = records.FirstOrDefault(x => x.Id == model.Id);
                if (old == null) return NotFound();
                model.DocNo = old.DocNo; model.CallOffNumber = old.CallOffNumber;
                model.CreatedBy = old.CreatedBy; model.CreatedDateTime = old.CreatedDateTime;
                model.ModifiedBy = "admin"; model.ModifiedDateTime = DateTime.Now; model.Status = old.Status;
                records[records.IndexOf(old)] = model;
            }
            await Write(RecordsPath, records);
            TempData["Success"] = "CallOff saved successfully.";
            return RedirectToAction(nameof(Form), new { id = model.Id });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rows = await Records(); rows.RemoveAll(x => x.Id == id); await Write(RecordsPath, rows);
            TempData["Success"] = "CallOff deleted."; return RedirectToAction(nameof(Form));
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
            TempData["Success"] = $"CallOff status changed to {status}.";
            return RedirectToAction(nameof(Form), new { id });
        }
    }

    public class CallOffPageConfig { public CallOffPageInfo Page {get;set;}=new(); public CallOffButtonGroup Buttons {get;set;}=new(); public List<CallOffSection> Sections {get;set;}=new(); public List<CallOffGridConfig> Grids {get;set;}=new(); }
    public class CallOffPageInfo { public string Title {get;set;}="Call Off"; public int PageId {get;set;}=401; public string Icon {get;set;}="fa-file-contract"; }
    public class CallOffButtonGroup { public List<CallOffButton> Form {get;set;}=new(); }
    public class CallOffButton { public string Id {get;set;}=""; public string Label {get;set;}=""; public string Icon {get;set;}=""; public string Style {get;set;}="secondary"; public string Action {get;set;}=""; public bool Visible {get;set;}=true; }
    public class CallOffSection { public string Id {get;set;}=""; public string Title {get;set;}=""; public List<CallOffField> Fields {get;set;}=new(); }
    public class CallOffField { public string Field {get;set;}=""; public string Label {get;set;}=""; public string Type {get;set;}="text"; public string? Source {get;set;} public string? Remarks {get;set;} public bool Required {get;set;} public bool Readonly {get;set;} public string? Step {get;set;} }
    public class CallOffGridConfig { public string Id {get;set;}=""; public string Title {get;set;}=""; public string Subtitle {get;set;}=""; public string Property {get;set;}=""; public List<CallOffField> Columns {get;set;}=new(); }
    public class CallOffOption { public string Value {get;set;}=""; public string Text {get;set;}=""; }
    public class CallOffMasterType { public int Id {get;set;} public string Name {get;set;}=""; }
    public class CallOffMasterRecord { public int Id {get;set;} public int MasterType {get;set;} public string Name {get;set;}=""; public bool IsActive {get;set;}=true; }

    public class CallOffRecord
    {
        public int Id {get;set;} public string? DocNo {get;set;} public DateTime? DocDate {get;set;}
        public string? CallOffNumber {get;set;} public DateTime? AgreementReferenceDate {get;set;}
        public string? ClientName {get;set;} public string? SupplierName {get;set;} public string? Currency {get;set;}
        public string? ContractNumberReference {get;set;} public string? ContractTitleReference {get;set;}
        public DateTime? PostDate {get;set;} public string? MainFrameAgreement {get;set;} public string? WorkOrderNo {get;set;}
        public string? ContractType {get;set;} public string? PersonnelName {get;set;} public string? PositionTitle {get;set;}
        public string? PointOfOrigin {get;set;} public string? ReportingTo {get;set;} public string? WorkBaseLocation {get;set;}
        public string? AssignmentType {get;set;} public string? VisaStatus {get;set;} public DateTime? CommencementDate {get;set;}
        public DateTime? EndDate {get;set;} public decimal? DurationMonths {get;set;} public string? WorkSchedule {get;set;}
        public bool TimesheetRequired {get;set;} public string? EndOfServiceType {get;set;} public string? LeaveEntitlement {get;set;}
        public string? ProbationPeriod {get;set;} public string? NoticePeriod {get;set;} public bool OvertimeApplicable {get;set;}
        public string? OvertimeRemarks {get;set;} public string? AdditionalTerms {get;set;}
        public List<BillingHeadRow> BillingHeads {get;set;}=new(); public List<DutyRow> Duties {get;set;}=new();
        public List<AdditionalInfoRow> AdditionalInfo {get;set;}=new(); public List<SalaryRow> SalaryBreakdown {get;set;}=new();
        public List<BenefitRow> Benefits {get;set;}=new(); public string Status {get;set;}="Pending";
        public string CreatedBy {get;set;}="admin"; public DateTime? CreatedDateTime {get;set;}
        public string? ModifiedBy {get;set;} public DateTime? ModifiedDateTime {get;set;}
    }
    public class BillingHeadRow { public BillingHeadRow(){} public BillingHeadRow(BillingHeadRow x){BillingHead=x.BillingHead;DenominationType=x.DenominationType;Amount=x.Amount;ServiceFeeType=x.ServiceFeeType;ServiceFeeValue=x.ServiceFeeValue;TaxPercent=x.TaxPercent;Qty=x.Qty;Remarks=x.Remarks;} public string? BillingHead {get;set;} public string? DenominationType {get;set;} public decimal? Amount {get;set;} public string? ServiceFeeType {get;set;} public decimal? ServiceFeeValue {get;set;} public decimal? TaxPercent {get;set;} public decimal? Qty {get;set;} public string? Remarks {get;set;} }
    public class DutyRow { public DutyRow(){} public DutyRow(DutyRow x){Main=x.Main;Sub=x.Sub;Description=x.Description;} public bool Main {get;set;} public bool Sub {get;set;} public string? Description {get;set;} }
    public class AdditionalInfoRow { public AdditionalInfoRow(){} public AdditionalInfoRow(AdditionalInfoRow x){AdditionalInfo=x.AdditionalInfo;Required=x.Required;} public string? AdditionalInfo {get;set;} public bool Required {get;set;} }
    public class SalaryRow { public SalaryRow(){} public SalaryRow(SalaryRow x){Component=x.Component;Amount=x.Amount;} public string? Component {get;set;} public decimal? Amount {get;set;} }
    public class BenefitRow { public BenefitRow(){} public BenefitRow(BenefitRow x){BenefitType=x.BenefitType;Amount=x.Amount;Frequency=x.Frequency;} public string? BenefitType {get;set;} public decimal? Amount {get;set;} public string? Frequency {get;set;} }
}
