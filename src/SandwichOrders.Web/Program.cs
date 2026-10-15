using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SandwichOrders.Core.Announcements;
using SandwichOrders.Core.Menu;
using SandwichOrders.Core.Orders;
using SandwichOrders.Web;
using Microsoft.Extensions.FileProviders;
using SandwichOrders.Web.Data;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

var builder = WebApplication.CreateBuilder(args);

// The database is chosen from configuration, read lazily when the context is first created.
//   Database:Provider                 = Sqlite (default) or SqlServer
//   ConnectionStrings:DefaultConnection
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=sandwich-orders.db";

    if (string.Equals(configuration["Database:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        // A relative SQLite file is put next to the app (the content root), not in whatever folder the process was
        // started from: under IIS that can be a system folder where the app is not allowed to write.
        var sqlite = new SqliteConnectionStringBuilder(connectionString);
        if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
        {
            sqlite.DataSource = Path.Combine(services.GetRequiredService<IHostEnvironment>().ContentRootPath, sqlite.DataSource);
        }

        options.UseSqlite(sqlite.ToString());
    }
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // After 5 wrong passwords in a row the account is locked for 5 minutes, so a password cannot be guessed
        // by trying many (the staff account matters most). AccountController passes lockoutOnFailure: true.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    // Pages redirect to the login page; the JSON API answers 401 / 403 instead, which JavaScript can handle.
    static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");
    options.Events.OnRedirectToLogin = context =>
    {
        if (IsApi(context.Request)) context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsApi(context.Request)) context.Response.StatusCode = StatusCodes.Status403Forbidden;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// The keys that encrypt the login cookie and the anti-forgery tokens are kept in the database. By default they live in
// memory or in the user profile; on shared hosting the app is restarted often (and a new Docker container has no
// profile), which would sign everybody out and make open forms fail with "400 Bad Request".
builder.Services.AddDataProtection()
    .SetApplicationName("SandwichOrders")
    .PersistKeysToDbContext<AppDbContext>();

// JavaScript sends the anti-forgery token of the page in this header with every POST to the API.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// UI text lives in Resources/SharedResource.nl.resx. The English text itself is the key, so English needs no file.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)))
    // Enums in JSON as text ("Prepared"), not as numbers.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IMenuRepository, EfMenuRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IOrderingSettings, EfOrderingSettings>();
builder.Services.AddScoped<IAnnouncementRepository, EfAnnouncementRepository>();
builder.Services.AddSingleton<PhotoStorage>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AnnouncementService>();
// Hosted services start in the order they were registered. The database must be ready FIRST: Data Protection
// (registered earlier by Identity) reads its keys from the DataProtectionKeys table while the host starts.
builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService, DatabaseInitializer>());
builder.Services.AddFormRateLimiting();

// GET /health answers "Healthy" when the app runs and can reach its database (for Docker and monitoring later).
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();

// Photos uploaded by the canteen employee live outside wwwroot (Uploads:Path), so Docker can keep them on a volume.
var photoStorage = app.Services.GetRequiredService<PhotoStorage>();
Directory.CreateDirectory(photoStorage.Directory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(photoStorage.Directory),
    RequestPath = "/uploads"
});

// Only the language cookie decides the language, so the site starts in English until the user switches.
var cultures = new[] { new CultureInfo("en"), new CultureInfo("nl") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = cultures,
    SupportedUICultures = cultures,
    RequestCultureProviders = [new CookieRequestCultureProvider()]
});

app.UseRouting();
app.UseRateLimiter(); // after UseRouting, so the [EnableRateLimiting] attributes on the actions are known
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllerRoute(name: "default", pattern: "{controller=Menu}/{action=Index}/{id?}");

app.Run();

// Makes the entry point visible to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
