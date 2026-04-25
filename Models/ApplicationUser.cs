using Microsoft.AspNetCore.Identity;

namespace AmarTools.Voting.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Add this if you haven't already!
        public string FullName { get; set; } = string.Empty;
    }
}