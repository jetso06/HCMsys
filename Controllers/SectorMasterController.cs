using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master")]
    public class SectorMasterController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        public SectorMasterController(IWebHostEnvironment env) => _env = env;

        private string Dir => Path.Combine(_env.WebRootPath, "data", "SectorMaster");
        private string ConfigPath => Path.Combine(Dir, "sector-master-config.json");
        private string LookupsPath => Path.Combine(Dir, "sector-master-lookups.json");
        private string DataPath => Path.Combine(Dir, "sector-master-data.json");

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

        [HttpGet("SectorMaster")]
        public IActionResult Index(int? id, bool clone = false)
        {
            var cfg = Read(ConfigPath, new SectorMasterConfig());
            var lookups = Read(LookupsPath, new Dictionary<string, List<SectorLookupOption>>());
            var data = Read(DataPath, new SectorMasterData());
            var record = data.Records.FirstOrDefault(x => x.Id == id) ?? new SectorMasterRecord();
            if (clone && record.Id > 0) { record.Id = 0; record.Name = record.Name + " Copy"; }
            while (record.SectorDefinitions.Count < 5) record.SectorDefinitions.Add(new SectorDefinitionRow());
            return View("~/Views/Master/SectorMaster.cshtml",
                new SectorMasterViewModel { Config = cfg, Lookups = lookups, Record = record });
        }

        [HttpGet("SectorMasterIndex")]
        public IActionResult List()
        {
            var cfg = Read(ConfigPath, new SectorMasterConfig());
            var data = Read(DataPath, new SectorMasterData());
            return View("~/Views/Master/SectorMasterIndex.cshtml",
                new SectorMasterIndexViewModel { Config = cfg, Records = data.Records.OrderByDescending(x => x.Id).ToList() });
        }

        [HttpPost("SectorMaster/Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(SectorMasterRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.Name) || string.IsNullOrWhiteSpace(record.Code))
            {
                TempData["Error"] = "Name and Code are mandatory.";
                return RedirectToAction(nameof(Index), new { id = record.Id > 0 ? record.Id : (int?)null });
            }

            var data = Read(DataPath, new SectorMasterData());
            record.SectorDefinitions = (record.SectorDefinitions ?? new List<SectorDefinitionRow>())
                .Where(x => x.FromAge.HasValue || x.ToAge.HasValue ||
                            !string.IsNullOrWhiteSpace(x.TravelClass) ||
                            !string.IsNullOrWhiteSpace(x.TravelType) ||
                            x.AmountInBaseCurrency.HasValue ||
                            !string.IsNullOrWhiteSpace(x.Remarks)).ToList();

            if (record.Id <= 0)
            {
                record.Id = ++data.LastId;
                record.CreatedBy = "System";
                record.CreatedDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                data.Records.Add(record);
            }
            else
            {
                var existing = data.Records.FirstOrDefault(x => x.Id == record.Id);
                if (existing == null) return NotFound();
                record.CreatedBy = existing.CreatedBy;
                record.CreatedDateTime = existing.CreatedDateTime;
                record.ModifiedBy = "System";
                record.ModifiedDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                data.Records[data.Records.IndexOf(existing)] = record;
            }

            Write(DataPath, data);
            TempData["Success"] = "Sector Master saved successfully.";
            return RedirectToAction(nameof(Index), new { id = record.Id });
        }

        [HttpPost("SectorMaster/Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var data = Read(DataPath, new SectorMasterData());
            data.Records.RemoveAll(x => x.Id == id);
            Write(DataPath, data);
            TempData["Success"] = "Sector Master record deleted.";
            return RedirectToAction(nameof(List));
        }
    }

    public class SectorMasterViewModel
    {
        public SectorMasterConfig Config { get; set; } = new();
        public Dictionary<string, List<SectorLookupOption>> Lookups { get; set; } = new();
        public SectorMasterRecord Record { get; set; } = new();
    }
    public class SectorMasterIndexViewModel
    {
        public SectorMasterConfig Config { get; set; } = new();
        public List<SectorMasterRecord> Records { get; set; } = new();
    }
    public class SectorMasterConfig
    {
        public SectorMasterPage Page { get; set; } = new();
        public SectorMasterButtons Buttons { get; set; } = new();
        public List<SectorMasterSection> Sections { get; set; } = new();
        public SectorMasterGrid Grid { get; set; } = new();
        public SectorMasterIndex Index { get; set; } = new();
    }
    public class SectorMasterPage { public string Title { get; set; } = "Sector Master"; public string Icon { get; set; } = "fa-layer-group"; public int PageId { get; set; } }
    public class SectorMasterButtons { public List<SectorMasterButton> Form { get; set; } = new(); public List<SectorMasterButton> Index { get; set; } = new(); }
    public class SectorMasterButton { public string Id { get; set; } = ""; public string Text { get; set; } = ""; public string Icon { get; set; } = ""; public string Style { get; set; } = "secondary"; public string Action { get; set; } = ""; public bool Visible { get; set; } = true; }
    public class SectorMasterSection { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public List<SectorMasterField> Fields { get; set; } = new(); }
    public class SectorMasterField { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public string Type { get; set; } = "text"; public string? Source { get; set; } public int MaxLength { get; set; } public bool Required { get; set; } public bool Readonly { get; set; } }
    public class SectorMasterGrid { public string Id { get; set; } = ""; public string Property { get; set; } = ""; public string Title { get; set; } = ""; public List<SectorMasterColumn> Columns { get; set; } = new(); }
    public class SectorMasterColumn { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public string Type { get; set; } = "text"; public string? Source { get; set; } public string? Step { get; set; } public int MaxLength { get; set; } }
    public class SectorMasterIndex { public string Heading { get; set; } = "Sector Master List"; public List<SectorMasterIndexColumn> Columns { get; set; } = new(); }
    public class SectorMasterIndexColumn { public string Field { get; set; } = ""; public string Label { get; set; } = ""; public bool Visible { get; set; } = true; }
    public class SectorLookupOption { public string Value { get; set; } = ""; public string Text { get; set; } = ""; }
    public class SectorMasterData { public int LastId { get; set; } public List<SectorMasterRecord> Records { get; set; } = new(); }
    public class SectorMasterRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string NameInNativeLanguage { get; set; } = "";
        public string Comments { get; set; } = "";
        public List<SectorDefinitionRow> SectorDefinitions { get; set; } = Enumerable.Range(0, 5).Select(_ => new SectorDefinitionRow()).ToList();
        public string CreatedBy { get; set; } = "";
        public string CreatedDateTime { get; set; } = "";
        public string ModifiedBy { get; set; } = "";
        public string ModifiedDateTime { get; set; } = "";
    }
    public class SectorDefinitionRow
    {
        public decimal? FromAge { get; set; }
        public decimal? ToAge { get; set; }
        public string TravelClass { get; set; } = "";
        public string TravelType { get; set; } = "";
        public decimal? AmountInBaseCurrency { get; set; }
        public string Remarks { get; set; } = "";
    }
}
