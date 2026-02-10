using System;

namespace Cvnet10Server;

public static class BuildMetadata {
    public static readonly DateTime BuildDate = new DateTime(2026, 2, 10, 08,51,28);
    // ビルド環境情報
    public const string MachineName = "DESKTOP-LV37IKB";
    public const string UserName = "user2010";
    public const string OSVersion = @"Microsoft Windows 10.0.26100 ";
    public const string DotNetVersion = ".NET Framework 4.8.9310.0";

    // 重要な環境変数のキャプチャ
    // CI環境（GitHub Actions等）かどうかの判定に便利
    public const string BuildConfiguration = "Debug";
    public const string CI = "false";

    /// <summary>
    /// ビルド情報を文字列として取得（ログ出力用）
    /// </summary>
    public static string GetSummary() {
        return $".net: {DotNetVersion} | Built: {BuildDate:yyyy-MM-dd HH:mm:ss} | Env: {BuildConfiguration} | Machine: {MachineName} ({UserName})";
    }
}