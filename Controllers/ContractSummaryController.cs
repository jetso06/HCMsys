using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

namespace HCMSys.Controllers
{
    [Route("Master/ContractSummary")]
    public class ContractSummaryController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        public ContractSummaryController(IWebHostEnvironment env) => _env = env;

        private string Dir => Path.Combine(_env.WebRootPath, "data", "ContractSummary");
        private string ConfigPath => Path.Combine(Dir, "contract-summary-config.json");
        private string LookupsPath => Path.Combine(Dir, "contract-summary-lookups.json");
        private string DataPath => Path.Combine(Dir, "contract-summary-data.json");

        private T Read<T>(string path, T fallback)
        {
            if (!System.IO.File.Exists(path)) return fallback;
            try { return JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), _json) ?? fallback; }
            catch { return fallback; }
        }
        private void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(value, _json));
        }

        [HttpGet("")]
        public IActionResult Index(int? id)
        {
            var cfg = Read(ConfigPath, new ContractSummaryConfig());
            var lookups = Read(LookupsPath, new Dictionary<string, List<LookupOption>>());
            var data = Read(DataPath, new ContractSummaryData());
            var record = data.Records.FirstOrDefault(x => x.Id == id) ?? new ContractSummaryRecord();
            return View("~/Views/Master/ContractSummary.cshtml",
                new ContractSummaryViewModel { Config = cfg, Lookups = lookups, Record = record });
        }

        [HttpGet("EmployeeAgreement/{employeeId:int}")]
        public IActionResult EmployeeAgreement(int employeeId)
        {
            var lookups = Read(LookupsPath, new Dictionary<string, List<LookupOption>>());
            var emp = lookups.GetValueOrDefault("employees")?.FirstOrDefault(x => x.Value == employeeId.ToString());
            if (emp == null) return NotFound(new { success = false });
            return Json(new { success = true, mainFrameAgreement = emp.MainFrameAgreement ?? "" });
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(ContractSummaryRecord record)
        {
            var data = Read(DataPath, new ContractSummaryData());
            if (record.Id <= 0) record.Id = ++data.LastId;
            else data.LastId = Math.Max(data.LastId, record.Id);

            record.BillingHeads = record.BillingHeads.Where(HasBillingData).ToList();
            record.AdditionalInfo = record.AdditionalInfo.Where(x => !string.IsNullOrWhiteSpace(x.AdditionalInfo)).ToList();
            record.SalaryBreakdown = record.SalaryBreakdown.Where(x => !string.IsNullOrWhiteSpace(x.Component)).ToList();
            record.Benefits = record.Benefits.Where(x => !string.IsNullOrWhiteSpace(x.BenefitType)).ToList();

            record.Contracts = record.Contracts
     .Where(x =>
         !string.IsNullOrWhiteSpace(x.CallOffNo) ||
         !string.IsNullOrWhiteSpace(x.RevisionId) ||
         !string.IsNullOrWhiteSpace(x.CallOffAgreementNo) ||
         !string.IsNullOrWhiteSpace(x.ContractStatus) ||
         !string.IsNullOrWhiteSpace(x.CommencementDate) ||
         !string.IsNullOrWhiteSpace(x.EndDate))
     .ToList();

            foreach (var contract in record.Contracts)
            {
                if (DateTime.TryParse(contract.CommencementDate, out var start) &&
                    DateTime.TryParse(contract.EndDate, out var end) && end >= start)
                {
                    contract.DurationMonths =
                        (end.Year - start.Year) * 12 + end.Month - start.Month;
                }
            }

            var index = data.Records.FindIndex(x => x.Id == record.Id);
            if (index >= 0) data.Records[index] = record; else data.Records.Add(record);
            Write(DataPath, data);
            TempData["Success"] = "Contract Summary saved successfully.";
            return RedirectToAction(nameof(Index), new { id = record.Id });
        }

        private static bool HasBillingData(ContractSummaryBillingHeadRow x) =>
    !string.IsNullOrWhiteSpace(x.BillingHead) || x.Amount.HasValue ||
    !string.IsNullOrWhiteSpace(x.Remarks);

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var data = Read(DataPath, new ContractSummaryData());
            data.Records.RemoveAll(x => x.Id == id);
            Write(DataPath, data);
            TempData["Success"] = "Contract Summary deleted.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class ContractSummaryViewModel { public ContractSummaryConfig Config { get; set; } = new(); public Dictionary<string,List<LookupOption>> Lookups { get; set; } = new(); public ContractSummaryRecord Record { get; set; } = new(); }
    public class ContractSummaryConfig { public ContractSummaryPage Page { get; set; } = new(); public List<ContractSummarySection> Sections { get; set; } = new(); public List<ContractSummaryGrid> Grids { get; set; } = new(); public List<ContractSummaryComment> Comments { get; set; } = new(); }
    public class ContractSummaryPage { public string Title { get; set; } = "Contract Summary"; public string Icon { get; set; } = "fa-file-contract"; public int PageId { get; set; } }
    public class ContractSummarySection { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public List<ContractSummaryField> Fields { get; set; } = new(); }
    public class ContractSummaryField { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public string Type { get; set; } = "text"; public string? Source { get; set; } public int MaxLength { get; set; } public bool Required { get; set; } public bool Readonly { get; set; } }
    public class ContractSummaryGrid { public string Id { get; set; } = ""; public string Property { get; set; } = ""; public string Title { get; set; } = ""; public string Subtitle { get; set; } = ""; public List<ContractSummaryColumn> Columns { get; set; } = new(); }
    public class ContractSummaryColumn { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public string Type { get; set; } = "text"; public string? Source { get; set; } public string? Step { get; set; } }
    public class ContractSummaryComment
    {
        public string Field { get; set; } = "";
        public string Label { get; set; } = "";
        public string After { get; set; } = "";
    }
    public class ContractSummaryContractRow
    {
        public string CallOffNo { get; set; } = "";
        public string RevisionId { get; set; } = "";
        public string CallOffAgreementNo { get; set; } = "";
        public string ContractStatus { get; set; } = "";
        public string CommencementDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public int DurationMonths { get; set; }
    }
    public class LookupOption { public string Value { get; set; } = ""; public string Text { get; set; } = ""; public string? MainFrameAgreement { get; set; } }
    public class ContractSummaryData { public int LastId { get; set; } public List<ContractSummaryRecord> Records { get; set; } = new(); }
    public class ContractSummaryRecord {
        public int Id { get; set; } public string EmployeeId { get; set; } = ""; public string MainFrameAgreement { get; set; } = "";
        public List<ContractSummaryContractRow> Contracts { get; set; } =
    Enumerable.Range(0, 5)
        .Select(_ => new ContractSummaryContractRow())
        .ToList();
        public List<ContractSummaryBillingHeadRow> BillingHeads { get; set; } =
    Enumerable.Range(0, 5).Select(_ => new ContractSummaryBillingHeadRow()).ToList();

        public List<ContractSummaryAdditionalInfoRow> AdditionalInfo { get; set; } =
            Enumerable.Range(0, 5).Select(_ => new ContractSummaryAdditionalInfoRow()).ToList();

        public List<ContractSummarySalaryBreakdownRow> SalaryBreakdown { get; set; } =
            Enumerable.Range(0, 5).Select(_ => new ContractSummarySalaryBreakdownRow()).ToList();

        public List<ContractSummaryBenefitRow> Benefits { get; set; } =
            Enumerable.Range(0, 5).Select(_ => new ContractSummaryBenefitRow()).ToList();
        public string ContractDetailsComments { get; set; } = ""; public string OtherDetailsComments { get; set; } = ""; public string SalaryBreakdownComments { get; set; } = ""; public string BenefitsComments { get; set; } = ""; public string AdditionalTerms { get; set; } = "";
    }
    public class ContractSummaryBillingHeadRow
    {
        public string BillingHead { get; set; } = "";
        public string DenominationType { get; set; } = "";
        public decimal? Amount { get; set; }
        public string ServiceFeeType { get; set; } = "";
        public decimal? ServiceFeeValue { get; set; }
        public decimal? TaxPercent { get; set; }
        public decimal? Qty { get; set; } = 1;
        public string Remarks { get; set; } = "";
    }

    public class ContractSummaryAdditionalInfoRow
    {
        public string AdditionalInfo { get; set; } = "";
        public bool Required { get; set; }
    }

    public class ContractSummarySalaryBreakdownRow
    {
        public string Component { get; set; } = "";
        public decimal? Amount { get; set; }
    }

    public class ContractSummaryBenefitRow
    {
        public string BenefitType { get; set; } = "";
        public decimal? Amount { get; set; }
        public string Frequency { get; set; } = "";
    }
}