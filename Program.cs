using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── MVC + Razor Pages ──────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();
builder.Services.AddHttpContextAccessor();

// ── Database (PostgreSQL) ──────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("VotingConnection");
if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException(
        "Connection string 'VotingConnection' is not configured. " +
        "Use 'dotnet user-secrets' in Development or set the " +
        "ConnectionStrings__VotingConnection environment variable in Production.");

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
    options.LoginPath       = "/Identity/Account/Login";
    options.LogoutPath      = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.AddRateLimiter(options =>
{
    // Public search: 30 requests per minute per IP
    options.AddFixedWindowLimiter("search", opt =>
    {
        opt.PermitLimit              = 30;
        opt.Window                   = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder     = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit               = 5;
    });

   
    options.AddFixedWindowLimiter("voting", opt =>
    {
        opt.PermitLimit              = 10;
        opt.Window                   = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder     = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit               = 2;
    });

    // Admin write operations: 60 requests per minute per IP
    options.AddFixedWindowLimiter("admin", opt =>
    {
        opt.PermitLimit              = 60;
        opt.Window                   = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder     = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit               = 10;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Custom Services ────────────────────────────────────────────────────────
builder.Services.AddScoped<IVotingService, VotingService>();
builder.Services.AddScoped<IBlockchainService, BlockchainService>();

// ── Background Blockchain Queue (Singleton + HostedService) ───────────────
builder.Services.AddSingleton<IVoteBlockQueue, VoteBlockQueue>();
builder.Services.AddHostedService<BlockchainBackgroundService>();

var app = builder.Build();

// ── Development: Migrate + Seed Admin ─────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services   = scope.ServiceProvider;
    var db         = services.GetRequiredService<VotingDbContext>();
    await db.Database.MigrateAsync();
    await SeedAdminUser(services, app.Configuration);
}

// ── Middleware ─────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();   // FIX: must come after UseRouting, before UseAuthorization
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

    // Read from config (dotnet user-secrets or env vars)
    var adminEmail    = config["Seed:AdminEmail"];
    var adminPassword = config["Seed:AdminPassword"];

    if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
    {
        logger.LogWarning(
            "Seed:AdminEmail or Seed:AdminPassword is not configured. " +
            "Skipping admin seed. Use 'dotnet user-secrets' to configure these values.");
        return;
    }

    // Ensure roles exist
    foreach (var role in new[] { "Admin", "ProgramOwner" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Create admin user if not already present
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
