using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace HCMSys.Controllers;

[Route("Master/CallOffTemplate")]
public class CallOffTemplateController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private string Dir => Path.Combine(_env.WebRootPath, "data", "CallOffTemplate");
    private string ConfigPath => Path.Combine(Dir, "calloff-template-config.json");
    private string RecordsPath => Path.Combine(Dir, "calloff-templates.json");
    private string CallOffDir => Path.Combine(_env.WebRootPath, "data", "CallOff");

    public CallOffTemplateController(IWebHostEnvironment env) => _env = env;

    private async Task<T> Read<T>(string path, T fallback)
    {
        if (!System.IO.File.Exists(path)) return fallback;
        return JsonSerializer.Deserialize<T>(await System.IO.File.ReadAllTextAsync(path), _json) ?? fallback;
    }
    private async Task Write<T>(string path, T data)
    {
        Directory.CreateDirectory(Dir);
        await System.IO.File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _json));
    }
    private Task<TemplateConfig> Config() => Read(ConfigPath, new TemplateConfig());
    private Task<List<JsonObject>> Records() => Read(RecordsPath, new List<JsonObject>());

    private async Task<Dictionary<string,List<TemplateOption>>> Lookups(TemplateConfig config)
    {
        var result = new Dictionary<string,List<TemplateOption>>(StringComparer.OrdinalIgnoreCase);
        var sources = config.Sections.SelectMany(x=>x.Fields).Concat(config.Grids.SelectMany(x=>x.Columns))
            .Where(x=>!string.IsNullOrWhiteSpace(x.Source)).Select(x=>x.Source!).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var path = Path.Combine(CallOffDir, Path.GetFileName(source));
            if (!System.IO.File.Exists(path)) path = Path.Combine(Dir, Path.GetFileName(source));
            var list = new List<TemplateOption>();
            if (System.IO.File.Exists(path))
            {
                using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String) { var s=item.GetString()??""; list.Add(new(){Value=s,Text=s}); }
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            var value=item.TryGetProperty("value",out var v)?v.ToString():item.TryGetProperty("id",out v)?v.ToString():"";
                            var text=item.TryGetProperty("text",out var t)?t.ToString():item.TryGetProperty("name",out t)?t.ToString():value;
                            list.Add(new(){Value=value,Text=text});
                        }
                    }
            }
            result[source]=list;
        }
        return result;
    }

    [HttpGet("")]
    [HttpGet("Form")]
    public async Task<IActionResult> Form(int? id, bool clone=false)
    {
        var cfg=await Config(); var rows=await Records();
        var record=id.HasValue?rows.FirstOrDefault(x=>(int?)x["id"]==id.Value):null;
        if(id.HasValue && record==null) return NotFound();
        if(clone && record!=null){record=(JsonObject)record.DeepClone();record["id"]=0;record["status"]="Pending";}
        record ??= new JsonObject {["id"]=0,["status"]="Pending"};
        ViewBag.Config=cfg; ViewBag.Record=record; ViewBag.Lookups=await Lookups(cfg);
        ViewBag.IsReadOnly=id.HasValue && !clone;
        return View("~/Views/Master/CallOffTemplate.cshtml");
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save()
    {
        var form=Request.Form; var cfg=await Config(); var rows=await Records();
        int id=int.TryParse(form["Id"],out var n)?n:0;
        var record=new JsonObject {["id"]=id};
        foreach(var f in cfg.Sections.SelectMany(s=>s.Fields))
        {
            var values=form[f.Field];
            record[f.Field]=f.Type=="checkbox"
                ? JsonValue.Create(values.Any(v=>string.Equals(v,"true",StringComparison.OrdinalIgnoreCase)))
                : JsonValue.Create(values.ToString());
        }
        foreach(var g in cfg.Grids)
        {
            int count=int.TryParse(form[$"{g.Id}__count"],out var c)?Math.Clamp(c,0,500):0;
            var array=new JsonArray();
            for(int i=0;i<count;i++)
            {
                var row=new JsonObject();
                foreach(var col in g.Columns)
                {
                    var values=form[$"{g.Property}[{i}].{col.Field}"];
                    row[col.Field]=col.Type=="checkbox"
                        ? JsonValue.Create(values.Any(v=>string.Equals(v,"true",StringComparison.OrdinalIgnoreCase)))
                        : JsonValue.Create(values.ToString());
                }
                array.Add(row);
            }
            record[g.Property]=array;
            record[$"{g.Id}Comments"]=form[$"{g.Id}Comments"].ToString();
        }
        var old=id==0?null:rows.FirstOrDefault(x=>(int?)x["id"]==id);
        if(id==0)
        {
            id=rows.Count==0?1:rows.Max(x=>(int?)x["id"]??0)+1;
            record["id"]=id; record["templateNo"]=$"COT-{id:00000}"; record["status"]="Pending"; rows.Add(record);
        }
        else
        {
            if(old==null)return NotFound();
            record["templateNo"]=old["templateNo"]?.DeepClone();
            record["status"]=old["status"]?.DeepClone()??"Pending";
            rows[rows.IndexOf(old)]=record;
        }
        await Write(RecordsPath,rows);
        TempData["Success"]="Call Off Template saved successfully.";
        return RedirectToAction(nameof(Form),new{id});
    }

    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var rows=await Records(); rows.RemoveAll(x=>(int?)x["id"]==id); await Write(RecordsPath,rows);
        TempData["Success"]="Call Off Template deleted."; return RedirectToAction(nameof(Form));
    }

    [HttpPost("SetStatus/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id,string status)
    {
        var rows=await Records(); var row=rows.FirstOrDefault(x=>(int?)x["id"]==id);
        if(row==null)return NotFound(); row["status"]=status; await Write(RecordsPath,rows);
        TempData["Success"]=$"Template status changed to {status}.";
        return RedirectToAction(nameof(Form),new{id});
    }
}

public class TemplateConfig { public TemplatePage Page{get;set;}=new(); public TemplateButtonGroup Buttons{get;set;}=new(); public List<TemplateSection> Sections{get;set;}=new(); public List<TemplateGrid> Grids{get;set;}=new(); }
public class TemplatePage { public string Title{get;set;}="Call Off Template"; public string Icon{get;set;}="fa-file-contract"; }
public class TemplateButtonGroup { public List<TemplateButton> Form{get;set;}=new(); }
public class TemplateButton { public string Label{get;set;}=""; public string Icon{get;set;}=""; public string Style{get;set;}="secondary"; public string Action{get;set;}=""; public bool Visible{get;set;}=true; }
public class TemplateSection { public string Id{get;set;}=""; public string Title{get;set;}=""; public List<TemplateField> Fields{get;set;}=new(); }
public class TemplateGrid { public string Id{get;set;}=""; public string Title{get;set;}=""; public string Subtitle{get;set;}=""; public string Property{get;set;}=""; public List<TemplateField> Columns{get;set;}=new(); }
public class TemplateField { public string Field{get;set;}=""; public string Label{get;set;}=""; public string Type{get;set;}="text"; public string? Source{get;set;} public bool Required{get;set;} public bool Readonly{get;set;} public string? Step{get;set;} public string? Remarks{get;set;} public int MaxLength{get;set;} }
public class TemplateOption { public string Value{get;set;}=""; public string Text{get;set;}=""; }
