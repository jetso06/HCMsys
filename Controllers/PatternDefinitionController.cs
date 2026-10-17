
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("PatternDefinition")]
    public class PatternDefinitionController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        private static readonly SemaphoreSlim FileLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };

        public PatternDefinitionController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataDirectory =>
            Path.Combine(
                _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"),
                "data",
                "PatternDefinition");

        private string DataFile =>
            Path.Combine(DataDirectory, "patterns.json");

        [HttpGet("")]
        public IActionResult Index()
        {
            return View("~/Views/Master/PatternDefinitionIndex.cshtml");
        }

        [HttpGet("PatternDefinition")]
        public IActionResult PatternDefinition(string? id)
        {
            ViewBag.PatternId = id;
            return View("~/Views/Master/PatternDefinition.cshtml");
        }

        [HttpGet("api/patterns")]
        public async Task<IActionResult> GetPatterns()
        {
            await FileLock.WaitAsync();

            try
            {
                var patterns = await ReadPatternsAsync();
                return Ok(patterns);
            }
            catch (JsonException)
            {
                return StatusCode(500, new
                {
                    message = "patterns.json contains invalid JSON. Please check the file."
                });
            }
            finally
            {
                FileLock.Release();
            }
        }

        [HttpPost("api/patterns")]
        public async Task<IActionResult> SavePattern([FromBody] PatternDefinition pattern)
        {
            if (pattern == null)
            {
                return BadRequest(new { message = "No pattern data was received." });
            }

            pattern.Name = pattern.Name?.Trim() ?? "";
            pattern.Code = pattern.Code?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(pattern.Name))
                return BadRequest(new { message = "Pattern Name is required." });

            if (string.IsNullOrWhiteSpace(pattern.Code))
                return BadRequest(new { message = "Pattern Code is required." });

            if (pattern.DaysInCycle < 1)
                return BadRequest(new { message = "Days in Cycle must be at least 1." });

            if (pattern.DaysInCycle > 1000)
                return BadRequest(new { message = "Days in Cycle cannot exceed 1000." });

            if (pattern.Days == null || pattern.Days.Length != pattern.DaysInCycle)
            {
                return BadRequest(new
                {
                    message = "The number of day statuses must match Days in Cycle."
                });
            }

            if (pattern.Days.Any(day =>
                day != "DD" && day != "OFF"))
            {
                return BadRequest(new
                {
                    message = "Each day must be either DD or OFF."
                });
            }

            await FileLock.WaitAsync();

            try
            {
                var patterns = await ReadPatternsAsync();

                var duplicateCode = patterns.Any(existing =>
                    !string.Equals(existing.Id, pattern.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Code, pattern.Code, StringComparison.OrdinalIgnoreCase));

                if (duplicateCode)
                {
                    return Conflict(new
                    {
                        message = "This Pattern Code is already in use."
                    });
                }

                PatternDefinition? existingPattern = null;

                if (!string.IsNullOrWhiteSpace(pattern.Id))
                {
                    existingPattern = patterns.FirstOrDefault(existing =>
                        existing.Id == pattern.Id);

                    if (existingPattern == null)
                    {
                        return NotFound(new
                        {
                            message = "The pattern you are trying to update was not found."
                        });
                    }
                }

                if (existingPattern != null)
                {
                    pattern.NameNo = existingPattern.NameNo;
                    pattern.Id = existingPattern.Id;

                    var index = patterns.FindIndex(existing =>
                        existing.Id == existingPattern.Id);

                    patterns[index] = pattern;
                }
                else
                {
                    pattern.NameNo = patterns.Count == 0
                        ? 1
                        : patterns.Max(existing => existing.NameNo) + 1;

                    pattern.Id = $"PAT-{pattern.NameNo:D4}";
                    patterns.Add(pattern);
                }

                Directory.CreateDirectory(DataDirectory);

                var json = JsonSerializer.Serialize(patterns, JsonOptions);

                await System.IO.File.WriteAllTextAsync(DataFile, json);

                return Ok(new
                {
                    message = "Pattern saved successfully.",
                    pattern
                });
            }
            catch (JsonException)
            {
                return StatusCode(500, new
                {
                    message = "Could not read patterns.json. Check its JSON format."
                });
            }
            catch (IOException)
            {
                return StatusCode(500, new
                {
                    message = "Could not write patterns.json. Check folder permissions."
                });
            }
            finally
            {
                FileLock.Release();
            }
        }

        [HttpDelete("api/patterns/{id}")]
        public async Task<IActionResult> DeletePattern(string id)
        {
            await FileLock.WaitAsync();

            try
            {
                var patterns = await ReadPatternsAsync();

                var pattern = patterns.FirstOrDefault(existing =>
                    string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase));

                if (pattern == null)
                {
                    return NotFound(new { message = "Pattern not found." });
                }

                patterns.Remove(pattern);

                Directory.CreateDirectory(DataDirectory);

                var json = JsonSerializer.Serialize(patterns, JsonOptions);
                await System.IO.File.WriteAllTextAsync(DataFile, json);

                return Ok(new { message = "Pattern deleted successfully." });
            }
            catch (JsonException)
            {
                return StatusCode(500, new
                {
                    message = "Could not read patterns.json. Check its JSON format."
                });
            }
            catch (IOException)
            {
                return StatusCode(500, new
                {
                    message = "Could not write patterns.json. Check folder permissions."
                });
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task<List<PatternDefinition>> ReadPatternsAsync()
        {
            if (!System.IO.File.Exists(DataFile))
                return new List<PatternDefinition>();

            var json = await System.IO.File.ReadAllTextAsync(DataFile);

            return JsonSerializer.Deserialize<List<PatternDefinition>>(
                json, JsonOptions) ?? new List<PatternDefinition>();
        }
    }

    public class PatternDefinition
    {
        public string Id { get; set; } = "";
        public int NameNo { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public int DaysInCycle { get; set; }
        public string[] Days { get; set; } = Array.Empty<string>();
    }
}
