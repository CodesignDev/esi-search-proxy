using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Web;

namespace EsiSearchProxy.Services
{
    public class EsiAuthService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EsiConfiguration _configuration;

        public EsiAuthService(
            IHttpClientFactory httpClientFactory,
            IOptions<EsiConfiguration> configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration.Value;
        }

        public async Task<string> GetAccessToken()
        {
            static string Encode(string val) => HttpUtility.UrlEncode(val);

            var refreshToken = _configuration.CharacterRefreshToken;

            var grantType = "refresh_token";

            var esiLoginUrl = _configuration.SSOUrl;

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(esiLoginUrl);

            var clientId = _configuration.ClientId;
            var clientSecret = _configuration.ClientSecret;

            var body = new Dictionary<string, string>
            {
                { "grant_type", grantType },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "refresh_token", refreshToken },
            };

            var requestBody = new StringContent(
                string.Join('&', body.Select(x => $"{Encode(x.Key)}={Encode(x.Value)}")),
                null,
                "application/x-www-form-urlencoded");

            using var response = await httpClient.PostAsync("/oauth/token", requestBody); // Deliberately not using the V2 auth
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<EsiAccessToken>();

            if (token is null) throw new Exception();

            // Make a request to the verify endpoint to check that the token is valid
            var verifyRequest = new HttpRequestMessage(HttpMethod.Get, "/oauth/verify");
            verifyRequest.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);

            using var verifyResponse = await httpClient.SendAsync(verifyRequest);
            verifyResponse.EnsureSuccessStatusCode();

            return token.AccessToken;
        }

        public class EsiAccessToken
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;
            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public uint ExpiresIn { get; set; } = uint.MinValue;

            [JsonIgnore]
            public DateTime ExpiresAt => DateTime.UtcNow.AddSeconds(ExpiresIn);
        }
    }
}
