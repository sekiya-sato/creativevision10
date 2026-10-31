using CodeShare;
using CvAsset;
using CvBase;
using Grpc.Core;
using Grpc.Net.Client;
using Newtonsoft.Json;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using System.Net.Http;

namespace CvWpfpos.Services;

/// <summary>
/// POS端末のサーバ通信。照会は Msg101_Op_Query、伝票の登録は Msg201_Op_Execute の汎用追加（InsertParam）を使う。
/// 単価・税・合計・精算の採番はサーバが確定させ、保存結果の伝票を返す（設計判断記録 §2.18）。
/// </summary>
public sealed class PosGrpcClient : IDisposable {
	private readonly GrpcChannel channel;
	private readonly ILoginService loginService;
	private readonly ICoreService coreService;
	private readonly PosSettings settings;
	private readonly Guid clientId = Guid.NewGuid();

	public PosGrpcClient(PosSettings settings) {
		this.settings = settings;
		channel = GrpcChannel.ForAddress(settings.ServerUrl, new GrpcChannelOptions { HttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan } });
		loginService = channel.CreateGrpcService<ILoginService>();
		coreService = channel.CreateGrpcService<ICoreService>();
	}

	/// <summary>バーコード（JAN1〜3）で1SKUを引く。単価は店舗上代。</summary>
	public Task<PosProduct?> LookupProductAsync(string barcode, CancellationToken cancellationToken) =>
		LookupAsync(new PosBarcodeLookupRequest { Barcode = barcode, StoreId = settings.StoreId }, cancellationToken);

	/// <summary>SKUキー（DerivedShohinColSiz.Id）で1SKUを引く。単価は店舗上代。</summary>
	public Task<PosProduct?> LookupSkuAsync(long skuId, CancellationToken cancellationToken) =>
		LookupAsync(new PosBarcodeLookupRequest { SkuId = skuId, StoreId = settings.StoreId }, cancellationToken);

	/// <summary>売上・返品を確定する。端末取引ID（PosClientSaleId）が同じ再送は既存の売上が返る。</summary>
	public Task<Tran01Tenuri> CheckoutAsync(Tran01Tenuri sale, CancellationToken cancellationToken) =>
		InsertAsync(sale, cancellationToken);

	/// <summary>POS売上を取り消す。返品伝票の内容はサーバが元売上から作り、精算は取消したレジ（設定のレジ番号）で集計する。</summary>
	public Task<Tran01Tenuri> CancelSaleAsync(Tran01Tenuri original, long staffId, CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(original.PosClientSaleId)) throw new InvalidOperationException("POS売上以外は取消できません。");
		return InsertAsync(new Tran01Tenuri {
			PosClientSaleId = original.PosClientSaleId + ":C",
			Id_Shain = staffId,
			JposPayment = new PosPaymentDetail { RegisterNo = settings.RegisterNo },
		}, cancellationToken);
	}

	/// <summary>日次精算を確定する。SeisanCnt=0 で送り、連番・現金実額・差額はサーバが確定する。</summary>
	public Task<Tran04PosSeisan> SaveSeisanAsync(Tran04PosSeisan seisan, CancellationToken cancellationToken) {
		seisan.SeisanCnt = 0;
		return InsertAsync(seisan, cancellationToken);
	}

	public Task<LoginReply> LoginAsync(string loginId, string password, CancellationToken cancellationToken) {
		var now = DateTime.Now;
		var ip = Common.GetIPAddress().FirstOrDefault();
		var request = new LoginRequest {
			LoginId = loginId,
			Name = $"CV POS ユーザ {now:yyyy/MM/dd HH:mm:ss}",
			CryptPassword = Common.EncryptLoginRequest(password, now),
			LoginDate = now,
			Info = Common.SerializeObject(new { IpAddress = ip.IPAddress?.ToString() ?? string.Empty, MacAddress = ip.MacAddress ?? string.Empty, Machine = Environment.MachineName, User = Environment.UserName, OsVer = Environment.OSVersion.Version.ToString() }),
		};
		return loginService.LoginAsync(request, CreateCallContext(cancellationToken, false));
	}

	public Task<LoginReply> RefreshLoginAsync(CancellationToken cancellationToken) =>
		loginService.LoginRefreshAsync(new LoginRefresh { Token = settings.AccessToken, Info = "{}" }, CreateCallContext(cancellationToken));

	/// <summary>ICoreService.Msg101_Op_Query を使ってサーバからデータを取得します。</summary>
	public async Task<List<T>> QueryListAsync<T>(string? where, string? order, string[]? parameters, int? maxCount, CancellationToken cancellationToken) {
		var param = new QueryListParam(typeof(T), where, order, parameters, maxCount);
		var msg = new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(param)
		};
		var reply = await coreService.QueryMsgAsync(msg, CreateCallContext(cancellationToken));
		// 0件は負のコード(NotFound)で返るが障害ではない（CvMsgErrorCode.NotFound）
		if (reply.Code == CvMsgErrorCode.NotFound) return [];
		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option);
		var list = JsonConvert.DeserializeObject<List<T>>(reply.DataMsg);
		return list ?? [];
	}

	/// <summary>ICoreService.Msg201_Op_Execute の汎用追加（InsertParam）。サーバが保存した行を返す。</summary>
	public async Task<T> InsertAsync<T>(T item, CancellationToken cancellationToken) where T : class {
		var reply = await coreService.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(InsertParam),
			DataMsg = Common.SerializeObject(new InsertParam(typeof(T), Common.SerializeObject(item))),
		}, CreateCallContext(cancellationToken));

		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option);
		if (reply.DataType != typeof(T)) {
			throw new InvalidOperationException($"登録応答の型が不正です。期待値={typeof(T).Name} 実際={reply.DataType?.Name}");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as T
			?? throw new InvalidOperationException("登録応答の復元に失敗しました。");
	}

	private async Task<PosProduct?> LookupAsync(PosBarcodeLookupRequest request, CancellationToken cancellationToken) {
		var reply = await coreService.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(PosBarcodeLookupRequest),
			DataMsg = Common.SerializeObject(request),
		}, CreateCallContext(cancellationToken));

		if (reply.Code == CvMsgErrorCode.NotFound) return null;
		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option);
		if (reply.DataType != typeof(PosProduct)) {
			throw new InvalidOperationException($"商品検索の応答型が不正です。期待値={nameof(PosProduct)} 実際={reply.DataType?.Name}");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) as PosProduct
			?? throw new InvalidOperationException("商品検索の応答の復元に失敗しました。");
	}

	private CallContext CreateCallContext(CancellationToken cancellationToken, bool includeAccessToken = true) {
		var headers = new Metadata { new("X-ClientId", clientId.ToString()) };
		if (includeAccessToken && !string.IsNullOrWhiteSpace(settings.AccessToken)) headers.Add("Authorization", $"Bearer {settings.AccessToken}");
		return new CallContext(new CallOptions(headers: headers, cancellationToken: cancellationToken));
	}

	public void Dispose() => channel.Dispose();
}
