using ModelContextProtocol.Server;

// 1. MCP サーバーのオプション構成とツールの登録
var options = new McpServerOptions {
	Capabilities = new() {
		Tools = new() {
			ToolCollection =
			[
				McpServerTool.Create(
					(string name) => $"Hello, {name}!",
					new()
					{
						Name = "greet",
						Description = "指定された名前に対して挨拶メッセージを返します。"
					})
			]
		}
	}
};

// 2. サーバーインスタンス化と Stdio トランスポートでの実行
var server = new McpServer(options);
using var transport = new StdioServerTransport();

await server.RunAsync(transport);
