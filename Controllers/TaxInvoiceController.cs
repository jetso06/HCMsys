using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/TaxInvoice")]
    public class TaxInvoiceController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public TaxInvoiceController(IWebHostEnvironment env) => _env = env;

        private string TaxDir => Path.Combine(_env.WebRootPath, "data", "TaxInvoice");
        private string TsDir => Path.Combine(_env.WebRootPath, "data", "Timesheet");
        private string TaxPath(string file) => Path.Combine(TaxDir, file);
        private string TsPath(string file) => Path.Combine(TsDir, file);

        private JsonObject ReadObject(string path)
        {
            if (!System.IO.File.Exists(path)) return new JsonObject();
            return JsonNode.Parse(System.IO.File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        private JsonArray ReadRecords(string path) => ReadObject(path)["records"] as JsonArray ?? new JsonArray();
        private JsonObject ReadData() => ReadObject(TaxPath("tax-invoice-data.json"));
        private JsonArray Invoices(JsonObject data) => data["invoices"] as JsonArray ?? new JsonArray();
        private void WriteData(JsonObject data)
        {
            Directory.CreateDirectory(TaxDir);
            System.IO.File.WriteAllText(TaxPath("tax-invoice-data.json"), data.ToJsonString(_jsonOptions));
        }
        private JsonObject ReadLookups()
        {
            var lookups = ReadObject(TaxPath("tax-invoice-lookups.json"));
            // Master data is read-only here; the Tax Invoice module does not alter source master files.
            var clients = ReadRecords(TsPath("clients.json"));
            var agreements = ReadRecords(TsPath("main-frame-agreements.json"));
            var timesheets = ReadRecords(TsPath("timesheets.json"));
            if (clients.Count > 0) lookups["clients"] = clients.DeepClone();
            if (agreements.Count > 0) lookups["mainFrameAgreements"] = agreements.DeepClone();
            if (timesheets.Count > 0)
            {
                var batches = new JsonArray();
                foreach (var ts in timesheets)
                {
                    if (ts == null) continue;
                    batches.Add(new JsonObject {
                        ["value"] = ts["id"]?.DeepClone(),
                        ["text"] = $"{ts["batchNo"]?.ToString() ?? "Timesheet"} | {ts["payMonth"]?.ToString() ?? ""}"
                    });
                }
                lookups["timesheetBatches"] = batches;
                lookups["invoiceBatches"] = batches.DeepClone();
            }
            return lookups;
        }

        [HttpGet("")]
        public IActionResult Index(int? id)
        {
            ViewData["Config"] = ReadObject(TaxPath("tax-invoice-config.json"));
            ViewData["Lookups"] = ReadLookups();
            var invoice = Invoices(ReadData()).FirstOrDefault(x => (int?)x?["id"] == id);
            ViewData["Invoice"] = id.HasValue && invoice != null ? invoice.DeepClone() : new JsonObject();
            return View("~/Views/Master/TaxInvoice.cshtml");
        }

        [HttpGet("/Master/TaxInvoiceIndex")]
        public IActionResult TaxInvoiceIndex()
        {
            var rows = Invoices(ReadData()).Select(x => x?.DeepClone()).Where(x => x != null).Cast<JsonNode>().ToList();
            ViewData["Config"] = ReadObject(TaxPath("tax-invoice-config.json"));
            return View("~/Views/Master/TaxInvoiceIndex.cshtml", rows);
        }

        [HttpGet("GetAgreements")]
        public IActionResult GetAgreements(int clientId)
        {
            var result = ReadRecords(TsPath("main-frame-agreements.json"))
                .Where(x => (int?)x?["clientId"] == clientId && (bool?)x?["active"] != false)
                .Select(x => new { value = x?["id"], text = x?["name"]?.ToString() ?? x?["contractNo"]?.ToString() });
            return Json(result);
        }

        [HttpGet("Timesheets")]
        public IActionResult Timesheets()
        {
            var result = new JsonArray();
            foreach (var ts in ReadRecords(TsPath("timesheets.json")))
            {
                if (ts == null) continue;
                result.Add(new JsonObject { ["id"] = ts["id"]?.DeepClone(), ["batchNo"] = ts["batchNo"]?.DeepClone(), ["payMonth"] = ts["payMonth"]?.DeepClone() });
            }
            return Json(result);
        }

        [HttpGet("LoadTimesheet/{timesheetId:int}")]
        public IActionResult LoadTimesheet(int timesheetId)
        {
            var ts = ReadRecords(TsPath("timesheets.json")).FirstOrDefault(x => (int?)x?["id"] == timesheetId);
            if (ts == null) return NotFound(new { message = "The selected Time Sheet was not found." });
            var rows = new JsonArray();
            var attendance = ts["attendance"] as JsonArray ?? ts["attendanceRows"] as JsonArray ?? new JsonArray();
            foreach (var a in attendance)
            {
                if (a == null) continue;
                rows.Add(new JsonObject {
                    ["payMonth"] = ts["payMonth"]?.DeepClone(),
                    ["tsBatchNo"] = ts["batchNo"]?.DeepClone(),
                    ["startDate"] = a["startDate"]?.DeepClone(), ["endDate"] = a["endDate"]?.DeepClone(),
                    ["empCode"] = a["empCode"]?.DeepClone(), ["empName"] = a["empName"]?.DeepClone(),
                    ["contractType"] = a["contractType"]?.DeepClone(),
                    ["billingHead"] = "", ["denominationType"] = "1", ["qty"] = 0, ["rate"] = 0, ["amount"] = 0,
                    ["serviceFeeType"] = "", ["serviceFeeValue"] = 0, ["taxPercent"] = 0, ["taxAmount"] = 0,
                    ["netAmount"] = 0, ["poNumber"] = "", ["poDate"] = "", ["remarks"] = ""
                });
            }
            return Json(new { success = true, attendanceRows = rows, timesheet = new {
                id = ts["id"], batchNo = ts["batchNo"], date = ts["date"], postDate = ts["postDate"],
                clientId = ts["clientId"], mainFrameAgreementId = ts["mainFrameAgreementId"], payMonth = ts["payMonth"], timeSheetType = ts["timeSheetType"]
            }});
        }

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save([FromBody] JsonObject? invoice)
        {
            if (invoice == null) return BadRequest(new { message = "Invalid Tax Invoice data." });
            var data = ReadData();
            if (data["invoices"] is not JsonArray list) { list = new JsonArray(); data["invoices"] = list; }
            int id = (int?)invoice["id"] ?? 0;
            if (id <= 0) { id = ((int?)data["lastId"] ?? 0) + 1; data["lastId"] = id; }
            invoice["id"] = id;
            var old = list.FirstOrDefault(x => (int?)x?["id"] == id);
            if (old != null) list[list.IndexOf(old)] = invoice.DeepClone(); else list.Add(invoice.DeepClone());
            WriteData(data);
            return Json(new { success = true, id, message = "Tax Invoice saved successfully." });
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var data = ReadData(); var list = Invoices(data);
            var row = list.FirstOrDefault(x => (int?)x?["id"] == id);
            if (row == null) return NotFound(new { message = "Tax Invoice not found." });
            list.Remove(row); WriteData(data);
            return Json(new { success = true, message = "Tax Invoice deleted successfully." });
        }
    }
}
