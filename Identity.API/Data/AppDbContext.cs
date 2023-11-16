using System;
using Identity.API.Entity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Identity.API.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        protected readonly IConfiguration Configuration;
        public AppDbContext(DbContextOptions<AppDbContext> options, IConfiguration configuration) : base(options)
        {
            Configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder?.UseNpgsql(Configuration.GetConnectionString("IdentityDB")??Configuration.GetConnectionString("AZURE_POSTGRESQL_CONNECTIONSTRING"));
        }

        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    }
}
