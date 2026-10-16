using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PublicView.Api.Auth;
using PublicView.Api.Data;
using PublicView.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));

builder.Services.AddScoped<SocialAuthEvents>(sp =>
    new SocialAuthEvents(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<ILogger<SocialAuthEvents>>()));
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("spa", policy => policy
        .WithOrigins("http://localhost:4200", "https://localhost:4200")
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = AuthDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = AuthDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = AuthDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-pv_session";
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

void AttachAuthEvents<TOptions>(TOptions options) where TOptions : RemoteAuthenticationOptions
{
    options.Events = new RemoteAuthenticationEvents
    {
        OnTicketReceived = ctx =>
            ctx.HttpContext.RequestServices.GetRequiredService<SocialAuthEvents>().OnTicketReceivedAsync(ctx)
    };
}
// Providers are registered only when their keys are present.
var cfg = builder.Configuration;

if (!string.IsNullOrEmpty(cfg["Authentication:Google:ClientId"]))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = cfg["Authentication:Google:ClientId"]!;
        options.ClientSecret = cfg["Authentication:Google:ClientSecret"]!;
        options.CallbackPath = "/signin-google";
        AttachAuthEvents(options);
    });
}

if (!string.IsNullOrEmpty(cfg["Authentication:Facebook:ClientId"]))
{
    authBuilder.AddFacebook(options =>
    {
        options.AppId = cfg["Authentication:Facebook:ClientId"]!;
        options.AppSecret = cfg["Authentication:Facebook:ClientSecret"]!;
        options.CallbackPath = "/signin-facebook";
        AttachAuthEvents(options);
    });
}

if (!string.IsNullOrEmpty(cfg["Authentication:Microsoft:ClientId"]))
{
    authBuilder.AddMicrosoftAccount(options =>
    {
        options.ClientId = cfg["Authentication:Microsoft:ClientId"]!;
        options.ClientSecret = cfg["Authentication:Microsoft:ClientSecret"]!;
        options.CallbackPath = "/signin-microsoft";
        AttachAuthEvents(options);
    });
}

if (!string.IsNullOrEmpty(cfg["Authentication:Twitter:ClientId"]))
{
    authBuilder.AddTwitter(options =>
    {
        options.ClientId = cfg["Authentication:Twitter:ClientId"]!;
        options.ClientSecret = cfg["Authentication:Twitter:ClientSecret"]!;
        options.CallbackPath = "/signin-twitter";
        AttachAuthEvents(options);
    });
}

if (!string.IsNullOrEmpty(cfg["Authentication:Apple:ClientId"]))
{
    authBuilder.AddApple(options =>
    {
        options.ClientId = cfg["Authentication:Apple:ClientId"]!;
        options.KeyId = cfg["Authentication:Apple:KeyId"]!;
        options.TeamId = cfg["Authentication:Apple:TeamId"]!;
        options.PrivateKey = (_, _) => Task.FromResult(new ReadOnlyMemory<char>((cfg["Authentication:Apple:PrivateKey"] ?? string.Empty).ToCharArray()));
        options.GenerateClientSecret = true;
        options.CallbackPath = "/signin-apple";
        AttachAuthEvents(options);
    });
}

if (!string.IsNullOrEmpty(cfg["Authentication:Instagram:ClientId"]))
{
    authBuilder.AddInstagram(options =>
    {
        options.ClientId = cfg["Authentication:Instagram:ClientId"]!;
        options.ClientSecret = cfg["Authentication:Instagram:ClientSecret"]!;
        options.CallbackPath = "/signin-instagram";
        AttachAuthEvents(options);
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("spa");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();
