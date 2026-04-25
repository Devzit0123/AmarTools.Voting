namespace AmarTools.Voting.Models
{
    /// <summary>
    /// View model used by the error page (Views/Shared/Error.cshtml)
    /// to display error information and optionally the request ID for debugging.
    /// </summary>
    public class ErrorViewModel
    {
        /// <summary>
        /// Unique identifier of the current HTTP request.
        /// Useful for correlating logs when reporting bugs.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Determines whether the RequestId should be displayed.
        /// Only shown in development environment or when explicitly enabled.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        // ────────────────────────────────────────────────
        // Optional but useful extensions (you can delete if not needed)
        // ────────────────────────────────────────────────

        /// <summary>
        /// Short user-friendly message (set in controller)
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// HTTP status code (e.g. 404, 500)
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Exception message (only shown in development!)
        /// </summary>
        public string? ExceptionMessage { get; set; }

        public bool ShowException => !string.IsNullOrEmpty(ExceptionMessage);
    }
}