using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/Classification")]
    public class ClassificationController : Controller
    {
        private readonly IWebHostEnvironment _env;

        private readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        public ClassificationController(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string DataDirectory =>
            Path.Combine(_env.WebRootPath, "data", "Classification");

        private string ConfigPath =>
            Path.Combine(DataDirectory, "classification-config.json");

        private string TypesPath =>
            Path.Combine(DataDirectory, "classification-types.json");

        private string RecordsPath =>
            Path.Combine(DataDirectory, "classifications.json");

        private async Task<T> ReadJson<T>(string path, T fallback)
        {
            if (!System.IO.File.Exists(path))
                return fallback;

            string json = await System.IO.File.ReadAllTextAsync(path);

            return JsonSerializer.Deserialize<T>(
                json, _jsonOptions) ?? fallback;
        }

        private async Task WriteJson<T>(string path, T data)
        {
            Directory.CreateDirectory(DataDirectory);

            string json = JsonSerializer.Serialize(
                data, _jsonOptions);

            await System.IO.File.WriteAllTextAsync(path, json);
        }

        private Task<ClassificationPageConfig> GetConfig() =>
            ReadJson(ConfigPath, new ClassificationPageConfig());

        private Task<List<ClassificationType>> GetTypes() =>
            ReadJson(TypesPath, new List<ClassificationType>());

        private Task<List<ClassificationRecord>> GetRecords() =>
            ReadJson(RecordsPath, new List<ClassificationRecord>());

        // INDEX
        [HttpGet("List")]
        [HttpGet("ClassificationIndex")]
        [HttpGet("~/Master/ClassificationIndex")]
        public async Task<IActionResult> Index(
            string? search, int? masterType)
        {
            var config = await GetConfig();
            var types = await GetTypes();
            var records = await GetRecords();

            if (!string.IsNullOrWhiteSpace(search))
            {
                records = records.Where(x =>
                    $"{x.Name} {x.Code} {x.NameNL} {x.Comments}"
                    .Contains(search,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (masterType.HasValue)
            {
                records = records
                    .Where(x => x.MasterType == masterType.Value)
                    .ToList();
            }

            var rows = records
    .OrderBy(x => x.Name)
    .Select(x => new ClassificationRow
    {
        Id = x.Id,

        Values = new Dictionary<string, string>
        {
            ["Name"] = x.Name,
            ["Code"] = x.Code,
            ["GroupName"] = x.GroupName,
            ["MasterTypeName"] = x.MasterTypeName,
            ["Group"] = x.GroupDisplay,
            ["Active"] = x.ActiveDisplay,

            ["ApprovalStatus"] = x.ApprovalStatus,

            ["Id"] = x.Id.ToString(),

            ["CreatedBy"] = x.CreatedBy,
            ["CreatedDateTime"] = x.CreatedDateTime,

            ["ModifiedBy"] = x.ModifiedBy,
            ["ModifiedDateTime"] = x.ModifiedDateTime
        }
    })
    .ToList();

            var model = new ClassificationIndexVM
            {
                Config = config,
                Types = types,
                Rows = rows,
                Search = search ?? "",
                SelectedMasterType = masterType
            };

            return View(
                "~/Views/Master/ClassificationIndex.cshtml",
                model);
        }

        // NEW / EDIT / CLONE FORM
        [HttpGet("")]
        [HttpGet("Classification")]
        public async Task<IActionResult> Form(int? id, bool clone = false)
        {
            var config = await GetConfig();
            var types = await GetTypes();
            var records = await GetRecords();

            var existing = id.HasValue
                ? records.FirstOrDefault(x => x.Id == id.Value)
                : null;

            if (id.HasValue && existing == null)
                return NotFound();

            ClassificationRecord model;

            if (existing == null)
            {
                model = new ClassificationRecord();
            }
            else
            {
                model = new ClassificationRecord
                {
                    Id = clone ? 0 : existing.Id,
                    Name = clone ? existing.Name + " Copy" : existing.Name,
                    Code = clone ? "" : existing.Code,
                    NameNL = existing.NameNL,
                    MasterType = existing.MasterType,
                    ServiceMapping = existing.ServiceMapping,
                    Comments = existing.Comments,
                    IsActive = existing.IsActive,
                    KeyValues = existing.KeyValues
                        .Select(x => new ClassificationKeyValue
                        {
                            Key = x.Key,
                            Value = x.Value
                        }).ToList()
                };
            }

            ViewBag.Config = config;
            ViewBag.Types = types;
            ViewBag.IsClone = clone;

            return View("~/Views/Master/Classification.cshtml", model);
        }


        // SAVE

        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(ClassificationRecord model)
        {
            model.KeyValues = (model.KeyValues ?? new())
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) ||
                            !string.IsNullOrWhiteSpace(x.Value))
                .ToList();

            if (string.IsNullOrWhiteSpace(model.Name) ||
                string.IsNullOrWhiteSpace(model.Code) ||
                string.IsNullOrWhiteSpace(model.ServiceMapping))
            {
                TempData["Error"] = "Please complete all mandatory fields.";
                return RedirectToAction(nameof(Form),
                    new { id = model.Id == 0 ? (int?)null : model.Id });
            }

            var records = await GetRecords();

            bool duplicateCode = records.Any(x =>
                x.Id != model.Id &&
                x.Code.Equals(model.Code, StringComparison.OrdinalIgnoreCase));

            if (duplicateCode)
            {
                TempData["Error"] = "This classification code already exists.";
                return RedirectToAction(nameof(Form),
                    new { id = model.Id == 0 ? (int?)null : model.Id });
            }

            if (model.Id == 0)
            {
                model.Id = records.Count == 0 ? 1 : records.Max(x => x.Id) + 1;
                model.IsActive = true;
                records.Add(model);
            }
            else
            {
                var existing = records.FirstOrDefault(x => x.Id == model.Id);

                if (existing == null)
                    return NotFound();

                existing.Name = model.Name;
                existing.Code = model.Code;
                existing.NameNL = model.NameNL;
                existing.MasterType = model.MasterType;
                existing.ServiceMapping = model.ServiceMapping;
                existing.Comments = model.Comments;
                existing.KeyValues = model.KeyValues;
            }

            await WriteJson(RecordsPath, records);

            TempData["Success"] = "Classification saved successfully.";

            return RedirectToAction(nameof(Index));
        }


        // DELETE ONE
        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var records = await GetRecords();

            records.RemoveAll(x => x.Id == id);

            await WriteJson(RecordsPath, records);

            return RedirectToAction(nameof(Index));
        }

        // DELETE SELECTED
        [HttpPost("DeleteSelected")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSelected(
            int[] selectedIds)
        {
            var records = await GetRecords();

            records.RemoveAll(x => selectedIds.Contains(x.Id));

            await WriteJson(RecordsPath, records);

            return RedirectToAction(nameof(Index));
        }

        // ACTIVATE / DEACTIVATE
        [HttpPost("ToggleStatus/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var records = await GetRecords();

            var item = records.FirstOrDefault(x => x.Id == id);

            if (item != null)
            {
                item.IsActive = !item.IsActive;

                await WriteJson(RecordsPath, records);
            }

            return RedirectToAction(nameof(Index));
        }
    }

    // DATA MODELS


    public class ClassificationRecord
    {
        public bool Group { get; set; }
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Code { get; set; } = "";

        public string? NameNL { get; set; }

        public int MasterType { get; set; }

        public string ServiceMapping { get; set; } = "";

        public string? Comments { get; set; }

        public bool IsActive { get; set; } = true;
        public int ParentId { get; set; }

        public bool IsGroup { get; set; }

        public string ApprovalStatus { get; set; } = "P";

        public string CreatedBy { get; set; } = "";

        public string CreatedDateTime { get; set; } = "";

        public string ModifiedBy { get; set; } = "";

        public string ModifiedDateTime { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string MasterTypeName { get; set; } = "";
        public string GroupDisplay { get; set; } = "";
        public string ActiveDisplay { get; set; } = "";

        public List<ClassificationKeyValue> KeyValues { get; set; } = new();
    }

    public class ClassificationKeyValue
    {
        public string Key { get; set; } = "";

        public string Value { get; set; } = "";
    }


    public class ClassificationType
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public class ConfigButton
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Style { get; set; } = "secondary";
        public string Action { get; set; } = "";
        public bool Visible { get; set; } = true;
    }

    public class ConfigField
    {
        public string Field { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";
        public int MaxLength { get; set; } = 100;
        public bool Required { get; set; }
        public string? OptionsSource { get; set; }
        public List<string>? Options { get; set; }
    }

    public class ConfigColumn
    {
        public string Field { get; set; } = "";
        public string Header { get; set; } = "";
        public string Tooltip { get; set; } = "";
    }


    public class ClassificationPageConfig
    {
        public PageInfo Page { get; set; } = new();

        public ButtonGroups Buttons { get; set; } = new();

        public List<ConfigField> FormFields { get; set; } = new();

        public List<ConfigTab> Tabs { get; set; } = new();
        public List<DatabaseField> DatabaseFields { get; set; } = new();
        public KeyValueTableInfo KeyValueTable { get; set; } = new();

        public CommentsInfo Comments { get; set; } = new();

        public TableInfo Table { get; set; } = new();
    }

    public class PageInfo
    {
        public string Title { get; set; } = "Master Classifications";

        public int PageId { get; set; } = 400;

        public string SectionTitle { get; set; } = "GENERAL";

        public string Icon { get; set; } = "fa-layer-group";
    }

    public class ButtonGroups
    {
        public List<ConfigButton> Index { get; set; } = new();

        public List<ConfigButton> Form { get; set; } = new();
    }

    public class ConfigTab
    {
        public string Id { get; set; } = "general";

        public string Label { get; set; } = "General";
    }

    public class KeyValueTableInfo
    {
        public string Heading { get; set; } = "Key - Value";

        public string KeyHeader { get; set; } = "Key";

        public string ValueHeader { get; set; } = "Value";

        public string AddButton { get; set; } = "Add Row";

        public string EmptyMessage { get; set; } =
            "No key-value entries added.";
    }

    public class CommentsInfo
    {
        public string Label { get; set; } = "Comments";

        public int MaxLength { get; set; } = 200;
    }

    public class TableInfo
    {
        public string Heading { get; set; } = "Classification List";

        public List<ConfigColumn> Columns { get; set; } = new();
    }

public class DatabaseField
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
}

public class ClassificationRow
    {
        public int Id { get; set; }
        public Dictionary<string, string> Values { get; set; } = new();
    }

    public class ClassificationIndexVM
    {
        public ClassificationPageConfig Config { get; set; } = new();
        public List<ClassificationType> Types { get; set; } = new();
        public List<ClassificationRow> Rows { get; set; } = new();
        public string Search { get; set; } = "";
        public int? SelectedMasterType { get; set; }
    }
}
