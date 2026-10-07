using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── MVC + Razor Pages ──────────────────────────────────────────────────────
var mvcBuilder = builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
if (builder.Environment.IsDevelopment())
    mvcBuilder.AddRazorRuntimeCompilation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection()
    .SetApplicationName("AmarTools.Voting")
    // Persist keys to DB so they survive deploys on platforms with ephemeral filesystems.
    .PersistKeysToDbContext<AmarTools.Voting.Data.VotingDbContext>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Database (PostgreSQL) ──────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("VotingConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException(
        "Connection string 'VotingConnection' is not configured. " +
        "Use 'dotnet user-secrets' in Development or set the " +
        "ConnectionStrings__VotingConnection (or DATABASE_URL) environment variable in Production.");

// Render/Heroku-style URI connection strings (postgres://user:pass@host:port/db)
// need converting to Npgsql's key=value format.
if (connStr.StartsWith("postgres://") || connStr.StartsWith("postgresql://"))
{
    var uri = new Uri(connStr);
    var userInfo = uri.UserInfo.Split(':', 2);
    var port = uri.Port == -1 ? 5432 : uri.Port; // Uri doesn't know postgres's default port
    connStr = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
              $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo[1])};" +
              $"SSL Mode=Require;Trust Server Certificate=true";
}

builder.Services.AddDbContext<VotingDbContext>(options =>
    options.UseNpgsql(connStr));

// ── Identity ───────────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
})
.AddEntityFrameworkStores<VotingDbContext>()
.AddDefaultTokenProviders()
.AddDefaultUI();

// ── Cookie Configuration ───────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Identity/Account/Login";
    options.LogoutPath       = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("search", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetClientPartitionKey(context), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit          = 30,
        Window               = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit           = 5
    }));

    options.AddPolicy("voting", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetClientPartitionKey(context), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit          = 10,
        Window               = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit           = 2
    }));

    options.AddPolicy("admin", context =>
        RateLimitPartition.GetFixedWindowLimiter(GetClientPartitionKey(context), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit          = 60,
        Window               = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit           = 10
    }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Custom Services ────────────────────────────────────────────────────────
builder.Services.AddScoped<IVotingService, VotingService>();
builder.Services.AddScoped<IBlockchainService, BlockchainService>();

// ── Background Blockchain Queue (Singleton + HostedService) ───────────────
builder.Services.AddSingleton<IVoteBlockQueue, VoteBlockQueue>();
builder.Services.AddHostedService<BlockchainBackgroundService>();

var app = builder.Build();

// ── Optional migration + admin seed ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db       = services.GetRequiredService<VotingDbContext>();
    if (app.Configuration.GetValue("Database:ApplyMigrations", app.Environment.IsDevelopment()))
        await db.Database.MigrateAsync();
    await SeedAdminUser(services, app.Configuration);
}

// ── Middleware ─────────────────────────────────────────────────────────────
app.UseForwardedHeaders();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

static string GetClientPartitionKey(HttpContext context)
{
    var id = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(id))
        return $"user:{id}";

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
}

//app.UseHttpsRedirection(); // Render handles HTTPS at proxy level
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.Run();


static async Task SeedAdminUser(IServiceProvider services, IConfiguration config)
{
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var logger      = services.GetRequiredService<ILogger<Program>>();

    var adminEmail    = config["Seed:AdminEmail"];
    var adminPassword = config["Seed:AdminPassword"];

    if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
    {
        logger.LogWarning(
            "Seed:AdminEmail or Seed:AdminPassword is not configured. " +
            "Skipping admin seed. Use 'dotnet user-secrets' to configure these values.");
        return;
    }

    foreach (var role in new[] { "Admin", "ProgramOwner" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    var admin = await userManager.FindByEmailAsync(adminEmail);
    if (admin == null)
    {
        admin = new ApplicationUser
        {
            UserName       = adminEmail,
            Email          = adminEmail,
            EmailConfirmed = true,
            FullName       = "System Administrator"
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
            logger.LogInformation("Admin user '{Email}' seeded successfully.", adminEmail);
        }
        else
        {
            logger.LogError("Failed to seed admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
