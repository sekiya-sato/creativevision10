using System.Text.Json;
using System.IO;

namespace CvWpfpos.Services;

public sealed class PosTokenStore
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CVPOS", "clientsettings.json");

    public PosTokenSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<PosTokenSettings>(File.ReadAllText(path)) ?? new PosTokenSettings()
                : new PosTokenSettings();
        }
        catch (JsonException)
        {
            return new PosTokenSettings();
        }
    }

    public void Save(string loginId, string token, DateTime expire)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PosTokenSettings { LoginId = loginId, Token = token, Expire = expire }));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class PosTokenSettings
{
    public string LoginId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public DateTime Expire { get; init; }
}
