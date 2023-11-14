using System;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Identity.API.Entity;
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
            // get current user
            var user = _userManager.GetUserAsync(context.Subject).Result?? throw new ArgumentException("Invalid subject identifier");
            // get all user claims 
            var claims = _userManager.GetClaimsAsync(user).Result;
            // add user claims to context
            context.IssuedClaims.AddRange(claims);
            return Task.CompletedTask;
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
