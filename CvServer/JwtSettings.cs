using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CvServer;

/// <summary>
/// JWT の検証と発行で共通利用する設定。
/// </summary>
public sealed class JwtSettings {
	private const string DefaultSecretKey = "veryveryhardsecurity-keys.needtoolong";

	public string? Issuer { get; }
	public string? Audience { get; }
	public string SecretKey { get; }
	public TimeSpan Lifetime { get; }
	public TimeSpan RefreshLifetime { get; }

	public JwtSettings(IConfiguration configuration) {
		var section = configuration.GetSection("WebAuthJwt");
		Issuer = section["Issuer"];
		Audience = section["Audience"];
		SecretKey = section["SecretKey"] ?? DefaultSecretKey;
		Lifetime = ReadLifetime(section, "Lifetime");
		RefreshLifetime = ReadLifetime(section, "Refreshtime");
	}

	public TokenValidationParameters CreateTokenValidationParameters() => new() {
		ValidateIssuer = true,
		ValidIssuer = Issuer,
		ValidateAudience = false,
		ValidAudience = Audience,
		ValidateLifetime = true,
		IssuerSigningKey = CreateSigningKey(),
		ValidateIssuerSigningKey = true,
		ClockSkew = TimeSpan.Zero,
	};

	public SigningCredentials CreateSigningCredentials() =>
		new(CreateSigningKey(), SecurityAlgorithms.HmacSha256);

	public string ResolveIssuer(string? issuer = null) => issuer ?? Issuer ?? "issuer";

	private SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SecretKey));

	private static TimeSpan ReadLifetime(IConfigurationSection section, string key) =>
		int.TryParse(section[key], out var minutes) ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(1);
}
