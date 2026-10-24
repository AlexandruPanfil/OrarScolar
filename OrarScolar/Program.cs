using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OrarScolar.Components;
using OrarScolar.Data;
using OrarScolar.Export;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Orar") ?? "Data Source=orar.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Cookie.Name = "orar.admin";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// brute-force guard: only POST /login is limited, 5 per minute per IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        HttpMethods.IsPost(ctx.Request.Method) && string.Equals(ctx.Request.Path.Value?.TrimEnd('/'), "/login", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter(""));
});

BellSchedule.Current = builder.Configuration.GetSection("Orar").Get<BellSchedule>() ?? new();

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContextAsync())
    await AppDb.InitializeAsync(db);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/logout", async (HttpContext ctx, IAntiforgery af) =>
{
    if (!await af.IsRequestValidAsync(ctx)) return Results.BadRequest();
    await ctx.SignOutAsync();
    return Results.Redirect("/");
});
app.MapGet("/export/pdf", ExportEndpoints.Pdf);
app.MapGet("/export/excel", ExportEndpoints.Excel);

app.Run();
