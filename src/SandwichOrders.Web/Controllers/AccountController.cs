using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using SandwichOrders.Core;
using SandwichOrders.Web.Infrastructure;
using SandwichOrders.Web.Models;

namespace SandwichOrders.Web.Controllers;

// US-03: create an account and sign in. Every new account is a Customer; staff accounts are prepared in advance.
public class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IStringLocalizer<SharedResource> L) : Controller
{
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    [EnableRateLimiting(RateLimits.Forms)]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await users.FindByEmailAsync(model.Email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), L["An account with this email already exists."].Value);
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            DisplayName = model.DisplayName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim()
        };

        var created = await users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            // Identity's own messages (for example "at least 8 characters") are shown in English.
            foreach (var error in created.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await users.AddToRoleAsync(user, Roles.Customer);
        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Menu");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [EnableRateLimiting(RateLimits.Forms)]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, L["Too many wrong passwords. This account is locked for 5 minutes."].Value);
            return View(model);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, L["Invalid email or password."].Value);
            return View(model);
        }

        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Menu");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Menu");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // Every signed-in user (also the canteen employee, whose first password comes from the deployment settings)
    // can choose a new password. The current password is asked again, so an unlocked phone is not enough.
    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimits.Forms)]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var changed = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!changed.Succeeded)
        {
            foreach (var error in changed.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Code == "PasswordMismatch"
                    ? L["The current password is not correct."].Value
                    : error.Description);
            }

            return View(model);
        }

        // The security stamp changed: sign in again so this browser stays signed in (other sessions are not).
        await signInManager.RefreshSignInAsync(user);
        TempData["Success"] = L["Your password has been changed."].Value;
        return RedirectToAction("Index", "Menu");
    }
}
