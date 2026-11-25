
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/SubContractInvoice")]
    public class SubContractInvoiceController : Controller
    {
        private readonly IWebHostEnvironment _env;

        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public SubContractInvoiceController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string Dir => Path.Combine(
            _env.WebRootPath, "data", "SubContractInvoice");

        private string LookupDir => Path.Combine(Dir, "lookups");

        private string ConfigPath => Path.Combine(
            Dir, "sub-contract-invoice-config.json");

        private string RecordsPath => Path.Combine(
            Dir, "sub-contract-invoice-records.json");

        private string RevisionsPath => Path.Combine(
            Dir, "sub-contract-invoice-revisions.json");

        private string LookupPath(string file) =>
            Path.Combine(LookupDir, Path.GetFileName(file));

        private async Task<T> Read<T>(string path, T fallback)
        {
            if (!System.IO.File.Exists(path))
                return fallback;

            var json = await System.IO.File.ReadAllTextAsync(path);

            return JsonSerializer.Deserialize<T>(json, _json) ?? fallback;
        }

        private async Task Write<T>(string path, T data)
        {
            var folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            await System.IO.File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(data, _json));
        }

        private Task<JsonObject> Config() =>
            Read(ConfigPath, new JsonObject());

        private Task<List<SCInvoiceRecord>> Records() =>
            Read(RecordsPath, new List<SCInvoiceRecord>());

        private Task<List<JsonObject>> Lookup(string file) =>
            Read(LookupPath(file), new List<JsonObject>());

        private static string Text(JsonObject? obj, string key) =>
            obj?[key]?.ToString() ?? "";

        private async Task<Dictionary<string, List<JsonObject>>> LoadLookups(
            JsonObject config)
        {
            var result = new Dictionary<string, List<JsonObject>>(
                StringComparer.OrdinalIgnoreCase);

            var sources = new List<string>();

            if (config["sections"] is JsonArray sections)
            {
                foreach (var section in sections.OfType<JsonObject>())
                {
                    if (section["fields"] is not JsonArray fields)
                        continue;

                    foreach (var field in fields.OfType<JsonObject>())
                    {
                        var source = Text(field, "source");
                        if (!string.IsNullOrWhiteSpace(source))
                            sources.Add(source);
                    }
                }
            }

            if (config["grids"] is JsonArray grids)
            {
                foreach (var grid in grids.OfType<JsonObject>())
                {
                    if (grid["columns"] is not JsonArray columns)
                        continue;

                    foreach (var column in columns.OfType<JsonObject>())
                    {
                        var source = Text(column, "source");
                        if (!string.IsNullOrWhiteSpace(source))
                            sources.Add(source);
                    }
                }
            }

            foreach (var source in sources.Distinct(
                StringComparer.OrdinalIgnoreCase))
            {
                result[source] = await Lookup(source);
            }

            return result;
        }

        private async Task PrepareForm(SCInvoiceRecord model)
        {
            var config = await Config();

            ViewBag.Config = config;
            ViewBag.Lookups = await LoadLookups(config);
            ViewBag.IsNew = model.Id == 0;
            ViewBag.IsReadOnly = false;
        }

        // ---------------- INDEX ----------------

        [HttpGet("/Master/SubContractInvoiceIndex")]
        public async Task<IActionResult> Index()
        {
            var config = await Config();

            JsonArray rows = new JsonArray();

            if (System.IO.File.Exists(RecordsPath))
            {
                var json = await System.IO.File.ReadAllTextAsync(RecordsPath);
                rows = JsonNode.Parse(json) as JsonArray ?? new JsonArray();
            }

            ViewData["Config"] = config;
            ViewData["Rows"] = rows;

            return View("~/Views/Master/SubContractInvoiceIndex.cshtml");
        }

        // ---------------- FORM ----------------

        [HttpGet("")]
        [HttpGet("Form")]
        public async Task<IActionResult> Form(int? id, bool clone = false)
        {
            var records = await Records();
            SCInvoiceRecord model;

            if (id.HasValue)
            {
                var existing = records.FirstOrDefault(x => x.Id == id.Value);

                if (existing == null)
                    return NotFound();

                model = JsonSerializer.Deserialize<SCInvoiceRecord>(
                    JsonSerializer.Serialize(existing, _json), _json
                ) ?? new SCInvoiceRecord();

                if (clone)
                {
                    model.Id = 0;
                    model.DocNo = "";
                    model.Status = "Pending";
                    model.CreatedBy = null;
                    model.CreatedDateTime = null;
                    model.ModifiedBy = null;
                    model.ModifiedDateTime = null;
                }
            }
            else
            {
                model = new SCInvoiceRecord
                {
                    DocDate = DateTime.Today,
                    ExRate = 1,
                    Attendance = new List<SCInvoiceAttendance>()
                };
            }

            await PrepareForm(model);

            return View("~/Views/Master/SubContractInvoice.cshtml", model);
        }

        // ---------------- SAVE ----------------

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(SCInvoiceRecord model)
        {
            var records = await Records();

            model.Attendance ??= new List<SCInvoiceAttendance>();

            foreach (var row in model.Attendance)
            {
                row.Amount = row.Qty * row.Rate;
                row.TaxAmount = row.Amount * row.TaxPercent / 100;
                row.NetAmount = row.Amount + row.TaxAmount;
            }

            if (model.Id == 0)
            {
                model.Id = records.Count == 0
                    ? 1
                    : records.Max(x => x.Id) + 1;

                model.DocNo = $"SCI-{model.Id:00000}";
                model.Status = "Pending";
                model.CreatedBy = "admin";
                model.CreatedDateTime = DateTime.Now;

                records.Add(model);
            }
            else
            {
                var old = records.FirstOrDefault(x => x.Id == model.Id);

                if (old == null)
                    return NotFound();

                model.DocNo = old.DocNo;
                model.Status = old.Status;
                model.CreatedBy = old.CreatedBy;
                model.CreatedDateTime = old.CreatedDateTime;
                model.ModifiedBy = "admin";
                model.ModifiedDateTime = DateTime.Now;

                records[records.IndexOf(old)] = model;
            }

            await Write(RecordsPath, records);

            TempData["Success"] = "Sub Contract Invoice saved successfully.";

            return RedirectToAction(nameof(Form), new { id = model.Id });
        }

        // ---------------- SAVE AS ----------------

        [HttpPost("SaveAs/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAs(int id)
        {
            var records = await Records();
            var original = records.FirstOrDefault(x => x.Id == id);

            if (original == null)
                return NotFound();

            var copy = JsonSerializer.Deserialize<SCInvoiceRecord>(
                JsonSerializer.Serialize(original, _json), _json
            ) ?? new SCInvoiceRecord();

            copy.Id = 0;
            copy.DocNo = "";
            copy.Status = "Pending";
            copy.CreatedBy = null;
            copy.CreatedDateTime = null;
            copy.ModifiedBy = null;
            copy.ModifiedDateTime = null;

            TempData["Success"] = "Copy created. Save it as a new invoice.";

            await PrepareForm(copy);

            return View("~/Views/Master/SubContractInvoice.cshtml", copy);
        }

        // ---------------- DELETE ----------------

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var records = await Records();
            records.RemoveAll(x => x.Id == id);

            await Write(RecordsPath, records);

            TempData["Success"] = "Invoice deleted.";

            return RedirectToAction(nameof(Index));
        }

        // ---------------- STATUS ----------------

        [HttpPost("Approve/{id:int}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Approve(int id) =>
            SetStatus(id, "Approved");

        [HttpPost("Reject/{id:int}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id) =>
            SetStatus(id, "Rejected");

        [HttpPost("UnPost/{id:int}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> UnPost(int id) =>
            SetStatus(id, "Pending");

        private async Task<IActionResult> SetStatus(int id, string status)
        {
            var records = await Records();
            var record = records.FirstOrDefault(x => x.Id == id);

            if (record == null)
                return NotFound();

            record.Status = status;
            record.ModifiedBy = "admin";
            record.ModifiedDateTime = DateTime.Now;

            await Write(RecordsPath, records);

            TempData["Success"] = $"Invoice status changed to {status}.";

            return RedirectToAction(nameof(Form), new { id });
        }

        // ---------------- REVISIONS ----------------

        [HttpGet("Revisions/{id:int}")]
        public async Task<IActionResult> Revisions(int id)
        {
            var revisions = await Read(
                RevisionsPath,
                new List<SCInvoiceRevision>());

            return Json(revisions.Where(x => x.RecordId == id));
        }

        // ---------------- REVISION TRACKING ----------------

        private async Task SaveRevision(SCInvoiceRecord record)
        {
            var revisions = await Read(
                RevisionsPath,
                new List<SCInvoiceRevision>());

            revisions.Add(new SCInvoiceRevision
            {
                RecordId = record.Id,
                DocNo = record.DocNo ?? "",
                RevisionDate = DateTime.Now,
                ModifiedBy = "admin",
                Snapshot = JsonSerializer.Serialize(record, _json)
            });

            await Write(RevisionsPath, revisions);
        }
    }

    // ---------------- RECORD MODELS ----------------

    public class SCInvoiceRecord
    {
        public int Id { get; set; }
        public string? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? PostDate { get; set; }
        public string? SupplierVendor { get; set; }
        public string? Currency { get; set; }
        public decimal ExRate { get; set; } = 1;
        public string? AttentionPerson { get; set; }
        public string? PaymentTerms { get; set; }
        public string? Comments { get; set; }

        public List<SCInvoiceAttendance> Attendance { get; set; } = new();

        public string? Name { get; set; }
        public string? Code { get; set; }
        public string? RefNo { get; set; }
        public string? Ave { get; set; }
        public string? AppSt { get; set; }

        public string Status { get; set; } = "Pending";
        public string? CreatedBy { get; set; }
        public DateTime? CreatedDateTime { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedDateTime { get; set; }
    }

    public class SCInvoiceAttendance
    {
        public bool Selected { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? EmpCode { get; set; }
        public string? EmpName { get; set; }
        public string? MainFrameAgreement { get; set; }
        public string? CallOff { get; set; }
        public string? ContractType { get; set; }
        public string? BillingHeads { get; set; }
        public string? DenominationType { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public decimal TaxPercent { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string? PONumber { get; set; }
        public DateTime? PODate { get; set; }
        public string? Remarks { get; set; }
    }

    public class SCInvoiceRevision
    {
        public int RecordId { get; set; }
        public string DocNo { get; set; } = "";
        public DateTime RevisionDate { get; set; }
        public string ModifiedBy { get; set; } = "";
        public string Snapshot { get; set; } = "";
    }
}
