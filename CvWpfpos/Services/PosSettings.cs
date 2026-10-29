using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CvWpfpos.Services;

public sealed class PosSettings
{
    public string ServerUrl { get; init; } = "https://localhost:5012";
    public string StoreName { get; init; } = "店舗名未設定";

    /// <summary>レシート／領収書に印字する店舗住所。用紙幅に合わせて自動で折り返す。</summary>
    public string StoreAddress { get; init; } = string.Empty;

    /// <summary>レシート／領収書に印字する店舗電話番号。</summary>
    public string StorePhone { get; init; } = string.Empty;

    public long StoreId { get; init; }
    public long WarehouseId { get; init; }
    public long StaffId { get; init; }

    /// <summary>レシートに印字する販売員コード。未設定なら StaffId を 6 桁ゼロ埋めして使う。</summary>
    public string StaffCode { get; init; } = string.Empty;

    /// <summary>消費税率(%)。上代を税抜として扱う外税方式で計算する。</summary>
    public int TaxRatePercent { get; init; } = 10;

    public string DisplayPortName { get; init; } = "COM1";
    public string PrinterPortName { get; init; } = "COM6";
    public string PosDisplayName { get; init; } = "DM-D30";
    public string PosPrinterName { get; init; } = "TM-m30II";

    /// <summary>
    /// レシート用紙幅(mm)。58 または 80。接続時に TM-m30II の設定もこの値へ切り替える。
    /// 用紙幅の問い合わせに応答しない場合は、レイアウト上の既定値としてのみ使う。
    /// </summary>
    public int PaperWidthMm { get; init; } = 80;

    public string AccessToken { get; set; } = string.Empty;

    /// <summary>販売員コード。未設定時は StaffId から生成する。</summary>
    public string ResolvedStaffCode => string.IsNullOrWhiteSpace(StaffCode) ? StaffId.ToString("D6") : StaffCode;

    public static PosSettings Load()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var settingsJson = LoadJsonFile("appsettings.json") ?? new JsonObject();
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            var environmentSettingsJson = LoadJsonFile($"appsettings.{environmentName}.json");
            if (environmentSettingsJson is not null) Merge(settingsJson, environmentSettingsJson);
        }

        return settingsJson.Deserialize<PosSettings>(options) ?? new PosSettings();
    }

    private static JsonObject? LoadJsonFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
    }

    private static void Merge(JsonObject destination, JsonObject source)
    {
        foreach (var (propertyName, sourceValue) in source)
        {
            if (sourceValue is JsonObject sourceObject && destination[propertyName] is JsonObject destinationObject)
            {
                Merge(destinationObject, sourceObject);
            }
            else
            {
                destination[propertyName] = sourceValue?.DeepClone();
            }
        }
    }
}
