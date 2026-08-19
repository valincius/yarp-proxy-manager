using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using ProxyManager.Api.Middleware;
using ProxyManager.Api.Routing;
using ProxyManager.Application;
using ProxyManager.Application.ApiKeys;
using ProxyManager.Application.Certificates;
using ProxyManager.Application.Proxy;
using ProxyManager.Application.ProxyHosts;
using ProxyManager.Application.Redirects;
using ProxyManager.Application.Streams;
using ProxyManager.Certificates;
using ProxyManager.Certificates.Acme;
using ProxyManager.Infrastructure.Dns;
using ProxyManager.Infrastructure.Persistence;
using ProxyManager.Proxy;
using ProxyManager.Streams;
using Serilog;
using Yarp.ReverseProxy.Configuration;

// --- Entry point ---
var app = Program.BuildApp(args);
await Program.InitializeAsync(app);
await app.RunAsync();

public partial class Program
{
    /// <summary>
    /// Builds the application (services, Kestrel endpoints, pipelines). Exposed so tests can
    /// host the real production pipeline on Kestrel with injected configuration.
    /// </summary>
    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- Logging (Serilog: console + optional rolling file under the data directory) ---
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(DataDirectory(builder), "logs", "proxy-manager-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        builder.Host.UseSerilog();

        // --- Data directory ---
        var dataDir = DataDirectory(builder);
        Directory.CreateDirectory(Path.Combine(dataDir, "logs"));

        // --- Data Protection (encrypts certificate passwords, DNS tokens, the ACME key) ---
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "dp-keys")));

        // --- Database ---
        var connectionString = builder.Configuration.GetConnectionString("ProxyDb")
            ?? $"Data Source={Path.Combine(dataDir, "proxy.db")}";
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<ProxyDbContext>((services, options) =>
            options.UseSqlite(connectionString)
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));

        // --- Identity (local users, cookie auth; OIDC arrives in a later phase) ---
        builder.Services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 5;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ProxyDbContext>()
            .AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "yarp_manager";
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });

        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.HttpOnly = false; // the SPA must read this cookie to echo the header
        });

        // --- MVC + OpenAPI ---
        builder.Services.AddControllers();
        builder.Services.Configure<MvcOptions>(options =>
            options.Conventions.Add(new RequireAdminPortConvention()));
        builder.Services.AddOpenApi();

        // --- Application services ---
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ProxyConfigReloader>();
        builder.Services.AddScoped<IProxyHostRepository, ProxyHostRepository>();
        builder.Services.AddScoped<IProxyConfigStore, ProxyConfigStore>();
        builder.Services.AddScoped<ProxyHostValidator>();
        builder.Services.AddScoped<ProxyHostService>();

        // --- Redirects / access lists / audit ---
        builder.Services.AddSingleton<HostPolicyIndex>();
        builder.Services.AddSingleton<RedirectIndex>();
        builder.Services.AddScoped<IRedirectHostRepository, RedirectHostRepository>();
        builder.Services.AddScoped<IAccessListRepository, AccessListRepository>();
        builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        builder.Services.AddScoped<IAccessListStore, AccessListStore>();
        builder.Services.AddScoped<IRedirectStore, RedirectStore>();
        builder.Services.AddScoped<RedirectHostValidator>();
        builder.Services.AddScoped<AccessListValidator>();
        builder.Services.AddScoped<RedirectHostService>();
        builder.Services.AddScoped<AccessListService>();

        // --- REST API keys ---
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<CreateApiKeyValidator>();
        builder.Services.AddScoped<ApiKeyService>();

        // --- Certificates subsystem ---
        builder.Services.AddHttpClient("CloudflareDns", client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddSingleton<ForceHttpsIndex>();
        builder.Services.AddSingleton<CertificateFileStore>(_ => new CertificateFileStore(dataDir));
        builder.Services.AddSingleton<Http01ChallengeStore>();
        builder.Services.AddSingleton<SniCertificateSelector>();
        builder.Services.AddScoped<ICertificateRepository, CertificateRepository>();
        builder.Services.AddScoped<ISecretProtector, SecretProtector>();
        builder.Services.AddScoped<IDnsChallengeProviderFactory, DnsChallengeProviderFactory>();
        builder.Services.AddTransient<IAcmeClient, CertesAcmeClient>();
        builder.Services.AddScoped<IssueCertificateValidator>();
        builder.Services.AddScoped<UploadCertificateValidator>();
        builder.Services.AddScoped<DnsCredentialValidator>();
        builder.Services.AddScoped<AcmeSettingsValidator>();
        builder.Services.AddScoped<CertificateManager>();
        builder.Services.AddHostedService<CertificateRenewalWorker>();

        // --- Kestrel HTTPS with SNI certificate selection ---
        // --- Streams (TCP/UDP) ---
        builder.Services.AddSingleton<StreamStatusRegistry>();
        builder.Services.AddSingleton<StreamListenerFactory>();
        builder.Services.AddSingleton<StreamHostService>();
        builder.Services.AddSingleton<IReservedPortsProvider>(new ReservedPortsProvider(builder.Configuration));
        builder.Services.AddScoped<IStreamRepository, StreamRepository>();
        builder.Services.AddScoped<StreamValidator>();
        builder.Services.AddScoped<StreamService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<StreamHostService>());

        // The config reload notifier fans out to both the YARP reloader and the stream host.
        builder.Services.AddSingleton<IConfigReloadNotifier>(sp => new CompositeReloadNotifier(
            new ConfigReloadNotifier(sp.GetRequiredService<ProxyConfigReloader>()),
            new StreamChangeNotifier(sp.GetRequiredService<StreamHostService>())));

        // --- YARP (empty initial config; ProxyConfigReloader swaps in the real routes) ---
        builder.Services.AddReverseProxy().LoadFromMemory([], []);

        // --- Metrics (Prometheus; includes YARP's built-in meters) ---
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("Yarp.ReverseProxy")
                .AddPrometheusExporter());

        // --- OIDC external login (optional; enabled by configuring Oidc:Authority) ---
        var oidcAuthority = builder.Configuration["Oidc:Authority"];
        if (!string.IsNullOrWhiteSpace(oidcAuthority))
        {
            builder.Services.AddAuthentication()
                .AddOpenIdConnect("oidc", options =>
                {
                    options.Authority = oidcAuthority;
                    options.ClientId = builder.Configuration["Oidc:ClientId"]
                        ?? throw new InvalidOperationException("Oidc:ClientId is required when OIDC is enabled.");
                    options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                    options.SaveTokens = true;
                });
        }

        configure?.Invoke(builder);

        // --- Kestrel HTTPS with SNI certificate selection ---
        // The selector is resolved from the app's own service provider (assigned after
        // Build) so the Kestrel callback and the rest of the app share one instance.
        SniCertificateSelector? sniSelector = null;
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificateSelector = (connectionContext, name) =>
                    sniSelector?.Select(connectionContext, name);
            });
        });

        var app = builder.Build();
        sniSelector = app.Services.GetRequiredService<SniCertificateSelector>();

        // --- Static file root: use the built frontend when present next to the source tree,
        //     otherwise the published wwwroot (Docker copies dist/client there). ---
        var cwd = Directory.GetCurrentDirectory();
        var webDistCandidates = new[]
        {
            Path.GetFullPath(Path.Combine(cwd, "web", "dist", "client")),             // app run from the repo root
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "web", "dist", "client")), // app run from src/ProxyManager.Api
            Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist", "client")),
        };
        var webDist = webDistCandidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
        var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        IFileProvider staticFileProvider;
        if (webDist.Length > 0)
        {
            staticFileProvider = new PhysicalFileProvider(webDist);
        }
        else if (Directory.Exists(wwwroot))
        {
            staticFileProvider = new PhysicalFileProvider(wwwroot);
        }
        else
        {
            // No frontend build present (fresh dev checkout / tests): serve nothing.
            staticFileProvider = new NullFileProvider();
        }

        var staticFileOptions = new StaticFileOptions { FileProvider = staticFileProvider };
        app.Logger.LogInformation(
            "Serving admin UI from {StaticRoot} (cwd: {Cwd}, contentRoot: {ContentRoot})",
            staticFileProvider is NullFileProvider ? "(none)" : webDist.Length > 0 ? webDist : wwwroot,
            Directory.GetCurrentDirectory(),
            app.Environment.ContentRootPath);

        var adminPort = GetAdminPort(builder.Configuration);

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<AcmeChallengeResponderMiddleware>();

        // --- Pipeline layout (two Kestrel endpoint groups, one pipeline) ---
        // 1. Controller routing is global but restricted to the admin port by AdminPortConvention,
        //    so proxy-port requests always fall through to YARP (host-based matching) below.
        // 2. The admin port additionally serves static files, OpenAPI and the SPA fallback.
        // 3. The proxy port runs the YARP reverse proxy.

        // Step 1 — controllers (admin port only via route constraint).
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            // Unmatch controller endpoints on the proxy port so the request falls
            // through to the YARP branch below (proxied hosts may serve /api paths).
            var endpoint = context.GetEndpoint();
            if (endpoint is not null
                && endpoint.Metadata.GetMetadata<RequireAdminPortMetadata>() is not null
                && !IsAdminPort(context, adminPort))
            {
                context.SetEndpoint(null);
            }

            await next(context);
        });
        app.UseWhen(ctx => IsAdminPort(ctx, adminPort), admin =>
        {
            admin.UseMiddleware<ApiKeyAuthenticationMiddleware>();
            admin.UseAuthentication();
            admin.UseAuthorization();
            admin.UseMiddleware<AntiforgeryValidationMiddleware>();
        });
        app.UseEndpoints(endpoints => endpoints.MapControllers());

        // Step 2 — admin UI: static files + OpenAPI + SPA fallback (admin port only).
        app.UseWhen(ctx => IsAdminPort(ctx, adminPort), admin =>
        {
            admin.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticFileOptions.FileProvider });
            admin.UseStaticFiles(staticFileOptions);
            admin.UseRouting();
            admin.UseEndpoints(endpoints =>
            {
                endpoints.MapOpenApi();
                endpoints.MapPrometheusScrapingEndpoint();
                endpoints.MapFallbackToFile("index.html", staticFileOptions);
            });
        });

        // Step 3 — reverse proxy (proxy port only).
        var httpsPort = GetEndpointPort(builder.Configuration, "Kestrel:Endpoints:Https:Url", 443);
        app.UseWhen(ctx => !IsAdminPort(ctx, adminPort), proxy =>
        {
            proxy.UseMiddleware<RedirectMiddleware>();
            proxy.UseMiddleware<AccessListMiddleware>();
            proxy.UseMiddleware<ExploitBlockMiddleware>();
            proxy.UseMiddleware<ForceHttpsRedirectMiddleware>(httpsPort);
            proxy.UseRouting();
            proxy.UseEndpoints(endpoints => endpoints.MapReverseProxy());
        });

        return app;
    }

    private static bool IsAdminPort(HttpContext context, int adminPort)
        => context.Connection.LocalPort == adminPort || context.Connection.LocalPort == 0;

    /// <summary>Fans out config-reload notifications to every subsystem that maintains a runtime projection.</summary>
    private sealed class CompositeReloadNotifier(params IConfigReloadNotifier[] notifiers) : IConfigReloadNotifier
    {
        public void Notify()
        {
            foreach (var notifier in notifiers)
            {
                notifier.Notify();
            }
        }
    }

    /// <summary>The proxy's own listening ports; streams must not collide with them.</summary>
    private sealed class ReservedPortsProvider(IConfiguration configuration) : IReservedPortsProvider
    {
        public IReadOnlyList<int> Ports { get; } =
        [
            GetEndpointPort(configuration, "Kestrel:Endpoints:ProxyHttp:Url", 80),
            GetEndpointPort(configuration, "Kestrel:Endpoints:Https:Url", 443),
            GetEndpointPort(configuration, "Kestrel:Endpoints:Admin:Url", 81),
        ];
    }

    /// <summary>Runs startup work: applies migrations, seeds the admin user, loads proxy config + certificates.</summary>
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        await MigrateAndSeedAsync(services);
        await services.GetRequiredService<ProxyConfigReloader>().ReloadAsync();
        await services.GetRequiredService<SniCertificateSelector>().ReloadAsync();
    }

    private static string DataDirectory(WebApplicationBuilder builder) =>
        builder.Configuration["Data:Directory"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "data");

    private static int GetAdminPort(IConfiguration configuration) =>
        GetEndpointPort(configuration, "Kestrel:Endpoints:Admin:Url", 81);

    private static int GetEndpointPort(IConfiguration configuration, string urlKey, int defaultPort)
    {
        var url = configuration[urlKey];
        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Port > 0
            ? uri.Port
            : defaultPort;
    }

    private static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ProxyDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>("Admin"));
        }

        if (!await roleManager.RoleExistsAsync("User"))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>("User"));
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var email = configuration["Admin:Email"] ?? "admin@example.com";
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var password = configuration["Admin:Password"] ?? "changeme";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = "Administrator",
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, password);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");
            if (configuration["Admin:Password"] is null)
            {
                logger.LogWarning(
                    "Created the default admin account '{Email}' with the default password. " +
                    "Set the Admin:Password configuration value (ADMIN_PASSWORD) and change it immediately.",
                    email);
            }
            else
            {
                logger.LogInformation("Created the admin account '{Email}'.", email);
            }
        }
        else
        {
            logger.LogError("Failed to create the admin account: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
