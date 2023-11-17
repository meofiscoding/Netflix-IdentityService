using System;
using Identity.API.Entity;
using Microsoft.AspNetCore.Identity;
using Identity.API.GrpcService.Protos;
using Grpc.Core;

namespace Identity.API.Service
{
    public class UserService : PaymentProtoService.PaymentProtoServiceBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UserService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public override async Task<PaymentResponse> UpdateUserMembership(PaymentRequest request, ServerCallContext context)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                return new PaymentResponse
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            var result = new IdentityResult();
            if (request.Success)
            {
                // if user have role user, then add role user
                var oldRole = await _userManager.GetRolesAsync(user);
                if (oldRole.Contains(UserRoles.User))
                {
                    await _userManager.RemoveFromRoleAsync(user, UserRoles.User);
                }
                result = await _userManager.AddToRoleAsync(user, UserRoles.Member);
            }
            else
            {
                // if have role member, then remove role member 
                var oldRole = await _userManager.GetRolesAsync(user);
                if (oldRole.Contains(UserRoles.Member))
                {
                    await _userManager.RemoveFromRoleAsync(user, UserRoles.Member);
                }
                result = await _userManager.AddToRoleAsync(user, UserRoles.User);
            }
            return new PaymentResponse
            {
                Success = result.Succeeded,
                Message = result.Succeeded ? "User updated" : $"User can not be updated due to {result.Errors}"
            };
        }

    }
}
