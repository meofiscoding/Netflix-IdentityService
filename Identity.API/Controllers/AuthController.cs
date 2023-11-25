using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Identity.API.Models.Auth;
using Identity.API.Entity;
using Microsoft.AspNetCore.Authorization;
using Identity.API.Utils;
using Microsoft.AspNetCore.Authentication;
using Duende.IdentityServer.Stores;
using Duende.IdentityServer;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Identity.API.Controllers;

[AllowAnonymous]
public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IIdentityServerInteractionService _interactionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IClientStore _clientStore;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        IIdentityServerInteractionService interactionService,
        UserManager<ApplicationUser> userManager,
        IAuthenticationSchemeProvider schemeProvider,
        IClientStore clientStore,
        ILogger<AuthController> logger)
    {
        _signInManager = signInManager;
        _interactionService = interactionService;
        _userManager = userManager;
        _schemeProvider = schemeProvider;
        _clientStore = clientStore;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string returnUrl)
    {
        if (TempData["ErrorMessage"] != null)
        {
            ViewBag.ErrorMessage = TempData["ErrorMessage"];
        }

        // build a model so we know what to show on the login page
        var vm = await BuildLoginViewModelAsync(returnUrl);

        if (vm.IsExternalLoginOnly)
        {
            // we only have one option for logging in and it's an external provider
            return RedirectToAction("Challenge", "External", new { scheme = vm.ExternalLoginScheme, returnUrl });
        }
        return View(vm);
    }

    private async Task<LoginViewModel> BuildLoginViewModelAsync(string returnUrl)
    {
        var context = await _interactionService.GetAuthorizationContextAsync(returnUrl);
        if (context?.IdP != null && await _schemeProvider.GetSchemeAsync(context.IdP) != null)
        {
            var local = context.IdP == IdentityServerConstants.LocalIdentityProvider;

            // this is meant to short circuit the UI and only trigger the one external IdP
            var vm = new LoginViewModel
            {
                EnableLocalLogin = local,
                ReturnUrl = returnUrl,
                Email = context?.LoginHint,
            };

            if (!local)
            {
                vm.ExternalProviders = new[] { new ExternalProvider { AuthenticationScheme = context.IdP } };
            }

            return vm;
        }

        var schemes = await _schemeProvider.GetAllSchemesAsync();

        var providers = schemes
            .Where(x => x.DisplayName != null)
            .Select(x => new ExternalProvider
            {
                DisplayName = x.DisplayName ?? x.Name,
                AuthenticationScheme = x.Name
            }).ToList();

        var allowLocal = true;
        if (context?.Client.ClientId != null)
        {
            var client = await _clientStore.FindEnabledClientByIdAsync(context.Client.ClientId);
            if (client != null)
            {
                allowLocal = client.EnableLocalLogin;

                if (client.IdentityProviderRestrictions?.Any() == true)
                {
                    providers = providers.Where(provider => client.IdentityProviderRestrictions.Contains(provider.AuthenticationScheme)).ToList();
                }
            }
        }

        return new LoginViewModel
        {
            AllowRememberLogin = AccountOptions.AllowRememberLogin,
            EnableLocalLogin = allowLocal && AccountOptions.AllowLocalLogin,
            ReturnUrl = returnUrl,
            Email = context?.LoginHint ?? string.Empty,
            ExternalProviders = providers.ToArray()
        };
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        // get tenant info// check if the model is valid
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user == null)
            {
                ModelState.AddModelError("Email", "Email is not valid");
                _logger.LogWarning($"Email {vm.Email} is not registered");
                return View(vm);
            }
            if (!await _userManager.CheckPasswordAsync(user, vm.Password))
            {
                ModelState.AddModelError("Password", "Password is not valid");
                _logger.LogWarning($"Password is not valid for {vm.Email}");
                return View(vm);
            }

            var signInResult = _signInManager.PasswordSignInAsync(user, vm.Password, false, false).Result;
            if (signInResult.Succeeded)
            {
                // redirect to the return url
                if (vm.ReturnUrl != null)
                {
                    _logger.LogWarning($"Route to {vm.ReturnUrl}");
                    return Redirect(vm.ReturnUrl);
                }
                else
                {
                    _logger.LogWarning("ReturnUrl is null");
                    return View();
                }
            }

        }
        _logger.LogWarning("Model is invalid");
        return Redirect(vm.ReturnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Logout(string logoutId)
    {
        await _signInManager.SignOutAsync();

        var logoutRequest = await _interactionService.GetLogoutContextAsync(logoutId);

        if (string.IsNullOrEmpty(logoutRequest.PostLogoutRedirectUri))
        {
            return RedirectToAction("Index", "Home");
        }

        return Redirect(logoutRequest.PostLogoutRedirectUri);
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new RegistrationResponseDto
            {
                IsSuccessfulRegistration = false,
                Errors = ModelState.Values.SelectMany(v => v.Errors.Select(e => e.ErrorMessage))
            });
        }

        var userByEmail = await _userManager.FindByEmailAsync(vm.Email!);
        if (userByEmail is not null)
        {
            return BadRequest(new RegistrationResponseDto
            {
                IsSuccessfulRegistration = false,
                Errors = new[] { "Email already in use" }
            });
        }

        //string username = vm.Email.Split('@')[0];
        //int count = await _userManager.Users
        //    .Where(u => u.Email != null && u.Email.Contains(username))
        //    .CountAsync();

        var user = new ApplicationUser
        {
            // UserName = username + (count > 0 ? count.ToString() : ""),
            UserName = vm.Email,
            Email = vm.Email
        };

        var result = await _userManager.CreateAsync(user, vm.Password);
        await _userManager.AddToRoleAsync(user, UserRoles.User);

        if (!result.Succeeded)
        {
            return BadRequest(new RegistrationResponseDto
            {
                IsSuccessfulRegistration = false,
                Errors = result.Errors.Select(e => e.Description)
            });
        }

        await _signInManager.SignInAsync(user, false);

        return StatusCode(201, new RegistrationResponseDto
        {
            IsSuccessfulRegistration = true
        });
    }

}