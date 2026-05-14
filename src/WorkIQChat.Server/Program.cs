using System.Diagnostics;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

using Serilog;

using WorkIQChat.Data.Interfaces;
using WorkIQChat.Data.Models;
using WorkIQChat.Server.Components;
using WorkIQChat.Server.Components.Account;
using WorkIQChat.Server.Components.Email;
using WorkIQChat.Server.Hubs;
using WorkIQChat.Server.Services;
using WorkIQChat.ServiceDefaults;
using WorkIQChat.Shared;

Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("Starting up");

var isMigrations = Environment.GetCommandLineArgs()[0].Contains("ef.dll");

try
{
    var builder = WebApplication.CreateBuilder(args);

    if (!isMigrations)
    {
        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration));
    }

    builder.AddServiceDefaults();

    builder.Services.AddSignalR();

    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents()
        .AddAuthenticationStateSerialization();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();

    // use this version for Azure OpenAI
    builder.AddAzureOpenAIClient("ai-model")
        .AddChatClient("gpt-5.1-chat")
        .UseFunctionInvocation();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();

    // Register Microsoft Entra external login when AzureAd:ClientId is configured.
    // This allows the app to request a Work IQ-scoped access token (WorkIQAgent.Ask)
    // during the sign-in flow, which the ChatHub then uses to call the Work IQ API.
    // To enable: register an Entra app with the WorkIQAgent.Ask delegated permission
    // and set AzureAd:TenantId, AzureAd:ClientId, and AzureAd:ClientSecret in configuration.
    // See: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-quickstart
    var azureAdSection = builder.Configuration.GetSection("AzureAd");
    if (!string.IsNullOrEmpty(azureAdSection["ClientId"]))
    {
        builder.Services.AddAuthentication()
            .AddOpenIdConnect("Microsoft", "Microsoft", options =>
            {
                // Use the tenant-specific v2.0 authority for proper Entra delegated auth.
                options.Authority = $"https://login.microsoftonline.com/{azureAdSection["TenantId"]}/v2.0";
                options.ClientId = azureAdSection["ClientId"]!;
                options.ClientSecret = azureAdSection["ClientSecret"];
                options.ResponseType = "code";

                // SaveTokens persists the access_token in the authentication cookie so it
                // can be retrieved later via HttpContext.GetTokenAsync("access_token").
                options.SaveTokens = true;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                // offline_access allows token refresh without re-prompting the user.
                options.Scope.Add("offline_access");
                // Request the Work IQ scope so the issued access_token can be used
                // directly with the Work IQ API (audience: api://workiq.svc.cloud.microsoft).
                options.Scope.Add("api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask");

                options.CallbackPath = "/signin-microsoft";
            });
    }

    builder.Services.AddAuthorization();

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString(Constants.DatabaseConnectionString))
            .EnableSensitiveDataLogging());
    builder.EnrichSqlServerDbContext<ApplicationDbContext>();
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    builder.Services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedAccount = true;

            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
        // AddRoles isn't added from the AddIdentityCore, so if you want to use roles, this must be explicitly added
        .AddRoles<Role>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();
    builder.Services.AddScoped<IUserService, HttpUserService>();

    // Register the Work IQ service and its named HttpClient.
    // The named client sets the base address and the A2A-Version header required by the
    // Work IQ Gateway (https://workiq.svc.cloud.microsoft/a2a/).
    // See: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-api-overview
    builder.Services.AddHttpClient(WorkIQService.HttpClientName, client =>
    {
        client.BaseAddress = new Uri("https://workiq.svc.cloud.microsoft/a2a/");
        // A2A-Version: 1.0 enables v1.0 method names (SendMessage) and wire format.
        // Omitting this header causes the gateway to default to the v0.3 protocol.
        client.DefaultRequestHeaders.Add("A2A-Version", "1.0");
        client.Timeout = TimeSpan.FromMinutes(2);
    });
    builder.Services.AddScoped<IWorkIQService, WorkIQService>();

    // Add route configuration to enforce lowercase URLs for better SEO
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.LowercaseUrls = true;
        options.LowercaseQueryStrings = true;
        options.AppendTrailingSlash = false;
    });

    var app = builder.Build();

    app.MapDefaultEndpoints();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.UseHttpsRedirection();
    app.UseAntiforgery();
    app.MapStaticAssets();

    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(WorkIQChat.Client._Imports).Assembly);

    app.MapHub<ChatHub>(ChatHubConstants.HubUrl);

    app.MapAdditionalIdentityEndpoints();

    app.Run();
}
catch (Exception ex) when (ex.GetType().Name is not "StopTheHostException" &&
                           ex.GetType().Name is not "HostAbortedException")
{
    Log.Fatal(ex, "Unhandled exception.");
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}