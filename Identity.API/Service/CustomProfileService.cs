using System;
using System.Security.Claims;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Identity.API.Entity;
using IdentityModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.API.Service
{
    public class CustomProfileService : IProfileService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public CustomProfileService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }
        public Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            var user = _userManager.GetUserAsync(context.Subject).Result ?? throw new ArgumentException("Invalid subject identifier");
            var claims = GetClaims(user).Result;
            // add user claims to context
            context.IssuedClaims.AddRange(claims);
            return Task.CompletedTask;
        }

        public async Task<List<Claim>> GetClaims(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtClaimTypes.Subject, user.Id),
                new Claim(JwtClaimTypes.Name, user.UserName??""),
                new Claim(JwtClaimTypes.Email, user.Email??""),
            };

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                claims.Add(new Claim(JwtClaimTypes.Role, role));
            }
            return claims;
        }

        public Task IsActiveAsync(IsActiveContext context)
        {
            // get current user
            var user = _userManager.GetUserAsync(context.Subject).Result ?? throw new ArgumentException("Invalid subject identifier");
            // check if user is active
            context.IsActive = user != null;
            return Task.CompletedTask;
        }
    }
}
