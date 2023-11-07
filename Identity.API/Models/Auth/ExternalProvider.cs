namespace Identity.API.Models.Auth
{
    public class ExternalProvider
    {
        public string DisplayName { get; set; } = string.Empty;
        public string AuthenticationScheme { get; set; } = string.Empty;
    }
}