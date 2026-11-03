using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("Master/Candidate")]
    public class CandidateController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public CandidateController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataPath(params string[] parts)
        {
            return Path.Combine(
                new[] { _environment.WebRootPath, "data", "Candidate" }
                    .Concat(parts)
                    .ToArray());
        }

        private string ConfigPath =>
            DataPath("candidate-config.json");

        private string RecordsPath =>
            DataPath("candidates.json");

        private string EducationConfigPath =>
            DataPath("educational-qualification-config.json");

        private string ExperienceConfigPath =>
            DataPath("experience-reference-config.json");

        private string ReferenceConfigPath =>
            DataPath("reference-config.json");

        private string AttachmentConfigPath =>
            DataPath("attachment-config.json");

        private JsonObject ReadConfigFile(string path)
        {
            if (!System.IO.File.Exists(path))
                throw new FileNotFoundException(
                    "Candidate configuration file was not found.", path);

            return JsonNode.Parse(
                System.IO.File.ReadAllText(path))?.AsObject()
                ?? new JsonObject();
        }

        private JsonObject ReadRecords()
        {
            if (!System.IO.File.Exists(RecordsPath))
            {
                var initialStore = new JsonObject
                {
                    ["lastId"] = 0,
                    ["records"] = new JsonArray()
                };

                WriteJson(RecordsPath, initialStore);
            }

            return JsonNode.Parse(
                System.IO.File.ReadAllText(RecordsPath))?.AsObject()
                ?? new JsonObject
                {
                    ["lastId"] = 0,
                    ["records"] = new JsonArray()
                };
        }

        private void WriteJson(string path, JsonNode data)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            System.IO.File.WriteAllText(
                path,
                data.ToJsonString(_jsonOptions));
        }

        private void WriteRecords(JsonObject store)
        {
            WriteJson(RecordsPath, store);
        }

        private void LoadPageConfiguration()
        {
            ViewData["CandidateConfig"] = ReadConfigFile(ConfigPath);
            ViewData["EducationConfig"] =
                ReadConfigFile(EducationConfigPath);
            ViewData["ExperienceConfig"] =
                ReadConfigFile(ExperienceConfigPath);
            ViewData["ReferenceConfig"] =
                ReadConfigFile(ReferenceConfigPath);
            ViewData["AttachmentConfig"] =
                ReadConfigFile(AttachmentConfigPath);
        }

        // Candidate form: /Master/Candidate
        [HttpGet("")]
        public IActionResult Index(int? id = null)
        {
            try
            {
                LoadPageConfiguration();

                var store = ReadRecords();
                JsonObject? candidate = null;

                if (id.HasValue)
                {
                    candidate = store["records"]?
                        .AsArray()
                        .OfType<JsonObject>()
                        .FirstOrDefault(r =>
                            r["id"]?.GetValue<int>() == id.Value)
                        ?.DeepClone() as JsonObject;

                    if (candidate == null)
                        return NotFound("Candidate record was not found.");
                }

                ViewData["CandidateId"] = id ?? 0;

                return View(
                    "~/Views/Master/Candidate.cshtml",
                    candidate ?? new JsonObject());
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to load Candidate");
            }
        }

        // Candidate grid: /Master/CandidateIndex
        [HttpGet("/Master/CandidateIndex")]
        public IActionResult CandidateIndex()
        {
            try
            {
                var store = ReadRecords();

                var records = store["records"]?.AsArray()
                    .OfType<JsonObject>()
                    .OrderByDescending(r =>
                        r["id"]?.GetValue<int>() ?? 0)
                    .Select(r => r.DeepClone())
                    .ToList() ?? new List<JsonNode>();

                return View(
                    "~/Views/Master/CandidateIndex.cshtml",
                    records);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to load Candidate records");
            }
        }

        // Save or update candidate.
        [HttpPost("Save")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(string candidateJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidateJson))
                    return BadRequest("Candidate data is required.");

                var candidate =
                    JsonNode.Parse(candidateJson)?.AsObject();

                if (candidate == null)
                    return BadRequest("Invalid candidate data.");

                var store = ReadRecords();

                var records = store["records"]?.AsArray()
                    ?? new JsonArray();

                int id = candidate["id"]?.GetValue<int>() ?? 0;

                var existing = records
                    .OfType<JsonObject>()
                    .FirstOrDefault(r =>
                        r["id"]?.GetValue<int>() == id);

                if (id == 0 || existing == null)
                {
                    id = (store["lastId"]?.GetValue<int>() ?? 0) + 1;

                    candidate["id"] = id;
                    candidate["code"] = $"CAND-{id:D4}";

                    store["lastId"] = id;

                    EnsureGridArrays(candidate);

                    records.Add(candidate);

                    TempData["SuccessMessage"] =
                        "Candidate saved successfully.";
                }
                else
                {
                    candidate["id"] = id;

                    candidate["code"] =
                        existing["code"]?.DeepClone()
                        ?? candidate["code"]?.DeepClone();

                    EnsureGridArrays(candidate);

                    int index = records.IndexOf(existing);
                    records[index] = candidate;

                    TempData["SuccessMessage"] =
                        "Candidate updated successfully.";
                }

                store["records"] = records;
                WriteRecords(store);

                return RedirectToAction(nameof(CandidateIndex));
            }
            catch (JsonException ex)
            {
                return BadRequest(
                    "Invalid candidate JSON: " + ex.Message);
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to save Candidate");
            }
        }

        private void EnsureGridArrays(JsonObject candidate)
        {
            candidate["education"] ??= new JsonArray();
            candidate["experience"] ??= new JsonArray();
            candidate["references"] ??= new JsonArray();
            candidate["attachments"] ??= new JsonArray();
        }

        // Delete candidate.
        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            try
            {
                var store = ReadRecords();
                var records = store["records"]?.AsArray();

                if (records == null)
                    return NotFound("Candidate records were not found.");

                var candidate = records
                    .OfType<JsonObject>()
                    .FirstOrDefault(r =>
                        r["id"]?.GetValue<int>() == id);

                if (candidate == null)
                {
                    TempData["ErrorMessage"] =
                        "Candidate record was not found.";
                }
                else
                {
                    records.Remove(candidate);
                    WriteRecords(store);

                    TempData["SuccessMessage"] =
                        "Candidate deleted successfully.";
                }

                return RedirectToAction(nameof(CandidateIndex));
            }
            catch (Exception ex)
            {
                return Problem(
                    detail: ex.Message,
                    title: "Unable to delete Candidate");
            }
        }
    }
}