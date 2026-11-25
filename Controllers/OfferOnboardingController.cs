
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers.OfferOnboardingModule
{
    [Route("Master/OfferOnboarding")]
    public class OfferOnboardingController : Controller
    {
        private readonly IWebHostEnvironment _env;

        private readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public OfferOnboardingController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string Dir => Path.Combine(
            _env.WebRootPath, "data", "OfferOnboarding");

        private string LookupDir => Path.Combine(Dir, "lookups");

        private string ConfigPath =>
            Path.Combine(Dir, "offer-onboarding-config.json");

        private string IndexConfigPath =>
            Path.Combine(Dir, "offer-onboarding-index-config.json");

        private string RecordsPath =>
            Path.Combine(Dir, "offer-onboarding-records.json");

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

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            await System.IO.File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(data, _json));
        }

        private Task<OAConfig> Config() =>
            Read(ConfigPath, new OAConfig());

        private Task<List<OARecord>> Records() =>
            Read(RecordsPath, new List<OARecord>());

        private async Task<List<JsonObject>> Lookup(string file) =>
            await Read(LookupPath(file), new List<JsonObject>());

        private static string Text(JsonObject? obj, string key) =>
            obj?[key]?.ToString() ?? "";

        private async Task<Dictionary<string, List<OAOption>>> LoadLookups(
            OAConfig config)
        {
            var result = new Dictionary<string, List<OAOption>>(
                StringComparer.OrdinalIgnoreCase);

            var sources = config.Sections
                .SelectMany(x => x.Fields)
                .Where(x => !string.IsNullOrWhiteSpace(x.Source))
                .Select(x => x.Source!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                var rows = await Lookup(source);

                result[source] = rows.Select(x => new OAOption
                {
                    Value = Text(x, "value"),
                    Text = Text(x, "text")
                }).ToList();
            }

            return result;
        }

        private async Task PrepareForm(OARecord model)
        {
            var config = await Config();

            ViewBag.Config = config;
            ViewBag.Lookups = await LoadLookups(config);
            ViewBag.IsNew = model.Id == 0;
            ViewBag.IsReadOnly = model.Id != 0;
        }

        // ---------------- FORM ----------------

        [HttpGet("")]
        [HttpGet("Form")]
        public async Task<IActionResult> Form(int? id, bool clone = false)
        {
            var records = await Records();
            OARecord model;

            if (id.HasValue)
            {
                var existing = records.FirstOrDefault(x => x.Id == id.Value);

                if (existing == null)
                    return NotFound();

                model = JsonSerializer.Deserialize<OARecord>(
                    JsonSerializer.Serialize(existing, _json), _json
                ) ?? new OARecord();

                if (clone)
                {
                    model.Id = 0;
                    model.DocNo = "";
                    model.Status = "Pending";
                    model.CreatedBy = "";
                    model.CreatedDateTime = null;
                    model.ModifiedBy = null;
                    model.ModifiedDateTime = null;
                }
            }
            else
            {
                model = new OARecord
                {
                    Date = DateTime.Today,
                    Comparison = new List<OAComparisonRow>()
                };
            }

            await PrepareForm(model);

            return View("~/Views/Master/OfferOnboarding.cshtml", model);
        }

        // ---------------- SAVE ----------------

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(OARecord model)
        {
            var records = await Records();

            model.Comparison ??= new List<OAComparisonRow>();

            if (model.Id == 0)
            {
                model.Id = records.Count == 0
                    ? 1
                    : records.Max(x => x.Id) + 1;

                model.DocNo = $"OA-{model.Id:00000}";
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

            TempData["Success"] = "Offer Acceptance / On Boarding record saved.";

            return RedirectToAction(nameof(Form), new { id = model.Id });
        }

        // ---------------- DELETE ----------------

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var records = await Records();
            records.RemoveAll(x => x.Id == id);

            await Write(RecordsPath, records);

            TempData["Success"] = "Record deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ---------------- STATUS ACTIONS ----------------

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

            TempData["Success"] = $"Status changed to {status}.";
            return RedirectToAction(nameof(Form), new { id });
        }

        // ---------------- AUTO-FILL ----------------

        [HttpGet("ClientByAgreement")]
        public async Task<IActionResult> ClientByAgreement(string value)
        {
            var agreements = await Lookup("main-frame-agreements.json");

            var agreement = agreements.FirstOrDefault(x =>
                Text(x, "value") == value);

            if (agreement == null)
                return NotFound();

            return Json(new
            {
                value = Text(agreement, "client"),
                text = Text(agreement, "clientName")
            });
        }

        [HttpGet("OfferDetails")]
        public async Task<IActionResult> OfferDetails(string value)
        {
            var offers = await Lookup("offers.json");

            var offer = offers.FirstOrDefault(x =>
                Text(x, "value") == value);

            if (offer == null)
                return NotFound();

            return Json(new
            {
                callOffType = Text(offer, "callOffType"),
                candidate = Text(offer, "candidate"),
                employee = Text(offer, "employee")
            });
        }

        // ---------------- OFFER ACCEPTANCE ----------------

        [HttpPost("OfferAcceptance/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OfferAcceptance(int id)
        {
            var records = await Records();
            var record = records.FirstOrDefault(x => x.Id == id);

            if (record == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(record.OfferNo))
            {
                TempData["Error"] = "Select an Offer No first.";
                return RedirectToAction(nameof(Form), new { id });
            }

            var offers = await Lookup("offers.json");

            var selected = offers.FirstOrDefault(x =>
                Text(x, "value") == record.OfferNo);

            if (selected == null)
                return NotFound();

            var callOff = Text(selected, "callOff");

            foreach (var offer in offers)
            {
                if (Text(offer, "callOff") == callOff)
                {
                    offer["status"] =
                        Text(offer, "value") == record.OfferNo
                            ? "Accepted"
                            : "Disabled";
                }
            }

            await Write(LookupPath("offers.json"), offers);

            record.Status = "Accepted";
            record.ModifiedBy = "admin";
            record.ModifiedDateTime = DateTime.Now;

            await Write(RecordsPath, records);

            TempData["Success"] =
                "Selected Offer accepted. Other offers for this Call Off were disabled.";

            return RedirectToAction(nameof(Form), new { id });
        }

        // ---------------- ON BOARDING ----------------

        [HttpPost("OnBoarding/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnBoarding(int id)
        {
            var records = await Records();
            var record = records.FirstOrDefault(x => x.Id == id);

            if (record == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(record.Candidate))
            {
                TempData["Error"] = "Select a Candidate first.";
                return RedirectToAction(nameof(Form), new { id });
            }

            var candidates = await Lookup("candidates.json");

            var candidate = candidates.FirstOrDefault(x =>
                Text(x, "value") == record.Candidate);

            if (candidate == null)
                return NotFound();

            var employees = await Lookup("employees.json");

            var employee = employees.FirstOrDefault(x =>
                Text(x, "candidateId") == record.Candidate);

            if (employee == null)
            {
                var number = employees.Count + 1;
                var employeeId = $"EMP-{number:000}";

                employee = new JsonObject
                {
                    ["value"] = employeeId,
                    ["text"] = Text(candidate, "name"),
                    ["name"] = Text(candidate, "name"),
                    ["dob"] = Text(candidate, "dob"),
                    ["candidateId"] = record.Candidate
                };

                employees.Add(employee);
                await Write(LookupPath("employees.json"), employees);
            }

            record.Employee = Text(employee, "value");
            record.Status = "On Boarding";
            record.ModifiedBy = "admin";
            record.ModifiedDateTime = DateTime.Now;

            await Write(RecordsPath, records);

            TempData["Success"] =
                "Employee lookup record created or updated from Candidate.";

            return RedirectToAction(nameof(Form), new { id });
        }

        // ---------------- RAISE AGREEMENT ----------------

        [HttpPost("RaiseAgreement/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RaiseAgreement(int id)
        {
            var records = await Records();
            var record = records.FirstOrDefault(x => x.Id == id);

            if (record == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(record.OfferNo))
            {
                TempData["Error"] = "Select an Offer No first.";
                return RedirectToAction(nameof(Form), new { id });
            }

            var offers = await Lookup("offers.json");

            var selected = offers.FirstOrDefault(x =>
                Text(x, "value") == record.OfferNo);

            if (selected == null)
                return NotFound();

            selected["status"] = "Active";

            var callOffValue = Text(selected, "callOff");
            var callOffs = await Lookup("call-offs.json");

            foreach (var callOff in callOffs)
            {
                if (Text(callOff, "value") == callOffValue)
                    callOff["isClosed"] = true;
            }

            await Write(LookupPath("offers.json"), offers);
            await Write(LookupPath("call-offs.json"), callOffs);

            record.Status = "Agreement Raised";
            record.ModifiedBy = "admin";
            record.ModifiedDateTime = DateTime.Now;

            await Write(RecordsPath, records);

            TempData["Success"] =
                "Call Off Agreement raised. The Call Off is closed for new offers.";

            return RedirectToAction(nameof(Form), new { id });
        }

        // ---------------- INDEX ----------------



        [HttpGet("/Master/OfferOnboardingIndex")]
        public async Task<IActionResult> Index()
        {
            // Load the dedicated Index JSON configuration.
            var config = await Read(
                IndexConfigPath,
                new JsonObject());

            // Load saved Offer Onboarding records.
            var records = await Records();

            // Pass configuration and records to the Index view.
            ViewData["Config"] = config;
            ViewData["Rows"] = JsonSerializer.SerializeToNode(records)
                               as JsonArray ?? new JsonArray();

            return View("~/Views/Master/OfferOnboardingIndex.cshtml");
        }


    }

    // ---------------- CONFIGURATION MODELS ----------------

    public class OAConfig
    {
        public OAPageInfo Page { get; set; } = new();
        public OAButtonGroup Buttons { get; set; } = new();
        public List<OASection> Sections { get; set; } = new();
        public List<OAGridConfig> Grids { get; set; } = new();
    }

    public class OAPageInfo
    {
        public string Title { get; set; } = "";
        public int PageId { get; set; }
        public string Icon { get; set; } = "";
    }

    public class OAButtonGroup
    {
        public List<OAButton> Form { get; set; } = new();
    }

    public class OAButton
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Style { get; set; } = "secondary";
        public string Action { get; set; } = "";
        public bool Visible { get; set; } = true;
    }

    public class OASection
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public List<OAField> Fields { get; set; } = new();
    }

    public class OAField
    {
        public string Field { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";
        public string? Source { get; set; }
        public bool Required { get; set; }
        public bool Readonly { get; set; }
        public int? Maxlength { get; set; }
        public JsonObject? AutoFill { get; set; }
        public string? FilterBy { get; set; }
    }

    public class OAGridConfig
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Property { get; set; } = "";
        public List<OAGridColumn> Columns { get; set; } = new();
        public List<OAComparisonRow> DefaultRows { get; set; } = new();
    }

    public class OAGridColumn
    {
        public string Field { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";
        public bool Readonly { get; set; } = true;
    }

    public class OAOption
    {
        public string Value { get; set; } = "";
        public string Text { get; set; } = "";
    }

    // ---------------- RECORD MODELS ----------------

    public class OARecord
    {
        public int Id { get; set; }
        public string? DocNo { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? PostDate { get; set; }
        public string? MainFrameAgreement { get; set; }
        public string? Client { get; set; }
        public string? CallOff { get; set; }
        public string? OfferNo { get; set; }
        public string? CallOffType { get; set; }
        public string? Candidate { get; set; }
        public string? Employee { get; set; }

        public List<OAComparisonRow> Comparison { get; set; } = new();

        public string Status { get; set; } = "Pending";
        public string? CreatedBy { get; set; }
        public DateTime? CreatedDateTime { get; set; }
        public string? ModifiedBy { get; set; }
        public DateTime? ModifiedDateTime { get; set; }
    }

    public class OAComparisonRow
    {
        public string? CandidateField { get; set; }
        public string? CandidateValue { get; set; }
        public string? EmployeeField { get; set; }
        public string? EmployeeValue { get; set; }
    }
}
