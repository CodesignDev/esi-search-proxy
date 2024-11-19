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

                using var esiRequest = CreateEsiRequest(requestMethod, esiRoute);

                // Check to see if this a search endpoint request
                if (SearchEndpointRegex.IsMatch(esiRoute))
                {
                    var characterSearchUrl = GetEsiRequestUrl(
                        $"/v3/characters/{_esiConfiguration.CharacterId}/search/"
                    );
                    esiRequest.RequestUri = new Uri(characterSearchUrl, UriKind.RelativeOrAbsolute);

                    // Attach an auth token to this request
                    var authorizationToken = await _esiAuthService.GetAccessToken();
                    esiRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
                }

                using var esiResponse = await httpClient.SendAsync(esiRequest);

                Response.StatusCode = (int)esiResponse.StatusCode;

                var responseHeaders = esiResponse.Headers.Concat(esiResponse.Content.Headers);
                foreach (var header in responseHeaders)
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }

                // Remove headers that could potentially cause issues
                foreach (var header in StrippedEsiResponseHeaders)
                {
                    Response.Headers.Remove(header);
                }

                await esiResponse.Content.CopyToAsync(Response.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occured while proxying ESI request {RequestMethod} {EsiRoute}", requestMethod, esiRoute);
                Response.StatusCode = 503;
            }
        }

        private HttpRequestMessage CreateEsiRequest(string method, string esiRoute)
        {
            var requestUrl = GetEsiRequestUrl(esiRoute);
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

        private string GetEsiRequestUrl(string esiRoute)
        {
            var esiUrl = esiRoute.StartsWith('/') ? esiRoute : $"/{esiRoute}";
            var queryString = Request.QueryString.ToString();

            return $"{esiUrl}{queryString}";
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
