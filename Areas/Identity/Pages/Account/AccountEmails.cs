using System.Text;
using System.Text.Encodings.Web;
using AmarTools.Voting.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AmarTools.Voting.Areas.Identity.Pages.Account
{
    /// <summary>Builds and sends the account emails used by Register, ResendEmailConfirmation and ForgotPassword.</summary>
    internal static class AccountEmails
    {
        public static string EncodeToken(string token) =>
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        public static string? DecodeToken(string? code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
            catch (FormatException) { return null; }
        }

        public static async Task SendConfirmationAsync(
            UserManager<ApplicationUser> userManager, IEmailSender emailSender,
            IUrlHelper url, string scheme, ApplicationUser user, string? returnUrl = null)
        {
            var code = EncodeToken(await userManager.GenerateEmailConfirmationTokenAsync(user));
            var link = url.Page("/Account/ConfirmEmail", pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code, returnUrl }, protocol: scheme)!;

            await emailSender.SendEmailAsync(user.Email!, "Confirm your AmarTools Voting account",
                Body($"Hello {user.FullName},",
                     "Please confirm your email address to activate your account.",
                     link, "Confirm my email",
                     "If you did not create this account, you can ignore this email."));
        }

        public static async Task SendPasswordResetAsync(
            UserManager<ApplicationUser> userManager, IEmailSender emailSender,
            IUrlHelper url, string scheme, ApplicationUser user)
        {
            var code = EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user));
            var link = url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { area = "Identity", code, email = user.Email }, protocol: scheme)!;

            await emailSender.SendEmailAsync(user.Email!, "Reset your AmarTools Voting password",
                Body($"Hello {user.FullName},",
                     "We received a request to reset your password.",
                     link, "Choose a new password",
                     "If you did not ask for this, you can ignore this email; your password stays the same."));
        }

        private static string Body(string greeting, string intro, string link, string buttonText, string footer)
        {
            var enc = HtmlEncoder.Default;
            return $"""
                <div style="font-family:Arial,sans-serif;max-width:520px;margin:auto;color:#1f2937;">
                  <h2 style="color:#3b5bdb;">AmarTools Voting</h2>
                  <p>{enc.Encode(greeting)}</p>
                  <p>{enc.Encode(intro)}</p>
                  <p><a href="{enc.Encode(link)}" style="display:inline-block;padding:10px 18px;background:#3b5bdb;color:#fff;border-radius:6px;text-decoration:none;">{enc.Encode(buttonText)}</a></p>
                  <p style="font-size:13px;color:#6b7280;">Or paste this link into your browser:<br>{enc.Encode(link)}</p>
                  <p style="font-size:13px;color:#6b7280;">{enc.Encode(footer)}</p>
                </div>
                """;
        }
    }
}
