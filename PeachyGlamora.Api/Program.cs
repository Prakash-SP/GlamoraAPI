using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Middleware;
using PeachyGlamora.Api.Models;
using PeachyGlamora.Api.Services;
using PeachyGlamora.Api.Validators;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// QuestPDF is free for small businesses/individuals under its Community
// license — this must be set once at startup before any PDF is generated.
// If Peachy Glamora's revenue/team size ever exceeds QuestPDF's Community
// license thresholds, this needs to switch to a paid QuestPDF license.
QuestPDF.Settings.License = LicenseType.Community;

// ---------- Database ----------
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(config.GetConnectionString("Default")));

// ---------- Identity (users, roles, password hashing) ----------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders(); // needed for password-reset tokens

// ---------- JWT auth (API calls) + Cookie auth (Swagger UI browser login) ----------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        // CRITICAL: without this, ASP.NET Core's default inbound claim mapping silently
        // rewrites standard JWT claims like "sub" into long Microsoft claim-type URIs
        // (e.g. .../nameidentifier) when validating an incoming token. Every controller
        // in this API reads User.FindFirst("sub") expecting the raw claim to survive —
        // without this line, that call returns null and throws a NullReferenceException
        // on every single authenticated request (login/register still "work" because
        // issuing a token doesn't require reading claims back off one).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!))
        };
    })
    // Separate scheme used only to gate /internal/docs (Swagger). Kept out of the
    // Default*Scheme above so it never interferes with normal Bearer-token auth
    // on the real API controllers.
    .AddCookie("SwaggerCookie", options =>
    {
        options.LoginPath = "/internal/docs/login";
        options.Cookie.Name = "PeachyGlamora.SwaggerAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SwaggerAdmin", policy =>
        policy.AddAuthenticationSchemes("SwaggerCookie")
              .RequireRole("Admin"));
});

// ---------- App services ----------
// Generic HttpClient factory — used by PincodeController for the server-side
// fallback call to the external India Post pincode API (a direct browser
// call to that API is blocked by CORS, so this fallback must happen here).
builder.Services.AddHttpClient();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IInvoicePdfService, InvoicePdfService>();
builder.Services.AddScoped<IPaymentGatewayService, UpiQrPaymentService>();
builder.Services.AddHttpClient<ISmsService, Msg91SmsService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IOrderNotificationService, OrderNotificationService>();
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
// Wraps IDataProtector (already active because Identity depends on it above)
// to encrypt/decrypt bank account numbers. Singleton is safe — IDataProtector
// instances are thread-safe and meant to be reused, not created per-request.
builder.Services.AddSingleton<BankAccountCrypto>();

// ---------- CORS: only the storefront + admin frontends may call this API ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("Storefront", policy =>
        policy.WithOrigins(config.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
              .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// ---------- Rate limiting: throttle auth endpoints to blunt brute-force / OTP-spam abuse ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// ---------- Background jobs (abandoned cart emails, order status reminders) ----------
builder.Services.AddHangfire(cfg => cfg.UseSqlServerStorage(config.GetConnectionString("Default")));
builder.Services.AddHangfireServer();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Peachy Glamora API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
});
builder.Services.AddScoped<BulkImportJobRunner>();

// Config-driven Swagger toggle. false by default in appsettings.json,
// overridden to true in appsettings.Production.json (or via env var
// SwaggerSettings__Enabled=true) when docs need to be reachable in prod.
var swaggerEnabled = config.GetValue<bool>("SwaggerSettings:Enabled");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear(); // nginx is on the same box, so trust it explicitly
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseGlobalExceptionHandling();
app.UseForwardedHeaders(); // must come early, before auth/HTTPS redirect
// Must be first: catches exceptions thrown by every later middleware/controller.
app.UseGlobalExceptionHandling();

// ---------- Middleware pipeline ----------

//app.UseHttpsRedirection();
app.UseCors("Storefront");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (swaggerEnabled)
{
    // Simple login page + POST handler for the SwaggerCookie scheme.
    // Only Admin-role users can reach /internal/docs beyond this point.
    app.MapGet("/internal/docs/login", () => Results.Content("""
        <!doctype html>
        <html>
        <body style="font-family: sans-serif; max-width: 320px; margin: 80px auto;">
            <h3>Peachy Glamora — API Docs</h3>
            <form method="post">
                <input name="email" placeholder="Email" style="display:block; width:100%; margin-bottom:8px;" />
                <input name="password" type="password" placeholder="Password" style="display:block; width:100%; margin-bottom:8px;" />
                <button type="submit">Log in</button>
            </form>
        </body>
        </html>
        """, "text/html"));

    app.MapPost("/internal/docs/login", async (HttpContext ctx, UserManager<ApplicationUser> userManager) =>
    {
        var form = await ctx.Request.ReadFormAsync();
        var email = form["email"].ToString();
        var password = form["password"].ToString();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null || !await userManager.CheckPasswordAsync(user, password))
            return Results.Redirect("/internal/docs/login?error=1");

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains("Admin"))
            return Results.Forbid();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "SwaggerCookie");
        await ctx.SignInAsync("SwaggerCookie", new ClaimsPrincipal(identity));

        return Results.Redirect("/internal/docs");
    });

    // Gate everything else under /internal/docs (Swagger JSON + UI) behind the
    // SwaggerCookie scheme + Admin role. Redirects to the login page above if
    // the visitor isn't authenticated as an Admin yet.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isDocsPath = path.StartsWithSegments("/internal/docs");
        var isLoginPath = path.StartsWithSegments("/internal/docs/login");

        if (isDocsPath && !isLoginPath)
        {
            var result = await context.AuthenticateAsync("SwaggerCookie");
            if (!result.Succeeded || !(result.Principal?.IsInRole("Admin") ?? false))
            {
                context.Response.Redirect("/internal/docs/login");
                return;
            }
        }

        await next();
    });

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Peachy Glamora API v1");
        c.RoutePrefix = "internal/docs";
    });
}

app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter() }
});
app.MapControllers();

// ---------- Seed roles + default admin user on first run ----------
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Customer", "Admin", "Support" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // Idempotent, like the role loop above — checks FindByEmailAsync first,
    // so this is safe to leave running on every single startup; it only
    // ever does anything the very first time (empty database). Deliberately
    // NOT done via HasData/a raw SQL migration: UserManager.CreateAsync is
    // what correctly hashes the password and sets SecurityStamp/
    // ConcurrencyStamp/NormalizedEmail — hand-writing those in a migration
    // risks a subtly wrong hash that fails to log in with no obvious error.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string adminEmail = "peachyglamora@gmail.com";

    if (await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "Admin",
            PhoneNumber = "8920346319",
            DateOfBirth = new DateTime(1993, 8, 3),
            AuthProvider = "Email",
            EmailConfirmed = true,
            ReferralCode = "ADM" + Random.Shared.Next(1000, 9999),
        };

        // NOTE: change this password immediately after first login in
        // production — it's committed to source control in plain text here,
        // same as any other seed value, which is fine only until the first
        // real login happens.
        var result = await userManager.CreateAsync(admin, "Minutes@123");
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, "Admin");
        else
            app.Logger.LogError("Failed to seed default admin user: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}

app.Run();