using System;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;
using CsvHelper.Configuration;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

int[] days = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CovidCaseDb>(opt => opt.UseInMemoryDatabase("CovidCases"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => "Hello World!");

app.MapGet("/get_latest_covid_case_data/{region}/{year}/{month}", async (CovidCaseDb db, string region, int year, int month) =>
{
    if (year < 2020 || year > DateTime.Now.Year || month < 1 || month > 12) throw new Exception("invaild dates");

    HttpClient httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyApplication", "1"));
    string repo = "CSSEGISandData/COVID-19";
    string contentsUrl = $"https://api.github.com/repos/{repo}/contents/csse_covid_19_data/csse_covid_19_daily_reports";
    Stream contentsStream = await httpClient.GetStreamAsync(contentsUrl);
    StreamReader contentsReader = new StreamReader(contentsStream);
    string contentsText = contentsReader.ReadToEnd();
    contentsReader.Close();

    List<FileMeta> contentsJson = JsonConvert.DeserializeObject<List<FileMeta>>(contentsText);
    string yearStr = year.ToString();
    string monthStr = month >= 1 && month <= 9 ? $"0{month}" : $"{month}";
    List<FileMeta> relatedMeta = new List<FileMeta>();
    foreach (FileMeta o in contentsJson)
    {
        if (o.name?.Length >= 10 && o.name.Substring(3, 7) == $"{monthStr}-{yearStr}" &&
            o.type == "file" &&
            o.download_url != null &&
            o.name != null)
            relatedMeta.Add(o);
    };
    Console.WriteLine(relatedMeta.Count);
    foreach (FileMeta fileMeta in relatedMeta)
    {
        // get data from github
        Stream fileStream = await httpClient.GetStreamAsync(fileMeta.download_url);
        StreamReader fileReader = new StreamReader(fileStream);
        CsvConfiguration conf = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
        };
        CsvReader filecsv = new CsvReader(fileReader, conf);

        // covidCasesByDay
        Guid covidCasesByDayID = Guid.NewGuid();
        CovidCasesByDay covidCasesByDay = new CovidCasesByDay
        {
            covidCasesByDayID = covidCasesByDayID,
        };

        // fileMeta
        fileMeta.fileMetaID = Guid.NewGuid();
        fileMeta.covidCasesByDayID = covidCasesByDayID;
        db.fileMetas.Add(fileMeta);

        // covidCases
        //covidCasesByDay.covidCases = filecsv.GetRecords<CovidCase>().ToList();//.Where(r => r.Country_Region == region).ToList();
        List<CovidCase> covidCases = filecsv.GetRecords<CovidCase>().ToList();
        foreach (CovidCase covidCase in covidCases)
        {
            Guid covidCaseID = Guid.NewGuid();
            covidCase.covidCaseID = covidCaseID;
            covidCase.covidCasesByDayID = covidCasesByDayID;
            db.covidCases.Add(covidCase);
        }

        db.covidCasesByDay.Add(covidCasesByDay);
    }

    await db.SaveChangesAsync();

    byte[] byteArray = Encoding.ASCII.GetBytes(contentsText);
    MemoryStream streamToReturn = new MemoryStream(byteArray);

    return Results.Stream(streamToReturn, "application/json");
    //return Results.Ok("data loaded!");
});

app.MapGet("/get_all_records", async (CovidCaseDb db) =>
{
    List<CovidCasesByDay> allCovidCasesByDay = await db.covidCasesByDay.Include(c => c.covidCases).Include(c => c.fileMeta).ToListAsync();
    byte[] byteArray = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(allCovidCasesByDay, Formatting.None,
                        new JsonSerializerSettings()
                        {
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                        }).ToString());
    MemoryStream streamToReturn = new MemoryStream(byteArray);

    return Results.Stream(streamToReturn, "application/json");
});

app.Run();

public partial class CovidCase
{
    public int? FIPS { get; set; }
    public string? Admin2 { get; set; }
    public string? Province_State { get; set; }
    public string? Country_Region { get; set; }
    public DateTime? Last_Update { get; set; }
    public Double? Lat { get; set; }
    public Double? Long_ { get; set; }
    public int? Confirmed { get; set; }
    public int? Deaths { get; set; }
    public int? Recovered { get; set; }
    public int? Active { get; set; }
    public string? Combined_Key { get; set; }
    public double? Incident_Rate { get; set; }
    public double? Case_Fatality_Ratio { get; set; }
    public Guid covidCaseID { get; set; }

    public Guid covidCasesByDayID { get; set; }
    public CovidCasesByDay? covidCasesByDay { get; set; }
}

public partial class FileMeta
{
    public string? sha { get; set; }
    public string? path { get; set; }
    public string? url { get; set; }
    public string? name { get; set; }
    public int? size { get; set; }
    public string? download_url { get; set; }
    public string? git_url { get; set; }
    public string? html_url { get; set; }
    public string? type { get; set; }
    public Guid fileMetaID { get; set; }

    public Guid covidCasesByDayID { get; set; }
    public CovidCasesByDay? covidCasesByDay { get; set; }
}

public partial class CovidCasesByDay
{
    public Guid covidCasesByDayID { get; set; }
    public List<CovidCase>? covidCases { get; } = new List<CovidCase>();
    public FileMeta? fileMeta { get; set; }
}

class CovidCaseDb : DbContext
{
    public CovidCaseDb(DbContextOptions<CovidCaseDb> options)
        : base(options) { }

    public DbSet<FileMeta> fileMetas => Set<FileMeta>();
    public DbSet<CovidCase> covidCases => Set<CovidCase>();
    public DbSet<CovidCasesByDay> covidCasesByDay => Set<CovidCasesByDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileMeta>().HasKey(c => new { c.fileMetaID });
        modelBuilder.Entity<CovidCase>().HasKey(c => new { c.covidCaseID });
        modelBuilder.Entity<CovidCasesByDay>().HasKey(c => new { c.covidCasesByDayID });
        modelBuilder.Entity<CovidCasesByDay>().HasMany(c => c.covidCases).WithOne(c => c.covidCasesByDay).HasForeignKey(c => c.covidCasesByDayID);
        modelBuilder.Entity<CovidCasesByDay>().HasOne(c => c.fileMeta).WithOne(f => f.covidCasesByDay).HasForeignKey<FileMeta>(c => c.covidCasesByDayID);
    }
}