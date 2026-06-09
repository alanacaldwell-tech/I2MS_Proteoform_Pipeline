using IonCounterWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IonCounterService>();

// Allow large file uploads (dmt files can be several hundred MB).
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; // 2 GB
});

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024;
});

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<IonCounterWeb.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
