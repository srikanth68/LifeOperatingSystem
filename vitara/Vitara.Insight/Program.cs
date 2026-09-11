using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Data;
using Vitara.Insight;

// Vitara Insight — the computation half of health, as its own process.
//
// Vitara used to do three jobs in two containers: fetch from Oura and HealthKit, work
// out what the numbers mean, and serve both. Splitting the middle job out is worth a
// container for reasons that showed up while building it.
//
// The failure domains are genuinely different. Ingestion breaks when a vendor changes
// an endpoint or a token expires; computation breaks when a detector has a bug or a
// threshold is wrong. Neither should be able to take the other down, and a detector
// loop wedged on bad data must not stop tonight's sleep from being fetched.
//
// They also change on completely different schedules. The Oura client is stable for
// months at a time; the detectors and thresholds are the part being actively tuned,
// and tuning them should not mean restarting the sync.
//
// ONE DATABASE, SPLIT BY TABLE OWNERSHIP. Not a second store. The computation needs
// every raw observation, and copying health data across a process boundary would buy
// separation at the price of a sync problem and two versions of the truth. Instead the
// write sets are disjoint:
//
//   vitara-worker   writes the typed tables   (SleepSession, DailyReadiness, ...)
//   vitara-insight  writes the derived ones   (Observations, Baselines,
//                                              DerivedMetrics, Findings)
//
// Both read everything. Nothing writes the same row as anything else, so SQLite in WAL
// mode serialises the writes it has to and never has two processes fighting over a
// table. The same pattern already runs between vitara and vitara-worker.
//
// No schema creation here, deliberately. Vitara.API owns the schema and runs
// EnsureCreated plus the hand-written migrations at startup; a second process racing to
// create the same tables is how a "CREATE TABLE IF NOT EXISTS" turns into a locked
// database on a cold boot. This one waits for the tables to exist.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<VitaraDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "vitara.db")}"));
builder.Services.AddScoped<IVitaraRepository, VitaraRepository>();

builder.Services.AddHostedService<DerivedMetricsWorker>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:5173"))
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();

app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5110");
