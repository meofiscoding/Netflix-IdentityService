using Identity.API.Configuration;
using Identity.API.Data;
using Identity.API.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Duende.IdentityServer;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Identity.API.Service;
using Identity.API;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinRequestBodyDataRate = null;

    options.ListenAnyIP(5286,
          listenOptions => { listenOptions.Protocols = HttpProtocols.Http1; });

    options.ListenAnyIP(50050,
       listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
});
var configuration = builder.Configuration;

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("IdentityDB") ?? configuration.GetConnectionString("AZURE_POSTGRESQL_CONNECTIONSTRING")));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();

// Add Google support
builder.Services.AddAuthentication().AddGoogle("Google", options =>
{
    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
    // Uncomment this when in development
    options.ClientId = configuration["Google:ClientId"];
    options.ClientSecret = configuration["Google:ClientSecret"];
});

// Add Grpc Service
builder.Services.AddGrpc();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(config =>
    {
        config.Password.RequiredLength = 6;
        config.Password.RequireDigit = true;
        config.Password.RequireNonAlphanumeric = true;
        config.Password.RequireUppercase = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var migrationsAssembly = typeof(Program).Assembly.GetName().Name;
builder.Services.AddIdentityServer(option =>
    {
        option.Events.RaiseErrorEvents = true;
        option.Events.RaiseInformationEvents = true;
        option.Events.RaiseFailureEvents = true;
        option.Events.RaiseSuccessEvents = true;

        // see https://docs.duendesoftware.com/identityserver/v6/fundamentals/resources/
        option.EmitStaticAudienceClaim = true;
        option.KeyManagement.KeyPath = "/home/shared/key";
        //// new key every 30 days
        option.KeyManagement.RotationInterval = TimeSpan.FromDays(30);

        //// announce new key 2 days in advance in discovery
        option.KeyManagement.PropagationTime = TimeSpan.FromDays(2);

        //// keep old key for 7 days in discovery for validation of tokens
        option.KeyManagement.RetentionDuration = TimeSpan.FromDays(7);

        //// don't delete keys after their retention period is over
        option.KeyManagement.DeleteRetiredKeys = false;
        if (builder.Environment.IsDevelopment())
        {
            option.IssuerUri = configuration["IdentityUrl"];
        }
    }
)
//.AddInMemoryClients(Config.Clients)
//.AddInMemoryIdentityResources(Config.IdentityResources)
//.AddInMemoryApiResources(Config.ApiResources)
//.AddInMemoryApiScopes(Config.ApiScopes)
.AddOperationalStore(options =>
{
    options.ConfigureDbContext = builder => builder.UseNpgsql(configuration.GetConnectionString("IdentityDB") ?? configuration.GetConnectionString("AZURE_POSTGRESQL_CONNECTIONSTRING"),
        sql => sql.MigrationsAssembly(migrationsAssembly));
})
.AddConfigurationStore(options =>
{
    options.ConfigureDbContext = builder => builder.UseNpgsql(configuration.GetConnectionString("IdentityDB") ?? configuration.GetConnectionString("AZURE_POSTGRESQL_CONNECTIONSTRING"),
        sql => sql.MigrationsAssembly(migrationsAssembly));
})
.AddAspNetIdentity<ApplicationUser>()
.AddProfileService<CustomProfileService>();
// .AddSigningCredential(GetIdentityServerCertificate());
// .AddDeveloperSigningCredential(); // Not recommended for production - you need to store your key material somewhere secure

builder.Services.ConfigureApplicationCookie(config =>
{
    config.Cookie.Name = "IdentityServer.Cookie";
    config.Cookie.SameSite = SameSiteMode.None;
    config.Cookie.IsEssential = true;
    //config.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    config.LoginPath = "/Auth/Login";
    config.LogoutPath = "/Auth/Logout";
});

// add CORS rule
builder.Services.AddCors();

var app = builder.Build();
// Map Grpc Service
app.MapGrpcService<UserService>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // app.UseForwardedHeaders(new ForwardedHeadersOptions
    // {
    //     ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    // });
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.Use(async (ctx, next) =>
    {
        ctx.SetIdentityServerOrigin(configuration["IdentityUrl"]);
        await next();
    });
}

app.UseCors(builder =>
{
    builder.AllowAnyOrigin();
    builder.AllowAnyHeader();
    builder.AllowAnyMethod();
});

// This cookie policy fixes login issues with Chrome 80+ using HHTP
app.UseCookiePolicy();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseIdentityServer();

app.MapDefaultControllerRoute();

InitializeDatabase(app);

static void InitializeDatabase(IApplicationBuilder app)
{
    var serviceScope = app.ApplicationServices?.GetService<IServiceScopeFactory>()?.CreateScope();
    
    if(serviceScope == null)
    {
        throw new System.Exception("Could not create service scope");
    }

    serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

    var context = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
    context.Database.Migrate();
    if (!context.Clients.Any())
    {
        foreach (var client in Config.Clients)
        {
            context.Clients.Add(client.ToEntity());
        }
        context.SaveChanges();
    }

    if (!context.IdentityResources.Any())
    {
        foreach (var resource in Config.IdentityResources)
        {
            context.IdentityResources.Add(resource.ToEntity());
        }
        context.SaveChanges();
    }

    if (!context.ApiScopes.Any())
    {
        foreach (var resource in Config.ApiScopes)
        {
            context.ApiScopes.Add(resource.ToEntity());
        }
      context.SaveChanges();
       
    }
   if (!context.ApiResources.Any())
        {
            foreach (var resource in Config.ApiResources)
            {
                context.ApiResources.Add(resource.ToEntity());
            }
            context.SaveChanges();
        }
}


// Apply database migration automatically. Note that this approach is not
// recommended for production scenarios. Consider generating SQL scripts from
// migrations instead.
using (var scope = app.Services.CreateScope())
{
    await SeedData.EnsureSeedData(scope, app.Configuration, app.Logger);
}

// X509Certificate2 GetIdentityServerCertificate()
// {
//     string keyVaultUrl = configuration["KeyVault:AzureKeyVaultURL"];
//     string clientId = configuration["KeyVault:ClientId"];
//     string clientSecret = configuration["KeyVault:ClientSecret"];
//     string tenantId = configuration["KeyVault:AzureClientTenantId"];

//     // Create a new SecretClient using ClientSecretCredential​
//     var client = new SecretClient(new Uri(keyVaultUrl), new ClientSecretCredential(tenantId, clientId, clientSecret));

//     try
//     {
//         // get certificate from key vault
//         var secret = client.GetSecret("IdenittyServer4Certificate");
//         var pemContent = secret.Value.Value;
//         // Separate the private key and certificate portions
//         string[] pemParts = pemContent.Split(new string[] { "-----END PRIVATE KEY-----" }, StringSplitOptions.RemoveEmptyEntries);
//         string privateKeyPem = pemParts[0] + "-----END PRIVATE KEY-----";
//         string certificatePem = pemParts[1];

//         // Load the private key into a RSA private key
//         var privateKey = RSA.Create();
//         privateKey.ImportFromPem(privateKeyPem);

//         // Load the certificate
//         var certificate = new X509Certificate2(Encoding.UTF8.GetBytes(certificatePem));

//         // Attach the private key to the certificate
//         return certificate.CopyWithPrivateKey(privateKey);
//     }
//     catch (System.Exception ex)
//     {
//         // handle case where certificate is not found in key vault
//         throw new System.Exception(ex.Message);
//     }
// }

await app.RunAsync();

