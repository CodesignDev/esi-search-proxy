namespace EsiSearchProxy
{
    public class EsiConfiguration
    {
        public string SSOUrl { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        public int CharacterId { get; set; }
        public string CharacterRefreshToken { get; set; } = string.Empty;
    }
}