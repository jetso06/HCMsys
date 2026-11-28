using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
namespace HCMSys.Controllers;
[Route("Master")]
public class AirTicketClaimEncashmentController : Controller
{
 private readonly IWebHostEnvironment _env;
 private readonly JsonSerializerOptions _json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
 private string Dir=>Path.Combine(_env.WebRootPath,"data","AirTicketClaimEncashment");
 private string ConfigPath=>Path.Combine(Dir,"air-ticket-config.json");
 private string LookupsPath=>Path.Combine(Dir,"air-ticket-lookups.json");
 private string DataPath=>Path.Combine(Dir,"air-ticket-data.json");
 public AirTicketClaimEncashmentController(IWebHostEnvironment env)=>_env=env;
 private T Read<T>(string p,T fallback)=>System.IO.File.Exists(p)?JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(p),_json)??fallback:fallback;
 private void Write<T>(string p,T x){Directory.CreateDirectory(Dir);System.IO.File.WriteAllText(p,JsonSerializer.Serialize(x,_json));}
 private AirTicketConfig Config()=>Read(ConfigPath,new AirTicketConfig());
 private AirTicketLookups Lookups()=>Read(LookupsPath,new AirTicketLookups());
 private AirTicketStore Store()=>Read(DataPath,new AirTicketStore());

 [HttpGet("AirTicketClaimEncashment")]
 public IActionResult Form(int? id,bool clone=false){
  var s=Store();var old=id.HasValue?s.Records.FirstOrDefault(x=>x.Id==id):null;
  var r=old==null?new AirTicketRecord():JsonSerializer.Deserialize<AirTicketRecord>(JsonSerializer.Serialize(old),_json)!;
  if(clone){r.Id=0;r.DocumentNo="";}
  if(r.Id==0){r.DocumentNo=$"ATC-{s.LastId+1:00000}";r.Date=string.IsNullOrEmpty(r.Date)?DateTime.Today.ToString("yyyy-MM-dd"):r.Date;r.PostDate=string.IsNullOrEmpty(r.PostDate)?DateTime.Now.ToString("yyyy-MM-ddTHH:mm"):r.PostDate;}
  if(r.Dependents.Count==0)r.Dependents.Add(new());if(r.Claims.Count==0)r.Claims.Add(new());
  return View("~/Views/Master/AirTicketClaimEncashment.cshtml",new AirTicketFormVM{Config=Config(),Lookups=Lookups(),Record=r});
 }
 [HttpGet("AirTicketClaimEncashmentIndex")]
 public IActionResult Index(){var s=Store();return View("~/Views/Master/AirTicketClaimEncashmentIndex.cshtml",new AirTicketIndexVM{Config=Config(),Records=s.Records.OrderByDescending(x=>x.Id).ToList()});}
 [HttpPost("AirTicketClaimEncashment/Save")][ValidateAntiForgeryToken]
 public IActionResult Save(AirTicketRecord record){
  if(string.IsNullOrWhiteSpace(record.Employee)){TempData["Error"]="Employee is required.";return RedirectToAction(nameof(Form));}
  var s=Store();record.Dependents??=new();record.Claims??=new();
  record.Dependents=record.Dependents.Where(x=>!string.IsNullOrWhiteSpace(x.Name)).ToList();
  if(record.Id==0){record.Id=++s.LastId;record.CreatedBy=User.Identity?.Name??"Admin";record.CreatedDateTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");s.Records.Add(record);}
  else{var old=s.Records.FirstOrDefault(x=>x.Id==record.Id);record.CreatedBy=old?.CreatedBy??"Admin";record.CreatedDateTime=old?.CreatedDateTime??DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");record.ModifiedBy=User.Identity?.Name??"Admin";record.ModifiedDateTime=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");if(old!=null)s.Records.Remove(old);s.Records.Add(record);}
  Write(DataPath,s);TempData["Success"]="AirTicket Claim / Encashment saved successfully.";return RedirectToAction(nameof(Index));
 }
 [HttpPost("AirTicketClaimEncashment/Delete/{id:int}")][ValidateAntiForgeryToken]
 public IActionResult Delete(int id){var s=Store();s.Records.RemoveAll(x=>x.Id==id);Write(DataPath,s);TempData["Success"]="Record deleted.";return RedirectToAction(nameof(Index));}
}
public class AirTicketFormVM{public AirTicketConfig Config{get;set;}=new();public AirTicketLookups Lookups{get;set;}=new();public AirTicketRecord Record{get;set;}=new();}
public class AirTicketIndexVM{public AirTicketConfig Config{get;set;}=new();public List<AirTicketRecord> Records{get;set;}=new();}
public class AirTicketConfig{public AirTicketPage Page{get;set;}=new();public AirTicketButtons Buttons{get;set;}=new();public List<AirTicketField> HeaderFields{get;set;}=new();public AirTicketGrid DependentGrid{get;set;}=new();public AirTicketGrid ClaimGrid{get;set;}=new();public AirTicketIndex Index{get;set;}=new();}
public class AirTicketPage{public string Title{get;set;}="AirTicket Claim / Encashment";public string Icon{get;set;}="fa-plane";}
public class AirTicketButtons{public List<AirTicketButton> Form{get;set;}=new();public List<AirTicketButton> Index{get;set;}=new();}
public class AirTicketButton{public string Text{get;set;}="";public string Action{get;set;}="";public string Style{get;set;}="secondary";public string Icon{get;set;}="";public bool Visible{get;set;}=true;}
public class AirTicketField{public string Field{get;set;}="";public string Label{get;set;}="";public string Type{get;set;}="text";public string Lookup{get;set;}="";public int MaxLength{get;set;}public bool Required{get;set;}public bool Readonly{get;set;}}
public class AirTicketGrid{public string Title{get;set;}="";public List<AirTicketField> Columns{get;set;}=new();}
public class AirTicketIndex{public string Heading{get;set;}="";public List<AirTicketIndexColumn> Columns{get;set;}=new();}
public class AirTicketIndexColumn
{
    public string Field { get; set; } = "";
    public string Label { get; set; } = "";
    public int Width { get; set; }
    public string Tooltip { get; set; } = "";
}
public class AirTicketLookups{public List<AirTicketEmployee> Employees{get;set;}=new();public List<string> DependentTypes{get;set;}=new();public List<string> TravelClasses{get;set;}=new();public List<string> TravelTypes{get;set;}=new();public List<string> ClaimTypes{get;set;}=new();}
public class AirTicketEmployee{public string Value{get;set;}="";public string Text{get;set;}="";public string Designation{get;set;}="";public string Department{get;set;}="";public string ReportingManager{get;set;}="";public string NoticePeriod{get;set;}="";}
public class AirTicketStore{public int LastId{get;set;}public List<AirTicketRecord> Records{get;set;}=new();}
public class AirTicketRecord{public int Id{get;set;}public string DocumentNo{get;set;}="";public string Date{get;set;}="";public string PostDate{get;set;}="";public string Employee{get;set;}="";public string Designation{get;set;}="";public string Department{get;set;}="";public string ReportingManager{get;set;}="";public string NoticePeriod{get;set;}="";public string Comments{get;set;}="";public List<AirTicketDependent> Dependents{get;set;}=new();public List<AirTicketClaim> Claims{get;set;}=new();public string CreatedBy{get;set;}="";public string CreatedDateTime{get;set;}="";public string ModifiedBy{get;set;}="";public string ModifiedDateTime{get;set;}="";}
public class AirTicketDependent{public string Name{get;set;}="";public string Type{get;set;}="";public string DateOfBirth{get;set;}="";public string TravelClass{get;set;}="";public string TravelType{get;set;}="";public decimal NoOfTickets{get;set;}public decimal ServiceDays{get;set;}}
public class AirTicketClaim{public string Type{get;set;}="";public string TravelClass{get;set;}="";public string TravelType{get;set;}="";public string TicketClaim{get;set;}="";public decimal EligibleTickets{get;set;}public decimal Approved{get;set;}public decimal Pending{get;set;}public decimal Available{get;set;}public decimal UsedNow{get;set;}public decimal Balance{get;set;}public decimal AmountInBaseCurrency{get;set;}}
