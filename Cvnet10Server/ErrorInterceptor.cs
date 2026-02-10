using Grpc.Core;
using Grpc.Core.Interceptors;
using NLog;

namespace Cvnet10Server;

public class ErrorInterceptor : Interceptor {
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
		TRequest request,
		ServerCallContext context,
		UnaryServerMethod<TRequest, TResponse> continuation) {
		try {
			return await continuation(request, context);
		}
		catch (RpcException) {
			// 既に RpcException になっている場合はそのままスロー
			throw;
		}
		catch (Exception ex) {
			// NLog で詳細な例外を記録 (appsettings.json の設定に従う)
			Logger.Error(ex, "gRPCサービス実行中に未ハンドルの例外が発生しました。 Method: {Method}", context.Method);

			// クライアントには詳細なスタックトレースを隠し、適切なステータスコードを返す
			throw new RpcException(new Status(StatusCode.Internal, "サーバー側でエラーが発生しました。詳細はログを確認してください。"));
		}
	}
}