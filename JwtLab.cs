using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IswStore;

// SOLO LABORATORIO: implementación deliberadamente insegura de JWT para la PoC de
// "JWT None Algorithm / secreto débil" exigida en la Fase 1. NO sirve como referencia de
// implementación segura. La versión de Fase 2 debe usar RS256, rotación de claves y una
// librería validada (System.IdentityModel.Tokens.Jwt) con un allow-list estricto de algoritmos.
public static class JwtLab
{
    // Vulnerabilidad intencional #1: secreto simétrico débil y hardcodeado en el código fuente.
    private const string WeakSecret = "isw2026";

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }

    public static string Create(int userId, string role, string name)
    {
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var payload = B64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = userId,
            role,
            name,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        })));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WeakSecret));
        var signature = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    // Vulnerabilidad intencional #2: la validación confía en el campo "alg" que declara el propio
    // token en vez de forzar un algoritmo esperado por el servidor. Un token con alg="none" se
    // acepta sin verificar ninguna firma (clásico bypass "JWT alg:none").
    public static bool TryParseInsecure(string? token, out int userId, out string role, out string name)
    {
        userId = 0; role = ""; name = "";
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length < 2) return false;

        JsonElement header, payload;
        try
        {
            header = JsonSerializer.Deserialize<JsonElement>(B64UrlDecode(parts[0]));
            payload = JsonSerializer.Deserialize<JsonElement>(B64UrlDecode(parts[1]));
        }
        catch { return false; }

        var alg = header.TryGetProperty("alg", out var a) ? a.GetString() ?? "" : "";
        if (!alg.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // Solo valida firma cuando el token declara HS256; cualquier otro valor (incluido "none") la omite.
            if (!alg.Equals("HS256", StringComparison.OrdinalIgnoreCase)) return false;
            if (parts.Length != 3) return false;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WeakSecret));
            var expected = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}")));
            if (expected != parts[2]) return false;
        }

        if (payload.TryGetProperty("exp", out var expClaim) &&
            DateTimeOffset.FromUnixTimeSeconds(expClaim.GetInt64()) < DateTimeOffset.UtcNow) return false;

        userId = payload.TryGetProperty("sub", out var s) ? s.GetInt32() : 0;
        role = payload.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
        name = payload.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        return true;
    }
}
