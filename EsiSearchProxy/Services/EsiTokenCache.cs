using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Caching.Memory;

namespace EsiSearchProxy.Services
{
    public class EsiTokenCache(IMemoryCache memoryCache)
    {
        public string GetToken()
        {
            return memoryCache.Get<string>("Token") ?? string.Empty;
        }

        public void StoreToken(string token)
        {
            memoryCache.Set("Token", token);
        }

        public (string token, uint character) GetCachedTokenWithCharacter()
        {
            var token = GetToken();

            var jwtHandler = new JwtSecurityTokenHandler();
            if (!jwtHandler.CanReadToken(token))
                return (token, 0);

            var jwt = jwtHandler.ReadJwtToken(token);
            var characterIdString = jwt.Payload.Sub.Replace("CHARACTER:EVE:", "");

            var characterId = uint.Parse(characterIdString);
            return (token, characterId);
        }
    }
}