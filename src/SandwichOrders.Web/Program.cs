using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
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
        options.UseSqlite(connectionString);
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
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// UI text lives in Resources/SharedResource.nl.resx. The English text itself is the key, so English needs no file.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IMenuRepository, EfMenuRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IOrderingSettings, EfOrderingSettings>();
builder.Services.AddScoped<IAnnouncementRepository, EfAnnouncementRepository>();
builder.Services.AddSingleton<PhotoStorage>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddHostedService<DatabaseInitializer>();

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
    RequestCultureProviders = new List<IRequestCultureProvider> { new CookieRequestCultureProvider() }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Menu}/{action=Index}/{id?}");

app.Run();

// Makes the entry point visible to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
