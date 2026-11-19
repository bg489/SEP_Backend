using System.Text;
using AutoScenarioX.Api.Common.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AutoScenarioX.Api.Common.Auth;

/// <summary>
/// Lấy và nhớ khoá công khai của Supabase để kiểm tra chữ ký JWT.
/// Tải từ JWKS (https://&lt;ref&gt;.supabase.co/auth/v1/.well-known/jwks.json), nhớ trong 10 phút,
/// và tải lại ngay khi gặp "kid" lạ (Supabase xoay khoá).
/// Dự án chưa bật khoá bất đối xứng thì JWKS rỗng - khi đó dùng Auth:LegacyJwtSecret (HS256).
/// </summary>
public sealed class SupabaseSigningKeys(
    IHttpClientFactory httpClientFactory,
    IOptions<AuthOptions> options,
    ILogger<SupabaseSigningKeys> logger)
{
    public const string HttpClientName = "supabase-jwks";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinReloadInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AuthOptions _options = options.Value;

    private IReadOnlyList<SecurityKey> _keys = [];
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>Hàm truyền cho TokenValidationParameters.IssuerSigningKeyResolver.</summary>
    public IEnumerable<SecurityKey> Resolve(string token, SecurityToken securityToken, string? kid, TokenValidationParameters parameters)
    {
        var keys = GetKeys(forceReload: false);

        // "kid" chưa biết -> khoá vừa được xoay, tải lại một lần
        if (kid is not null && keys.All(k => k.KeyId != kid))
            keys = GetKeys(forceReload: true);

        return string.IsNullOrEmpty(kid) ? keys : keys.Where(k => k.KeyId == kid || k.KeyId is null);
    }

    private IReadOnlyList<SecurityKey> GetKeys(bool forceReload)
    {
        // "kid" lạ chỉ được tải lại tối đa mỗi MinReloadInterval: chạy trước khi kiểm chữ ký nên token giả
        // với kid ngẫu nhiên sẽ gọi JWKS mỗi request và chặn luồng xử lý. Khoá vừa xoay chờ tối đa 30 s.
        var maxAge = forceReload ? MinReloadInterval : CacheDuration;
        if (DateTimeOffset.UtcNow - _loadedAt < maxAge) return _keys;

        _gate.Wait();
        try
        {
            if (DateTimeOffset.UtcNow - _loadedAt < maxAge) return _keys;

            _keys = Load();
            _loadedAt = DateTimeOffset.UtcNow;
            return _keys;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<SecurityKey> Load()
    {
        var keys = new List<SecurityKey>();

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var json = client.GetStringAsync(_options.JwksUri).GetAwaiter().GetResult();
            keys.AddRange(JsonWebKeySet.Create(json).GetSigningKeys());
        }
        catch (Exception ex)
        {
            // Không ném: còn khoá HS256 cũ để thử, và lỗi mạng không nên thành 500
            logger.LogWarning(ex, "Không tải được JWKS từ {Uri}", _options.JwksUri);
        }

        if (!string.IsNullOrWhiteSpace(_options.LegacyJwtSecret))
            keys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.LegacyJwtSecret)));

        if (keys.Count == 0)
            logger.LogError("Không có khoá nào để kiểm tra JWT. Kiểm tra Auth:ProjectUrl hoặc đặt Auth:LegacyJwtSecret.");

        return keys;
    }
}
