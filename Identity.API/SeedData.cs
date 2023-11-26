using System;
using System.Security.Claims;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Mappers;
using Identity.API.Configuration;
using Identity.API.Data;
using Identity.API.Entity;
using IdentityModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Polly;

namespace Identity.API
{
    public static class SeedData
    {
        public static async Task EnsureSeedData(IServiceScope scope, IConfiguration configuration, ILogger logger)
        {
            var retryPolicy = CreateRetryPolicy(configuration, logger);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await retryPolicy.ExecuteAsync(async () =>
                {
                    context.Database.Migrate();

                    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    // for each const string in UserRoles, create a role if it doesn't exist
                    foreach (var role in (List<string>)typeof(UserRoles).GetFields().Select(x => x.GetValue(null).ToString()).ToList())
                    {
                        var roleExist = await roleMgr.RoleExistsAsync(role);
                        if (!roleExist)
                        {
                            var result = await roleMgr.CreateAsync(new IdentityRole(role));
                            if (!result.Succeeded)
                            {
                                throw new Exception(result.Errors.First().Description);
                            }
                        }
                    }

                    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                    var testUser = await userMgr.FindByNameAsync("test");

                    if (testUser == null)
                    {
                        testUser = new ApplicationUser
                        {
                            Id = Guid.NewGuid().ToString(),
                            Email = "test@local",
                            EmailConfirmed = true,
                            UserName = "test"
                        };

                        var result = userMgr.CreateAsync(testUser, "Pass@word1").Result;

                        if (!result.Succeeded)
                        {
                            throw new Exception(result.Errors.First().Description);
                        }

                        result = userMgr.AddClaimsAsync(testUser, new Claim[]{
                            new Claim(JwtClaimTypes.Name, "Test User"),
                            new Claim(JwtClaimTypes.GivenName, "Test"),
                            new Claim(JwtClaimTypes.FamilyName, "User"),
                            new Claim(JwtClaimTypes.WebSite, "http://test.com"),
                        }).Result;

                        if (!result.Succeeded)
                        {
                            throw new Exception(result.Errors.First().Description);
                        }
                        logger.LogDebug("test@local created");
                    }
                    else
                    {
                        logger.LogDebug("test@local already exists");
                    }

                });
        }

        public static async Task InitializeDatabaseAsync(IServiceScope scope, IConfiguration configuration, ILogger logger)
        {
            var retryPolicy = CreateRetryPolicy(configuration, logger);
            var context = scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>();

            await retryPolicy.ExecuteAsync(async () =>
                {
                    context.Database.Migrate();
                    try
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
                        context.Database.Migrate();

                        context.RemoveRange(context.Clients);
                        context.RemoveRange(context.IdentityResources);
                        context.RemoveRange(context.ApiScopes);
                        context.RemoveRange(context.ApiResources);

                        foreach (var client in Config.Clients)
                        {
                            context.Clients.Add(client.ToEntity());
                        }

                        foreach (var resource in Config.IdentityResources)
                        {
                            context.IdentityResources.Add(resource.ToEntity());
                        }

                        foreach (var resource in Config.ApiScopes)
                        {
                            context.ApiScopes.Add(resource.ToEntity());
                        }

                        foreach (var resource in Config.ApiResources)
                        {
                            context.ApiResources.Add(resource.ToEntity());
                        }

                        context.SaveChanges();
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Exception during seeding PersistedGrantDbContext: {ex.Message}");
                    }

                });
        }

        private static AsyncPolicy CreateRetryPolicy(IConfiguration configuration, ILogger logger)
        {
            var retryMigrations = false;
            bool.TryParse(configuration["RetryMigrations"], out retryMigrations);

            // Only use a retry policy if configured to do so.
            // When running in an orchestrator/K8s, it will take care of restarting failed services.
            if (retryMigrations)
            {
                return Policy.Handle<Exception>().
                    WaitAndRetryForeverAsync(
                        sleepDurationProvider: retry => TimeSpan.FromSeconds(5),
                        onRetry: (exception, retry, timeSpan) => logger.LogWarning(exception, "Error migrating database (retry attempt {retry})", retry));
            }

            return Policy.NoOpAsync();
        }
    }
}
