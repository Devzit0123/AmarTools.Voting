using AmarTools.Voting.Data;
using AmarTools.Voting.Models;
using AmarTools.Voting.Services;
using AmarTools.Voting.Services.Background;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── MVC + Razor Pages ─────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages().AddRazorRuntimeCompilation();
builder.Services.AddHttpContextAccessor();

// ── Database (PostgreSQL) ─────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("VotingConnection")
    ?? throw new InvalidOperationException("Connection string 'VotingConnection' not found.");

// ✅ ONLY DbContext (Scoped)
builder.Services.AddDbContext<VotingDbContext>(options =>
    options.UseNpgsql(connStr));

// ❌ REMOVED: AddDbContextFactory (this caused your error)

// ── Identity ──────────────────────────────────────────────────────────────
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

// ── Cookie Configuration ──────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// ── Custom Services ───────────────────────────────────────────────────────
builder.Services.AddScoped<IVotingService, VotingService>();
builder.Services.AddScoped<IBlockchainService, BlockchainService>();

// ── Background Service (SAFE) ─────────────────────────────────────────────
builder.Services.AddSingleton<IVoteBlockQueue, VoteBlockQueue>();
builder.Services.AddHostedService<BlockchainBackgroundService>();

var app = builder.Build();

// ── Development: Migrate + Seed Admin ─────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    var db = services.GetRequiredService<VotingDbContext>();
    await db.Database.MigrateAsync();

    await SeedAdminUser(services);
}

// ── Middleware ────────────────────────────────────────────────────────────
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

// ── Seed Admin User ───────────────────────────────────────────────────────
static async Task SeedAdminUser(IServiceProvider services)
{
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    const string adminEmail = "admin@amartools.com";
    const string adminPassword = "Admin@123456";

    // Create Roles
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));

    if (!await roleManager.RoleExistsAsync("ProgramOwner"))
        await roleManager.CreateAsync(new IdentityRole("ProgramOwner"));

    // Create Admin User
    var admin = await userManager.FindByEmailAsync(adminEmail);
    if (admin == null)
    {
        admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "System Administrator"
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}