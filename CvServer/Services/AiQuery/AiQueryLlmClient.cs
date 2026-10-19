using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace CvServer.Services.AiQuery;

/// <summary>
/// AI照会の設定（appsettings の "AiQuery" セクション）。
/// Doc/spec/2026-10-19_AI-query_最小変更設計案.md §6.6
/// </summary>
public sealed class AiQueryOptions {
	public bool Enabled { get; set; }
	/// <summary>OpenAI互換APIのベースURL（例 http://127.0.0.1:1234/v1）</summary>
	public string Endpoint { get; set; } = string.Empty;
	public string Model { get; set; } = string.Empty;
	/// <summary>空なら Authorization ヘッダを送らない（社内LLMはキーなし）</summary>
	public string ApiKey { get; set; } = string.Empty;
	public int TimeoutSeconds { get; set; } = 180;
	/// <summary>1往復あたりのLLM呼出し上限（情報取得ツールのやり取りを含む）</summary>
	public int MaxToolRounds { get; set; } = 8;
	/// <summary>SQL検査エラー時の修正依頼回数の上限</summary>
	public int MaxRepair { get; set; } = 2;
	/// <summary>LLMへ送る会話履歴の件数上限（直近から）</summary>
	public int MaxMessages { get; set; } = 20;
	/// <summary>業務辞書。相対パスはサーバ実行フォルダ基準（ビルドで CvServer/AiQueryDictionary.md を実行フォルダへコピー）</summary>
	public string DictionaryPath { get; set; } = "AiQueryDictionary.md";

	public static AiQueryOptions From(IConfiguration configuration) =>
		configuration.GetSection("AiQuery").Get<AiQueryOptions>() ?? new AiQueryOptions();
}

/// <summary>
/// AI照会のLLM接続境界。テストでは固定応答に差し替える。
/// クラウド接続は別実装として追加する（自動フォールバックはしない）。
/// </summary>
public interface IAiQueryLlm {
	/// <summary>
	/// ツール付きでチャット補完を1回呼び、assistant メッセージ（role / content / tool_calls）を返す。
	/// </summary>
	Task<JObject> ChatAsync(JArray messages, JArray tools, CancellationToken ct);
}

/// <summary>
/// OpenAI互換 Chat Completions（LM Studio・Ollama・llama.cpp server・vLLM 等）への接続。
/// 構造化応答は tools + tool_choice=required のツール引数で受け取る
/// （LM Studio は tools と response_format の同時指定を受け付けないため）。
/// </summary>
public sealed class OpenAiCompatibleLlm(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiCompatibleLlm> logger) : IAiQueryLlm {

	public async Task<JObject> ChatAsync(JArray messages, JArray tools, CancellationToken ct) {
		var options = AiQueryOptions.From(configuration);
		var body = new JObject {
			["model"] = options.Model,
			["messages"] = messages,
			["tools"] = tools,
			["tool_choice"] = "required",
			["temperature"] = 0.1,
			["stream"] = false,
		};
		using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint.TrimEnd('/') + "/chat/completions") {
			Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
		};
		if (!string.IsNullOrEmpty(options.ApiKey)) {
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
		}
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

		using var response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
		var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) {
			logger.LogWarning("AI照会 LLM応答エラー Status={Status} Body={Body}", (int)response.StatusCode, Truncate(text));
			throw new InvalidOperationException($"AIの応答がエラーでした（HTTP {(int)response.StatusCode}）: {Truncate(text)}");
		}
		var json = JObject.Parse(text);
		if (json.SelectToken("choices[0].message") is not JObject message) {
			throw new InvalidOperationException($"AIの応答形式が不正です: {Truncate(text)}");
		}
		// 推論過程は使わず、会話へも戻さない
		message.Remove("reasoning_content");
		return message;
	}

	static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "…";
}
