namespace AmarTools.Voting.Services
{
    /// <summary>
    /// Adds the standard browser security headers to every response.
    /// The Content-Security-Policy lists the CDNs the layouts load from. It still allows
    /// 'unsafe-inline' scripts and styles because the views use inline &lt;script&gt; /
    /// &lt;style&gt; blocks and onclick handlers; moving those into files would allow
    /// dropping it. frame-ancestors 'none' blocks click-jacking.
    /// </summary>
    public static class SecurityHeaders
    {
        public const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://code.jquery.com; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
            "font-src 'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        public static void Apply(IHeaderDictionary headers)
        {
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        }

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    Apply(context.Response.Headers);
                    return Task.CompletedTask;
                });
                await next();
            });
    }
}
