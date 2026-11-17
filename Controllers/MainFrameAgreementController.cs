using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;

namespace HCMSys.Controllers
{
    [Route("Master/MainFrameAgreement")]
    public class MainFrameAgreementController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public MainFrameAgreementController(IWebHostEnvironment env) => _env = env;

        private string DataDir => Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "data", "MainFrameAgreement");
        private string ConfigPath => Path.Combine(DataDir, "main-frame-agreement-config.json");
        private string LookupPath => Path.Combine(DataDir, "main-frame-agreement-lookups.json");
        private string RecordsPath => Path.Combine(DataDir, "main-frame-agreement-data.json");

        private async Task<JsonObject> ReadObject(string path, JsonObject? fallback = null)
        {
            Directory.CreateDirectory(DataDir);
            if (!System.IO.File.Exists(path))
            {
                var value = fallback ?? new JsonObject();
                await System.IO.File.WriteAllTextAsync(path, value.ToJsonString(_json));
                return (JsonObject)value.DeepClone();
            }
            return JsonNode.Parse(await System.IO.File.ReadAllTextAsync(path)) as JsonObject ?? new JsonObject();
        }

        private async Task WriteObject(string path, JsonObject value)
        {
            Directory.CreateDirectory(DataDir);
            await System.IO.File.WriteAllTextAsync(path, value.ToJsonString(_json));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(int? id = null)
        {
            var config = await ReadObject(ConfigPath);
            var lookups = await ReadObject(LookupPath);
            var store = await ReadObject(RecordsPath, new JsonObject { ["lastId"] = 0, ["records"] = new JsonArray() });
            ViewBag.ConfigJson = config.ToJsonString();
            ViewBag.LookupsJson = lookups.ToJsonString();
            ViewBag.RecordsJson = store["records"]?.ToJsonString() ?? "[]";
            ViewBag.RecordId = id ?? 0;
            ViewData["Title"] = config["page"]?["name"]?.GetValue<string>() ?? "Main Frame Agreement";
            return View("~/Views/Master/MainFrameAgreement.cshtml");
        }

        [HttpGet("Get/{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var store = await ReadObject(RecordsPath, new JsonObject { ["lastId"] = 0, ["records"] = new JsonArray() });
            var records = store["records"] as JsonArray ?? new JsonArray();
            var record = records.FirstOrDefault(x => (int?)x?["id"] == id);
            return record == null ? NotFound(new { message = "Record not found." }) : Json(record);
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] JsonObject model)
        {
            var store = await ReadObject(RecordsPath, new JsonObject { ["lastId"] = 0, ["records"] = new JsonArray() });
            var records = store["records"] as JsonArray ?? new JsonArray();
            var id = (int?)model["id"] ?? 0;
            var reference = (string?)model["contractReference"] ?? "";

            var config = await ReadObject(ConfigPath);
            var uniqueReference = config["rules"]?["uniqueContractReference"]?.GetValue<bool>() ?? true;
            if (uniqueReference && !string.IsNullOrWhiteSpace(reference))
            {
                var duplicate = records.Any(x => (int?)x?["id"] != id &&
                    string.Equals((string?)x?["contractReference"], reference, StringComparison.OrdinalIgnoreCase));
                if (duplicate) return BadRequest(new { message = "Contract Reference must be unique." });
            }

            var start = (string?)model["startDate"];
            var end = (string?)model["endDate"];
            if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end) &&
                DateTime.TryParse(start, out var sd) && DateTime.TryParse(end, out var ed) && ed < sd)
                return BadRequest(new { message = "End Date cannot be earlier than Start Date." });

            var matchExpiry = config["rules"]?["contractEndDateMustMatchTrackedAttachmentExpiry"]?.GetValue<bool>() ?? true;
            if (matchExpiry && model["attachments"] is JsonArray attachments && !string.IsNullOrWhiteSpace(end))
            {
                var trackedExpiryDates = attachments
                    .Where(a => (bool?)a?["trackExpiry"] == true)
                    .Select(a => (string?)a?["expiryDate"])
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if (trackedExpiryDates.Any(x => !string.Equals(x, end, StringComparison.Ordinal)))
                    return BadRequest(new { message = "The Contract End Date must match the expiry date of each attachment with Track Expiry enabled." });
            }

            var currency = (string?)model["currency"] ?? "";
            var decimals = currency.Equals("OMR", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (decimal.TryParse((string?)model["contractValue"], out var amount))
                model["contractValue"] = Math.Round(amount, decimals, MidpointRounding.AwayFromZero).ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);

            if (id <= 0)
            {
                var lastId = (int?)store["lastId"] ?? 0;
                id = lastId + 1;
                store["lastId"] = id;
                model["id"] = id;
                model["documentNo"] ??= $"MFA-{id:00000}";
                model["status"] ??= "Draft";
                model["createdAt"] = DateTime.UtcNow.ToString("O");
                records.Add(model.DeepClone());
            }
            else
            {
                var index = -1;
                for (var i = 0; i < records.Count; i++)
                    if ((int?)records[i]?["id"] == id) { index = i; break; }
                if (index < 0) return NotFound(new { message = "Record not found." });
                model["updatedAt"] = DateTime.UtcNow.ToString("O");
                records[index] = model.DeepClone();
            }

            await WriteObject(RecordsPath, store);
            return Json(new { success = true, id, documentNo = (string?)model["documentNo"], message = "Main Frame Agreement saved successfully." });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var store = await ReadObject(RecordsPath, new JsonObject { ["lastId"] = 0, ["records"] = new JsonArray() });
            var records = store["records"] as JsonArray ?? new JsonArray();
            var record = records.FirstOrDefault(x => (int?)x?["id"] == id);
            if (record == null) return NotFound(new { message = "Record not found." });
            records.Remove(record);
            await WriteObject(RecordsPath, store);
            return Json(new { success = true, message = "Record deleted." });
        }

        [HttpPost("Status/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Status(int id, [FromBody] JsonObject payload)
        {
            var action = (string?)payload["action"] ?? "";
            var status = action.ToLowerInvariant() switch
            {
                "approve" => "Approved",
                "reject" => "Rejected",
                "unpost" => "Draft",
                _ => null
            };
            if (status == null) return BadRequest(new { message = "Unsupported status action." });
            var store = await ReadObject(RecordsPath, new JsonObject { ["lastId"] = 0, ["records"] = new JsonArray() });
            var records = store["records"] as JsonArray ?? new JsonArray();
            var record = records.FirstOrDefault(x => (int?)x?["id"] == id) as JsonObject;
            if (record == null) return NotFound(new { message = "Record not found." });
            record["status"] = status;
            record["updatedAt"] = DateTime.UtcNow.ToString("O");
            await WriteObject(RecordsPath, store);
            return Json(new { success = true, status, message = $"Record status changed to {status}." });
        }

        [HttpPost("UploadAttachment")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<IActionResult> UploadAttachment(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose a file to upload." });

            var allowed = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(extension))
                return BadRequest(new { message = "This file type is not allowed." });

            var uploadDir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", "MainFrameAgreement");
            Directory.CreateDirectory(uploadDir);
            var safeName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadDir, safeName);
            await using (var stream = new FileStream(fullPath, FileMode.CreateNew))
                await file.CopyToAsync(stream);

            return Json(new
            {
                fileName = Path.GetFileName(file.FileName),
                filePath = Url.Content($"~/uploads/MainFrameAgreement/{safeName}")
            });
        }
    }
}