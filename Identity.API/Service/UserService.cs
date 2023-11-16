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

            var result = request.Success ? await _userManager.AddToRoleAsync(user, "PayingUser") : await _userManager.RemoveFromRoleAsync(user, "PayingUser");

            return new PaymentResponse
            {
                Success = result.Succeeded,
                Message = result.Succeeded ? "User updated" : $"User can not be updated due to {result.Errors}"
            };
        }

    }
}
