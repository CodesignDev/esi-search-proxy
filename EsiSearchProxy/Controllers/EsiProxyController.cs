using EsiSearchProxy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace EsiSearchProxy.Controllers
{
    [ApiController]
    public class EsiSearchProxyController : Controller
    {
        private static readonly Regex SearchEndpointRegex = new(@"^/?(?:v[1-9]|latest|dev|legacy)/search/?.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CharacterOnlineEndpointRegex = new(@"^/?(v[1-9]|latest|dev|legacy)/characters/([0-9]+)/online/?.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly IEnumerable<string> EsiSearchProxyHeaders = new[] { "Host", "X-Proxy-Auth", "X-Entity-ID", "X-Token-Type" };
        private static readonly IEnumerable<string> StrippedEsiResponseHeaders = new[] { "strict-transport-security", "transfer-encoding" };

        private readonly ILogger<EsiSearchProxyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EsiConfiguration _esiConfiguration;
        private readonly EsiAuthService _esiAuthService;

        public EsiSearchProxyController(
            ILogger<EsiSearchProxyController> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<EsiConfiguration> esiConfiguration,
            EsiAuthService esiAuthService)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _esiConfiguration = esiConfiguration.Value;
            _esiAuthService = esiAuthService;
        }

        [Route("{**esiRoute}")]
        [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
        public async Task Proxy(string esiRoute)
        {
            var requestMethod = Request.Method;

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.BaseAddress = new Uri(_esiConfiguration.BaseUrl);

                using var esiRequest = true switch
                {
                    true when SearchEndpointRegex.IsMatch(esiRoute) => await CreateProxiedSearchRequest(),
                    true when CharacterOnlineEndpointRegex.IsMatch(esiRoute) => CreateProxiedOnlineRequest(esiRoute),
                    _ => CreateEsiRequest(requestMethod, esiRoute)
                };

                using var esiResponse = await httpClient.SendAsync(esiRequest);

                // Process the esi response
                switch (true)
                {
                    case true when CharacterOnlineEndpointRegex.IsMatch(esiRoute):
                        await ProcessCharacterOnlineResponse(esiRoute, esiResponse);
                        break;

                    default:
                        await ProcessEsiResponse(esiResponse);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occured while proxying ESI request {RequestMethod} {EsiRoute}", requestMethod, esiRoute);
                Response.StatusCode = 503;
            }
        }

        private HttpRequestMessage CreateEsiRequest(string method, string url)
        {
            var requestUrl = GetEsiRequestUrl(url);
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(requestUrl, UriKind.RelativeOrAbsolute),
                Method = GetHttpMethod(method),
            };

            var headers = Request.Headers.Where(x => !EsiSearchProxyHeaders.Contains(x.Key, StringComparer.OrdinalIgnoreCase));
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            if (!HasRequestBody(method))
                return request;

            var streamContent = new StreamContent(Request.Body);
            request.Content = streamContent;

            return request;
        }

        private async Task<HttpRequestMessage> CreateProxiedSearchRequest()
        {
            var characterSearchUrl = $"/v3/characters/{_esiConfiguration.CharacterId}/search/";
            var request = CreateEsiRequest("GET", characterSearchUrl);

            // Attach an auth token to this request
            var authorizationToken = await _esiAuthService.GetAccessToken();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);

            return request;
        }

        private HttpRequestMessage CreateProxiedOnlineRequest(string url)
        {
            var characterOnlineRouteMatch = CharacterOnlineEndpointRegex.Match(url);
            if (!characterOnlineRouteMatch.Success || characterOnlineRouteMatch.Groups[1].Value is not ("v1" or "legacy"))
                return CreateEsiRequest(Request.Method, url);

            var characterId = characterOnlineRouteMatch.Groups[2].Value;

            var characterOnlineUrl = $"/v3/characters/{characterId}/online/";
            var request = CreateEsiRequest("GET", characterOnlineUrl);

            return request;
        }

        private string GetEsiRequestUrl(string url)
        {
            var esiUrl = url.StartsWith('/') ? url : $"/{url}";
            var queryString = Request.QueryString.ToString();

            return $"{esiUrl}{queryString}";
        }

        private async Task ProcessEsiResponse(HttpResponseMessage response)
        {
            ProcessResponseHeaders(response);
            await response.Content.CopyToAsync(Response.Body);
        }

        private async Task ProcessCharacterOnlineResponse(string requestUrl, HttpResponseMessage response)
        {
            // If the response doesn't need to be rewritten then exit
            var characterOnlineRegexMatches = CharacterOnlineEndpointRegex.Match(requestUrl);
            if (!characterOnlineRegexMatches.Success || characterOnlineRegexMatches.Groups[1].Value is not ("v1" or "legacy"))
            {
                await ProcessEsiResponse(response);
                return;
            }

            // Decode the response from esi into a dynamic object
            var characterOnlineResponse = await response.Content.ReadFromJsonAsync<IDictionary<string, object>>();

            // Pull the online flag from the response
            var isCharacterOnline = characterOnlineResponse?["online"] ?? false;

            // Process the response headers (except the content length
            ProcessResponseHeaders(response);
            Response.Headers.Remove("Content-Length");

            // Rewrite the content on the response
            await Response.WriteAsJsonAsync(isCharacterOnline);
        }

        private void ProcessResponseHeaders(HttpResponseMessage response)
        {
            Response.StatusCode = (int)response.StatusCode;

            var responseHeaders = response.Headers.Concat(response.Content.Headers);
            foreach (var header in responseHeaders)
            {
                Response.Headers[header.Key] = header.Value.ToArray();
            }

            // Remove headers that could potentially cause issues
            foreach (var header in StrippedEsiResponseHeaders)
            {
                Response.Headers.Remove(header);
            }
        }

        private static HttpMethod GetHttpMethod(string method)
        {
            return method.ToUpper() switch
            {
                "GET" => HttpMethod.Get,
                "HEAD" => HttpMethod.Head,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "PATCH" => HttpMethod.Patch,
                "DELETE" => HttpMethod.Delete,
                "OPTIONS" => HttpMethod.Options,
                _ => new HttpMethod(method),
            };
        }

        private static bool HasRequestBody(string method)
        {
            var methods = new[] { "POST", "PUT", "PATCH" };
            return methods.Contains(method.ToUpper());
        }
    }
}
