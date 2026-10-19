
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers
{
    [Route("PatternAssignment/api")]
    [ApiController]
    public class PatternAssignmentController : ControllerBase
    {
        private readonly IWebHostEnvironment _environment;
        private static readonly SemaphoreSlim FileLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public PatternAssignmentController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        private string DataDirectory => Path.Combine(
            _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"),
            "data", "PatternAssignment");

        private string AssignmentFile => Path.Combine(DataDirectory, "patternassignment.json");
        private string EmployeeFile => Path.Combine(DataDirectory, "employees.json");

        private string PatternFile => Path.Combine(
            _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"),
            "data", "PatternDefinition", "patterns.json");

        private async Task<List<T>> ReadList<T>(string path)
        {
            if (!System.IO.File.Exists(path))
                return new List<T>();

            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }

        private async Task WriteList<T>(string path, List<T> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await System.IO.File.WriteAllTextAsync(
                path, JsonSerializer.Serialize(items, JsonOptions));
        }

        private async Task EnsureEmployees()
        {
            if (System.IO.File.Exists(EmployeeFile))
                return;

            var employees = new List<Employee>
            {
                new() { Id = "1", Code = "EMP001", Name = "Aarav Sharma", Department = "IT", Company = "Neoxis", Active = true },
                new() { Id = "2", Code = "EMP002", Name = "Ananya Nair", Department = "HR", Company = "Neoxis", Active = true },
                new() { Id = "3", Code = "EMP003", Name = "Arjun Menon", Department = "Finance", Company = "Neoxis", Active = true },
                new() { Id = "4", Code = "EMP004", Name = "Diya Thomas", Department = "IT", Company = "Neoxis", Active = true },
                new() { Id = "5", Code = "EMP005", Name = "Rahul Kumar", Department = "Operations", Company = "Neoxis", Active = true },
                new() { Id = "6", Code = "EMP006", Name = "Meera Joseph", Department = "HR", Company = "Neoxis", Active = true },
                new() { Id = "7", Code = "EMP007", Name = "Vishnu Das", Department = "Finance", Company = "Neoxis", Active = false }
            };

            await WriteList(EmployeeFile, employees);
        }

        [HttpGet("patterns")]
        public async Task<IActionResult> GetPatterns()
        {
            try
            {
                return Ok(await ReadList<PatternDefinitionRecord>(PatternFile));
            }
            catch (JsonException)
            {
                return StatusCode(500, new { message = "patterns.json contains invalid JSON." });
            }
        }

        [HttpGet("assignments")]
        public async Task<IActionResult> GetAssignments()
        {
            try
            {
                return Ok(await ReadList<PatternAssignmentRecord>(AssignmentFile));
            }
            catch (JsonException)
            {
                return StatusCode(500, new { message = "patternassignment.json contains invalid JSON." });
            }
        }

        [HttpGet("assignments/{id}")]
        public async Task<IActionResult> GetAssignment(string id)
        {
            var records = await ReadList<PatternAssignmentRecord>(AssignmentFile);
            var record = records.FirstOrDefault(x => x.Id == id);

            return record == null
                ? NotFound(new { message = "Assignment not found." })
                : Ok(record);
        }

        [HttpGet("filters")]
        public async Task<IActionResult> GetFilters()
        {
            await EnsureEmployees();
            var employees = await ReadList<Employee>(EmployeeFile);

            return Ok(new
            {
                companies = employees.Select(x => x.Company).Distinct().OrderBy(x => x),
                departments = employees.Select(x => x.Department).Distinct().OrderBy(x => x)
            });
        }

        [HttpPost("load-employees")]
        public async Task<IActionResult> LoadEmployees([FromBody] DateRangeRequest request)
        {
            if (!DateOnly.TryParse(request.StartDate, out var startDate) ||
                !DateOnly.TryParse(request.EndDate, out var endDate))
                return BadRequest(new { message = "Enter valid start and end dates." });

            if (endDate < startDate)
                return BadRequest(new { message = "End Date cannot be earlier than Start Date." });

            await EnsureEmployees();

            var employees = await ReadList<Employee>(EmployeeFile);
            var assignments = await ReadList<PatternAssignmentRecord>(AssignmentFile);

            var eligible = employees
                .Where(e => e.Active)
                .Where(e => !assignments.Any(a =>
                    a.EmployeeIds.Contains(e.Id) &&
                    DateOnly.TryParse(a.StartDate, out var aStart) &&
                    DateOnly.TryParse(a.EndDate, out var aEnd) &&
                    aStart <= endDate && aEnd >= startDate))
                .Select(e => new
                {
                    id = e.Id,
                    code = e.Code,
                    name = e.Name,
                    department = e.Department,
                    company = e.Company
                });

            return Ok(eligible);
        }

        [HttpPost("assignments")]
        public async Task<IActionResult> SaveAssignment([FromBody] SaveAssignmentRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "No assignment data received." });

            if (string.IsNullOrWhiteSpace(request.PatternId))
                return BadRequest(new { message = "Select a pattern." });

            if (!DateOnly.TryParse(request.Date, out _))
                return BadRequest(new { message = "Enter a valid document date." });

            if (!DateOnly.TryParse(request.StartDate, out var startDate) ||
                !DateOnly.TryParse(request.EndDate, out var endDate))
                return BadRequest(new { message = "Enter valid start and end dates." });

            if (endDate < startDate)
                return BadRequest(new { message = "End Date cannot be earlier than Start Date." });

            if (request.EmployeeIds == null || request.EmployeeIds.Count == 0)
                return BadRequest(new { message = "Select at least one employee." });

            await FileLock.WaitAsync();

            try
            {
                await EnsureEmployees();

                var employees = await ReadList<Employee>(EmployeeFile);
                var patterns = await ReadList<PatternDefinitionRecord>(PatternFile);
                var assignments = await ReadList<PatternAssignmentRecord>(AssignmentFile);

                var pattern = patterns.FirstOrDefault(p => p.Id == request.PatternId);
                if (pattern == null)
                    return BadRequest(new { message = "The selected pattern was not found." });

                var selectedEmployees = employees
                    .Where(e => request.EmployeeIds.Contains(e.Id) && e.Active)
                    .ToList();

                if (selectedEmployees.Count != request.EmployeeIds.Distinct().Count())
                    return BadRequest(new { message = "One or more selected employees are invalid or inactive." });

                var existing = string.IsNullOrWhiteSpace(request.Id)
                    ? null
                    : assignments.FirstOrDefault(a => a.Id == request.Id);

                if (!string.IsNullOrWhiteSpace(request.Id) && existing == null)
                    return NotFound(new { message = "Assignment to edit was not found." });

                var conflicts = assignments
                    .Where(a => existing == null || a.Id != existing.Id)
                    .Where(a =>
                        DateOnly.TryParse(a.StartDate, out var aStart) &&
                        DateOnly.TryParse(a.EndDate, out var aEnd) &&
                        aStart <= endDate && aEnd >= startDate)
                    .SelectMany(a => a.EmployeeIds)
                    .Intersect(request.EmployeeIds)
                    .Distinct()
                    .ToList();

                if (conflicts.Count > 0)
                {
                    var names = employees
                        .Where(e => conflicts.Contains(e.Id))
                        .Select(e => e.Name);

                    return Conflict(new
                    {
                        message = "These employees already have an overlapping assignment: " +
                                  string.Join(", ", names)
                    });
                }

                var year = DateOnly.Parse(request.Date).Year;
                var documentNo = existing?.DocumentNo ??
                    $"{year}-{assignments.Count(a => a.DocumentNo.StartsWith(year + "-")) + 1:000}";

                var record = new PatternAssignmentRecord
                {
                    Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                    DocumentNo = documentNo,
                    Date = request.Date,
                    Comments = request.Comments?.Trim() ?? "",
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    PatternId = pattern.Id,
                    PatternName = pattern.Name,
                    PatternCode = pattern.Code,
                    EmployeeIds = request.EmployeeIds.Distinct().ToList(),
                    EmployeeNames = selectedEmployees.Select(e => e.Name).ToList(),
                    EmployeeCount = selectedEmployees.Count
                };

                if (existing == null)
                    assignments.Add(record);
                else
                {
                    var index = assignments.FindIndex(a => a.Id == existing.Id);
                    assignments[index] = record;
                }

                await WriteList(AssignmentFile, assignments);

                return Ok(new
                {
                    message = existing == null
                        ? "Assignment saved successfully."
                        : "Assignment updated successfully.",
                    assignment = record
                });
            }
            catch (JsonException)
            {
                return StatusCode(500, new { message = "A JSON file contains invalid JSON." });
            }
            catch (IOException)
            {
                return StatusCode(500, new { message = "Could not write the JSON file. Check folder permissions." });
            }
            finally
            {
                FileLock.Release();
            }
        }

        [HttpDelete("assignments/{id}")]
        public async Task<IActionResult> DeleteAssignment(string id)
        {
            await FileLock.WaitAsync();

            try
            {
                var assignments = await ReadList<PatternAssignmentRecord>(AssignmentFile);
                var record = assignments.FirstOrDefault(a => a.Id == id);

                if (record == null)
                    return NotFound(new { message = "Assignment not found." });

                assignments.Remove(record);
                await WriteList(AssignmentFile, assignments);

                return Ok(new { message = "Assignment deleted successfully." });
            }
            catch (IOException)
            {
                return StatusCode(500, new { message = "Could not update the assignment JSON file." });
            }
            finally
            {
                FileLock.Release();
            }
        }
    }

    public class Employee
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Department { get; set; } = "";
        public string Company { get; set; } = "";
        public bool Active { get; set; }
    }

    public class PatternDefinitionRecord
    {
        public string Id { get; set; } = "";
        public int NameNo { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public int DaysInCycle { get; set; }
        public string[] Days { get; set; } = Array.Empty<string>();
    }

    public class DateRangeRequest
    {
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
    }

    public class SaveAssignmentRequest
    {
        public string? Id { get; set; }
        public string Date { get; set; } = "";
        public string? Comments { get; set; }
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string PatternId { get; set; } = "";
        public List<string> EmployeeIds { get; set; } = new();
    }

    public class PatternAssignmentRecord
    {
        public string Id { get; set; } = "";
        public string DocumentNo { get; set; } = "";
        public string Date { get; set; } = "";
        public string Comments { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string PatternId { get; set; } = "";
        public string PatternName { get; set; } = "";
        public string PatternCode { get; set; } = "";
        public List<string> EmployeeIds { get; set; } = new();
        public List<string> EmployeeNames { get; set; } = new();
        public int EmployeeCount { get; set; }
    }
}
