using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBase.Sql;
using CvBaseOracle;
using CvDomainLogic;
using ProtoBuf.Grpc;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace CvServer.Services;

public partial class CoreService {
	private const int NotFoundCode = CvMsgErrorCode.NotFound;
	private const string ConcurrentUpdateMessage = "他で更新されています";

	private WriteEffectRunner? _effects;

	/// <summary>
	/// 障害調査時だけ、従来どおり要求パラメータ全文とSQLを記録する。
	/// </summary>
	private void LogDetailedRequest(string message, Func<object?[]> argsFactory) {
		if (_configuration.GetValue<bool>("Diagnostics:EnableDetailedRequestLogging")) {
			_logger.LogInformation(message, argsFactory());
		}
	}
	/// <summary>
	/// テーブル更新に伴う副作用(在庫・引当・派生・V*列伝播)の起動役。
	/// トランザクションと楽観排他はこのクラスが持ち、副作用の順序は <see cref="WriteEffectRunner"/> が持つ。
	/// </summary>
	private WriteEffectRunner Effects => _effects ??= new WriteEffectRunner(_db);

	/// <summary>
	/// クライアントが組み立てたSQLを接続先DBの方言へ変換する。
	/// <para>
	/// CV10 は SQL の組み立てを CvWpfclient 側でも行うため、送られてくるSQLは SQLite 方言が正典である。
	/// SQLite 接続では <c>PassThroughSqlDialect</c> が引数の参照をそのまま返すので変換処理は走らない。
	/// 変換を差すのはこの1点だけで、CvBase / CvDomainLogic 内部のSQLは通さない。
	/// 設計は `.omo/2026-08-25_sql_dialect_translator_detail_design.md` を参照する。
	/// </para>
	/// </summary>
	private string TranslateClientSql(string sql) => TranslateClientSql(sql, null);

	/// <summary>
	/// <paramref name="queryKey"/> に方言別の手書きSQLが登録されていればそれを使い、
	/// 無ければ通常の方言変換を行う。
	/// </summary>
	private string TranslateClientSql(string sql, string? queryKey) {
		// SQLite（恒等変換）では即座に引数を返す。差し替え表の参照も変換も行わない。
		// 現に動いているSQLiteの実行経路へ、方言変換のコードを一切通さないための分岐である。
		if (!_db.Dialect.TranslatesSql) {
			return sql;
		}
		if (SqlOverrideCatalog.TryGet(queryKey, _db.Dialect.Name, out var overrideSql)) {
			_logger.LogInformation("SQL方言 手書きSQLへ差し替え 方言={Dialect} QueryKey={QueryKey}",
				_db.Dialect.Name, queryKey);
			return overrideSql;
		}
		var translated = _db.Dialect.Translate(sql);
		// 変換が起きたときだけログへ出す。SQLiteでログが増えないようにする
		if (!ReferenceEquals(translated, sql) && !string.Equals(translated, sql, StringComparison.Ordinal)) {
			_logger.LogDebug("SQL方言変換 方言={Dialect} 変換前={Before} 変換後={After}",
				_db.Dialect.Name, sql, translated);
		}
		return translated;
	}

	private CvMsg HandleCopyReply(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleCopyReply invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, request.DataType, request.DataMsg);
	}

	private CvMsg HandleGetVersion(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetVersion invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(InfoServer), Common.SerializeObject(_appGlobal.VerInfo));
	}

	private CvMsg HandleGetEnv(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetEnv invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(Dictionary<string, string>), Common.SerializeObject(GetEnvironmentVariables()));
	}

	private CvMsg HandleGetConnectionStatus(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetConnectionStatus invoked Flag:{Flag}", request.Flag);

		var resultData = _configuration.GetSection("ConnectionStrings").GetChildren()
			.Select(c => c.Key)
			.ToList();
		return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(resultData));
	}

	private CvMsg HandlerGetTableList(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleGetTableList invoked Flag:{Flag}", request.Flag);
		var resultData = new List<Tuple<string, string, long>>();
		try {
			resultData = _db.GetTableCounts();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleGetTableList error");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
		return CreateSuccessResponse(request.Flag, typeof(List<Tuple<string, string, long>>), Common.SerializeObject(resultData));
	}
	private CvMsg HandlerGetConvertTaskList(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleGetConvertTaskList invoked Flag:{Flag}", request.Flag);
		var resultData = new List<string>();
		try {
			resultData = CreateConvertDb().GetAllTaskNames();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleGetConvertTaskList error");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
		return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(resultData));
	}
	private CvMsg HandleConvertMasterShohin(CvMsg request, CallContext context) {
		var rebuild = new RebuildDb(_db);
		var ret = rebuild.RebuildMasterShohin2Meisho();
		return CreateSuccessResponse(request.Flag, typeof(InfoServer), Common.SerializeObject(_appGlobal.VerInfo));
	}

	private async Task<CvMsg> HandlePosLookupProductAsync(CvMsg request, CallContext context) {
		if (!TryDeserializePosRequest<PosBarcodeLookupRequest>(request, out var payload, out var error)) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, error, typeof(string), string.Empty);
		}
		var product = await _pointOfSaleService.LookupProductAsync(payload, context);
		return product == null
			? CreateNotFoundResponse(request.Flag, typeof(PosProduct), "null")
			: CreateSuccessResponse(request.Flag, typeof(PosProduct), Common.SerializeObject(product));
	}

	private async Task<CvMsg> HandlePosCheckoutAsync(CvMsg request, CallContext context) {
		if (!TryDeserializePosRequest<PosCheckoutRequest>(request, out var payload, out var error)) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, error, typeof(string), string.Empty);
		}
		var response = await _pointOfSaleService.CheckoutAsync(payload, context);
		return CreateSuccessResponse(request.Flag, typeof(PosCheckoutResponse), Common.SerializeObject(response));
	}

	private async Task<CvMsg> HandlePosCancelSaleAsync(CvMsg request, CallContext context) {
		if (!TryDeserializePosRequest<PosCancelSaleRequest>(request, out var payload, out var error)) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, error, typeof(string), string.Empty);
		}
		var response = await _pointOfSaleService.CancelSaleAsync(payload, context);
		return CreateSuccessResponse(request.Flag, typeof(PosCancelSaleResponse), Common.SerializeObject(response));
	}

	private async Task<CvMsg> HandlePosSaveSeisanAsync(CvMsg request, CallContext context) {
		if (!TryDeserializePosRequest<PosSaveSeisanRequest>(request, out var payload, out var error)) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, error, typeof(string), string.Empty);
		}
		var response = await _pointOfSaleService.SaveSeisanAsync(payload, context);
		return CreateSuccessResponse(request.Flag, typeof(PosSaveSeisanResponse), Common.SerializeObject(response));
	}

	private static bool TryDeserializePosRequest<T>(CvMsg request, out T payload, out string error) where T : class {
		payload = default!;
		error = string.Empty;
		if (request.DataType != typeof(T)) {
			error = $"POS要求型が不正です。期待値={typeof(T).Name} 実際={request.DataType?.Name}";
			return false;
		}
		try {
			payload = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType) as T ?? throw new InvalidOperationException("POS要求を復元できません。");
			return true;
		}
		catch (Exception ex) {
			error = $"POS要求の復元に失敗しました: {ex.Message}";
			return false;
		}
	}
	/// <summary>
	/// Master系のV*列とJSON内の名称スナップショットを参照先マスタの現在値で再同期する
	/// (マスタ改名時の伝播はHandleUpdateで自動実行されるため、これはDB変換後や取りこぼしの修復用)
	/// </summary>
	private CvMsg HandleMasterVColumnResync(CvMsg request, CallContext context) {
		var errors = new List<string>();
		var startTime = DateTime.Now;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var updated = new MasterCascadeDb(_db).ResyncAll(errors);
			_db.CompleteTransaction();
			var summary = BuildResyncSummary(startTime, updated, errors.Count);
			_logger.LogInformation("V*列再同期 {Summary}", summary.Replace(Environment.NewLine, " "));
			// 一部ルールが失敗した場合は成功扱いにしない(利用者へ提示して再実行を促す)
			if (errors.Count > 0) {
				return CreateErrorResponse(request.Flag, CvMsgErrorCode.Unexpected, string.Join(Environment.NewLine, errors), typeof(string), summary);
			}
			return CreateSuccessResponse(request.Flag, typeof(string), summary);
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			_logger.LogError(ex, "V*列再同期に失敗");
			return CreateExceptionResponse(request.Flag, ex, typeof(string),
				BuildResyncSummary(startTime, 0, errors.Count) + Environment.NewLine + string.Join(Environment.NewLine, errors));
		}
	}
	/// <summary>
	/// 対象6伝票を新しい消費税計算方式（取引先マスタのTaxCalcUnit/TaxRounding・明細別Id_Tax）へ
	/// 揃える一括再計算（移行・既存データ救済用の一時処理。冪等）
	/// </summary>
	private CvMsg HandleTranTaxRebuild(CvMsg request, CallContext context) {
		var startTime = DateTime.Now;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var results = new TranTaxRebuildDb(_db).RebuildAll();
			_db.CompleteTransaction();
			var summary = TranTaxRebuildDb.BuildSummary(startTime, results);
			_logger.LogInformation("伝票税額再更新 {Summary}", summary.Replace(Environment.NewLine, " "));
			return CreateSuccessResponse(request.Flag, typeof(string), summary);
		}
		catch (Exception ex) {
			// 途中で落ちた場合は部分更新を残さない(明細Tax合計0の判定が壊れて再実行が効かなくなるため)
			_db.AbortTransaction();
			_logger.LogError(ex, "伝票税額再更新に失敗");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>
	/// 棚卸の店舗別状況照会。棚卸開始処理・棚卸確定処理の画面が店舗一覧を組み立てるのに使う。
	/// <para>
	/// 店舗ごとの棚卸日・計上月・開始済み／確定済み／再確定要と、基準日以外の日付で入力された
	/// 棚卸伝票の内訳を返す。確定処理の前に日付補正の要否を確認するためにも使う(設計書2.5 / 4)。
	/// </para>
	/// </summary>
	private CvMsg HandleStocktakeStatus(CvMsg request, CallContext context) {
		try {
			if (Common.DeserializeObject(request.DataMsg, request.DataType) is not StocktakeParameter param) {
				return CreateErrorResponse(request.Flag, -1, null, typeof(string), "エラー: パラメータのデシリアライズに失敗");
			}
			var stocktakeDb = new StocktakeDb(_db);
			var days = stocktakeDb.ResolveDays(param.FallbackMonth, param.SokoIds);
			var reply = new StocktakeStatusReply { Shops = stocktakeDb.FetchRefixStatus(days) };
			foreach (var day in days) {
				reply.Misdated.AddRange(stocktakeDb.FetchMisdatedTana(day));
			}
			return CreateSuccessResponse(request.Flag, typeof(StocktakeStatusReply), Common.SerializeObject(reply));
		}
		catch (Exception ex) {
			_logger.LogError(ex, "棚卸の店舗別状況照会に失敗");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>
	/// 再同期の実行結果サマリを組み立てる(開始/終了/所要時間はサーバ側の実測値)
	/// </summary>
	private static string BuildResyncSummary(DateTime startTime, int updated, int errorCount) {
		var endTime = DateTime.Now;
		var elapsed = endTime - startTime;
		var lines = new List<string> {
			$"更新行数={updated}",
			$"開始 {startTime:yyyy/MM/dd HH:mm:ss}",
			$"終了 {endTime:yyyy/MM/dd HH:mm:ss}",
			$"所要 {(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒",
		};
		if (errorCount > 0) {
			lines.Add($"失敗ルール数={errorCount}");
		}
		return string.Join(Environment.NewLine, lines);
	}
	/// <summary>
	/// Query系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvMsg HandleOpQuery(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		return param switch {
			QueryOneParam queryOne => HandleQueryOne(request.Flag, queryOne),
			QueryByIdParam queryById => HandleQueryById(request.Flag, queryById),
			QueryListSqlParam querySql => HandleQueryListSql(request.Flag, querySql),
			QueryListParam queryList => HandleQueryList(request.Flag, queryList),
			_ => throw new NotImplementedException(),
		};
	}

	/// <summary>
	/// Execute系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvMsg HandleOpExecute(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		return param switch {
			InsertParam insert => HandleInsert(request.Flag, insert),
			InsertBulkParam insertBulk => HandleBulkInsert(request.Flag, insertBulk),
			UpdateParam update => HandleUpdate(request.Flag, update),
			DeleteParam delete => HandleDelete(request.Flag, delete),
			DeleteByIdParam deleteById => HandleDeleteById(request.Flag, deleteById),
			DeleteBulkParam deleteBulk => HandleBulkDelete(request.Flag, deleteBulk),
			PartialUpdateParam partialUpdate => HandlePartialUpdate(request.Flag, partialUpdate),
			ShippingConfirmParam confirm => HandleShippingConfirm(request.Flag, confirm),
			ShippingCancelParam cancel => HandleShippingCancel(request.Flag, cancel),
			ShippingCreateParam create => HandleShippingCreate(request.Flag, create),
			OpeningBalanceImportParam opening => HandleOpeningBalanceImport(request.Flag, opening),
			_ => throw new NotImplementedException(),
		};
	}

	/// <summary>
	/// 期首残高（売掛・請求・買掛・支払）の洗い替え登録。対象日付の既存行を指定取引先ぶん削除してから登録する。
	/// <para>
	/// <c>Summary*</c> 系は <c>ITranSoko</c> でも <c>IBaseCodeName</c> でもないため、
	/// <see cref="WriteEffectRunner"/> の付随処理（在庫再集計・V*列伝播・引当）は対象外である。
	/// </para>
	/// </summary>
	private CvMsg HandleOpeningBalanceImport(CvFlag flag, OpeningBalanceImportParam opening) {
		_logger.LogInformation("パラメータ OpeningBalanceImportParam テーブル={Table} キー={KeyDate} 対象取引先={Count}件",
			opening.TableName, opening.KeyDate, opening.OwnerIds?.Length ?? 0);

		try {
			var result = new OpeningBalanceDb(_db).Import(opening);
			_logger.LogInformation("期首残高登録 削除={Deleted}件 登録={Inserted}件", result.Deleted, result.Inserted);
			return CreateSuccessResponse(flag, typeof(OpeningBalanceImportResult), Common.SerializeObject(result));
		}
		catch (ArgumentException ex) {
			// 入力条件（テーブル名・期首前・洗い替え範囲）の違反は画面で直せるのでメッセージだけ返す
			return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, ex.Message,
				typeof(OpeningBalanceImportParam), Common.SerializeObject(opening));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(OpeningBalanceImportParam), Common.SerializeObject(opening));
		}
	}

	/// <summary>
	/// 出荷指示確定。対象の配分に <c>KakuteiDay</c> を立てる。有効在庫（実在庫 − 引当数）が
	/// 1SKUでも負になる場合は <see cref="ShippingDb.ConfirmShipping"/> が全件拒否するので、
	/// 割れたSKUを <see cref="ShippingShortageDto"/> 配列で返して1件も確定しない。
	/// </summary>
	private CvMsg HandleShippingConfirm(CvFlag flag, ShippingConfirmParam confirm) {
		_logger.LogInformation("パラメータ ShippingConfirmParam 件数={Count} 確定日={KakuteiDay}",
			confirm.HaibunIds?.Length ?? 0, confirm.KakuteiDay);

		var shippingDb = new ShippingDb(_db);
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var confirmed = shippingDb.ConfirmShipping(confirm.HaibunIds ?? [], confirm.KakuteiDay, out var errors);
			if (errors.Count > 0) {
				// 有効在庫割れは1件も確定していない。書いていないが念のため戻す
				_db.AbortTransaction();
				var dto = errors
					.Select(e => new ShippingShortageDto(e.Id_Soko, e.Id_Shohin, e.Id_Col, e.Id_Siz, e.Shiji, e.Yuko))
					.ToArray();
				_logger.LogInformation("出荷指示確定 有効在庫割れ {Count}SKU", dto.Length);
				return CreateErrorResponse(flag, CvMsgErrorCode.ShippingUnavailable, "有効在庫が不足しています",
					typeof(ShippingShortageDto[]), Common.SerializeObject(dto));
			}
			_db.CompleteTransaction();
			_logger.LogInformation("出荷指示確定 確定={Confirmed}", confirmed);
			return CreateSuccessResponse(flag, typeof(ShippingConfirmResult), Common.SerializeObject(new ShippingConfirmResult(confirmed)));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>出荷指示確定の取消。まだ伝票を作っていない確定済み行の <c>KakuteiDay</c> を空へ戻す。</summary>
	private CvMsg HandleShippingCancel(CvFlag flag, ShippingCancelParam cancel) {
		_logger.LogInformation("パラメータ ShippingCancelParam 件数={Count}", cancel.HaibunIds?.Length ?? 0);

		var shippingDb = new ShippingDb(_db);
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var canceled = shippingDb.CancelConfirm(cancel.HaibunIds ?? []);
			_db.CompleteTransaction();
			_logger.LogInformation("出荷指示確定取消 取消={Canceled}", canceled);
			return CreateSuccessResponse(flag, typeof(ShippingCancelResult), Common.SerializeObject(new ShippingCancelResult(canceled)));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>
	/// 出荷処理。確定済み配分に実数量を入れ、出荷売上／移動伝票を作成して <c>EndFlag=1</c>（引当解除）にする。
	/// 楽観排他は <see cref="ShippingDb.ProcessShipping"/> が先に全行を検証し、競合なら何も書かずに返すので
	/// ここでトランザクションを戻して再取得を促す。
	/// </summary>
	private CvMsg HandleShippingCreate(CvFlag flag, ShippingCreateParam create) {
		var rows = create.Rows ?? [];
		_logger.LogInformation("パラメータ ShippingCreateParam 件数={Count} 伝票日={DenDay} 社員={IdShain}",
			rows.Length, create.DenDay, create.IdShain);

		var shippingDb = new ShippingDb(_db);
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var created = shippingDb.ProcessShipping(
				[.. rows.Select(r => (r.Id, r.ExpectedVdu, r.JitsuSu))], create.DenDay, create.IdShain, out var conflict);
			if (conflict) {
				_db.AbortTransaction();
				_logger.LogInformation("出荷処理 競合検知");
				return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, typeof(string), string.Empty);
			}
			_db.CompleteTransaction();
			_logger.LogInformation("出荷処理 伝票作成={Slips} 引当解除={Released}", created.Count, rows.Length);
			return CreateSuccessResponse(flag, typeof(ShippingCreateResult),
				Common.SerializeObject(new ShippingCreateResult([.. created], rows.Length)));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>
	/// 指定列だけを更新する。<c>Vdu</c> はサーバー側で採番し、全行を単一トランザクションで処理する。
	/// <para>
	/// 楽観排他は <see cref="HandleUpdate"/> と同じ考え方で行う。行ごとに一覧取得時点の
	/// <see cref="PartialUpdateRow.ExpectedVdu"/> を <c>WHERE</c> へ入れ、更新行数が0なら他端末の更新
	/// (または削除)と判断して全体をrollbackし、<see cref="CvMsgErrorCode.ConcurrentUpdate"/> を返す。
	/// </para>
	/// <para>
	/// 列名はSQL文へ直接埋め込むため、対象型にマップされた実プロパティ名と完全一致するものだけを採用する
	/// （一致した実プロパティ名を使って組み立てるので、クライアント文字列はSQLへ渡らない）。
	/// </para>
	/// </summary>
	private CvMsg HandlePartialUpdate(CvFlag flag, PartialUpdateParam partialUpdate) {
		_logger.LogInformation("パラメータ PartialUpdateParam.ItemType={ItemType} 列={Columns} 行数={RowCount}",
			partialUpdate.ItemType, string.Join(",", partialUpdate.Columns ?? []), partialUpdate.Rows?.Length ?? 0);

		if (!TryValidatePartialUpdate(partialUpdate, out var columns, out var error)) {
			return CreateErrorResponse(flag, CvMsgErrorCode.Unexpected, error, typeof(string), string.Empty);
		}
		var rows = partialUpdate.Rows ?? [];
		if (rows.Length == 0) {
			return CreateSuccessResponse(flag, typeof(PartialUpdateResult), Common.SerializeObject(new PartialUpdateResult(0)));
		}

		var tableName = _db.GetTableName(partialUpdate.ItemType);
		var setClause = string.Join(", ", columns.Select((c, i) => $"{c} = @{i}"));
		var sql = $"UPDATE {tableName} SET {setClause}, {nameof(BaseDbClass.Vdu)} = @{columns.Count}"
			+ $" WHERE {nameof(BaseDbClass.Id)} = @{columns.Count + 1} AND {nameof(BaseDbClass.Vdu)} = @{columns.Count + 2}";
		var vdate = Common.GetVdate();
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var updated = 0;
			// 部分更新の値は通信上すべて文字列で来る。SQLiteは列アフィニティで解釈するため
			// 現行どおり文字列のまま渡す。型に厳しい他DBのときだけ列のCLR型へ変換する。
			var columnTypes = _db.Dialect.TranslatesSql
				? ResolvePartialUpdateColumnTypes(partialUpdate.ItemType, columns)
				: null;
			foreach (var row in rows) {
				object[] args = [.. ConvertPartialUpdateValues(row.Values, columnTypes), vdate, row.Id, row.ExpectedVdu];
				var count = _db.Execute(sql, args);
				if (count == 0) {
					// 部分適用は作らない。1件でも競合したら全件戻して再取得させる。
					_db.AbortTransaction();
					_logger.LogInformation("部分更新 競合検知 {Table} Id={Id} ExpectedVdu={ExpectedVdu}", tableName, row.Id, row.ExpectedVdu);
					return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage,
						typeof(string), $"Id={row.Id}");
				}
				updated += count;
			}
			Effects.AfterPartialUpdate(partialUpdate.ItemType, columns, [.. rows.Select(r => r.Id)]);
			_db.CompleteTransaction();
			_logger.LogInformation("部分更新 {Table} 列={Columns} 更新行数={Updated}", tableName, string.Join(",", columns), updated);
			return CreateSuccessResponse(flag, typeof(PartialUpdateResult), Common.SerializeObject(new PartialUpdateResult(updated)));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, typeof(string), string.Empty);
		}
	}

	/// <summary>
	/// 部分更新の対象列のCLR型を解決する。解決できない列は <c>null</c> を置き、文字列のまま渡す。
	/// </summary>
	private static Type?[] ResolvePartialUpdateColumnTypes(Type itemType, List<string> columns) {
		var types = new Type?[columns.Count];
		for (var i = 0; i < columns.Count; i++) {
			var property = itemType.GetProperty(columns[i]);
			var type = property?.PropertyType;
			// Nullable<T> は T として扱う
			types[i] = type == null ? null : Nullable.GetUnderlyingType(type) ?? type;
		}
		return types;
	}

	/// <summary>
	/// 文字列で受けた値を列のCLR型へ変換する。
	/// <paramref name="columnTypes"/> が <c>null</c>（SQLite）のときは何もせず文字列のまま返す。
	/// </summary>
	private static object[] ConvertPartialUpdateValues(string[] values, Type?[]? columnTypes) {
		if (columnTypes == null) {
			return [.. values];
		}
		var args = new object[values.Length];
		for (var i = 0; i < values.Length; i++) {
			var value = values[i] ?? string.Empty;
			var type = i < columnTypes.Length ? columnTypes[i] : null;
			args[i] = ConvertPartialUpdateValue(value, type);
		}
		return args;
	}

	private static object ConvertPartialUpdateValue(string value, Type? type) {
		if (type == null || type == typeof(string)) {
			return value;
		}
		// 空文字は数値列では0、真偽列ではfalseとして扱う。SQLiteは空文字をそのまま格納するが、
		// 型に厳しいDBでは代入できないため既定値へ寄せる
		if (string.IsNullOrWhiteSpace(value)) {
			return type == typeof(bool) ? false : Activator.CreateInstance(type) ?? value;
		}
		try {
			if (type == typeof(bool)) {
				// 0/1 表現も受ける
				return bool.TryParse(value, out var flag) ? flag : value.Trim() != "0";
			}
			if (type.IsEnum) {
				return Enum.ToObject(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
			}
			return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
		}
		catch (Exception) {
			// 変換できない値はDB側のエラーに委ねる（サーバで勝手に丸めない）
			return value;
		}
	}

	/// <summary>
	/// 部分更新の要求を検証し、列名を対象型にマップされた実プロパティ名へ解決する。
	/// 対象外の型、列の未指定・存在しない列・禁止列・重複指定、行のIdと値数の不整合をすべて拒否する。
	/// </summary>
	private static bool TryValidatePartialUpdate(PartialUpdateParam partialUpdate, out List<string> columns, out string error) {
		columns = [];
		var itemType = partialUpdate.ItemType;
		if (!typeof(BaseDbClass).IsAssignableFrom(itemType)) {
			error = $"部分更新できない型です: {itemType.Name}";
			return false;
		}
		var requested = partialUpdate.Columns ?? [];
		if (requested.Length == 0) {
			error = "更新列が指定されていません";
			return false;
		}
		var mapped = itemType.GetProperties()
			.Where(p => p.GetCustomAttribute<NPoco.IgnoreAttribute>() == null
				&& p.GetCustomAttribute<NPoco.ResultColumnAttribute>() == null
				&& p.GetCustomAttribute<NPoco.ComputedColumnAttribute>() == null)
			.Select(p => p.Name)
			.ToList();
		foreach (var column in requested) {
			var name = (column ?? string.Empty).Trim();
			var actual = mapped.Find(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
			if (actual == null) {
				error = $"{itemType.Name} に存在しない列です: {name}";
				return false;
			}
			if (WriteEffectRunner.PartialUpdateDeniedColumns.Contains(actual, StringComparer.OrdinalIgnoreCase)) {
				error = $"部分更新では変更できない列です: {actual}";
				return false;
			}
			if (columns.Contains(actual, StringComparer.OrdinalIgnoreCase)) {
				error = $"列が重複しています: {actual}";
				return false;
			}
			columns.Add(actual);
		}
		// 値の数が列数と合わない行は、クライアント側の組み立て誤りなので更新前に全体を弾く
		foreach (var row in partialUpdate.Rows ?? []) {
			if (row.Id <= 0) {
				error = $"Idが不正です: {row.Id}";
				return false;
			}
			if ((row.Values?.Length ?? 0) != columns.Count) {
				error = $"Id={row.Id} の値の数が更新列数と一致しません";
				return false;
			}
		}
		error = string.Empty;
		return true;
	}

	private CvMsg HandleQueryOne(CvFlag flag, QueryOneParam queryOne) {
		LogDetailedRequest("パラメータ QueryOneParam.ItemType={ItemType} 内容={Payload}", () => [queryOne.ItemType, Common.SerializeObject(queryOne)]);

		var sql = TranslateClientSql(queryOne.AddWhere());
		try {
			var data = _db.Fetch(queryOne.ItemType, sql, queryOne.Parameters).FirstOrDefault();
			return data == null
				? CreateNotFoundResponse(flag)
				: CreateSuccessResponse(flag, data.GetType(), Common.SerializeObject(data));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvMsg HandleQueryById(CvFlag flag, QueryByIdParam queryById) {
		LogDetailedRequest("パラメータ QueryByIdParam.ItemType={ItemType} 内容={Payload}", () => [queryById.ItemType, Common.SerializeObject(queryById)]);

		try {
			var data = _db.Fetch(queryById.ItemType, "where Id = @0", queryById.Id).FirstOrDefault();
			if (data is BaseDbClass db && queryById.ExpectedVdu > 0 && db.Vdu != queryById.ExpectedVdu) {
				return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, data.GetType(), Common.SerializeObject(data));
			}
			return data == null
				? CreateNotFoundResponse(flag)
				: CreateSuccessResponse(flag, data.GetType(), Common.SerializeObject(data));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvMsg HandleQueryList(CvFlag flag, QueryListParam queryList) {
		var sql = TranslateClientSql(BuildQueryListSql(queryList));
		var listType = typeof(List<>).MakeGenericType(queryList.ItemType);

		LogDetailedRequest("パラメータ QueryListParam.ItemType={ItemType} 内容={Payload} SQL={Sql}", () => [queryList.ItemType, Common.SerializeObject(queryList), sql]);

		try {
			var list = _db.Fetch(queryList.ItemType, sql, queryList.Parameters);
			return list == null || list.Count == 0
				? CreateNotFoundResponse(flag, listType, "[]")
				: CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvMsg HandleQueryListSql(CvFlag flag, QueryListSqlParam querySql) {
		var sql = TranslateClientSql(querySql.Sql ?? string.Empty, querySql.QueryKey);
		var listType = typeof(List<>).MakeGenericType(querySql.ItemType);

		LogDetailedRequest("パラメータ QueryListSqlParam.ItemType={ItemType} 内容={Payload} SQL={Sql}", () => [querySql.ItemType, Common.SerializeObject(querySql), sql]);

		try {
			var list = _db.Fetch(querySql.ItemType, sql, querySql.Parameters);
			return list == null || list.Count == 0
				? CreateNotFoundResponse(flag, listType, "[]")
				: CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvMsg HandleInsert(CvFlag flag, InsertParam insert) {
		LogDetailedRequest("パラメータ InsertParam.ItemType={ItemType} 内容={Payload}", () => [insert.ItemType, Common.SerializeObject(insert)]);

		var item = insert.GetItemObject();
		var vdate = SetCreatedAuditValues(insert.ItemType, item);

		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			_db.Insert(item);
			// 副作用(派生展開・在庫・引当)は追加と同一トランザクション内で実行する
			LogEffects(insert.ItemType, Effects.After(WriteOp.Insert, insert.ItemType, item, null, vdate));
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, item.GetType(), Common.SerializeObject(item));
		}
	}
	/// <summary>
	/// 追加処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="insertBulk"></param>
	/// <returns></returns>
	private CvMsg HandleBulkInsert(CvFlag flag, InsertBulkParam insertBulk) {
		LogDetailedRequest("パラメータ InsertBulkParam.ItemType={ItemType} 内容={Payload}", () => [insertBulk.ItemType, Common.SerializeObject(insertBulk)]);

		// JSON配列 → List<ItemType> にデシリアライズ
		var listType = typeof(List<>).MakeGenericType(insertBulk.ItemType);
		var items = Common.DeserializeObject(insertBulk.Item, listType);
		if (items is not IList list || list.Count == 0) {
			return CreateNotFoundResponse(flag, listType, "[]");
		}
		// 配分は数百件が一括登録されるため、引当キーを集めてループ後に一度だけ引き直す。
		// 在庫は差分の加減算なので、まとめずに行ごとに計算する必要がある
		var reserveKeys = new HashSet<ReserveKey>();
		var effects = WriteEffectResult.Empty;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			foreach (var item in list) {
				var vdate = SetCreatedAuditValues(insertBulk.ItemType, item);
				_db.Insert(item);
				effects = effects.Add(Effects.After(WriteOp.Insert, insertBulk.ItemType, item, null, vdate, reserveKeys));
			}
			effects = effects.Add(new WriteEffectResult(0, Effects.FlushReserve(reserveKeys), 0, 0));
			LogEffects(insertBulk.ItemType, effects);
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, listType, Common.SerializeObject(list));
		}
	}
	/// <summary>
	/// 更新処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="update"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	private CvMsg HandleUpdate(CvFlag flag, UpdateParam update) {
		LogDetailedRequest("パラメータ UpdateParam.ItemType={ItemType} 内容={Payload}", () => [update.ItemType, Common.SerializeObject(update)]);

		var item = update.GetItemObject();
		if (!typeof(BaseDbClass).IsAssignableFrom(update.ItemType) || item is not BaseDbClass db) {
			throw new NotImplementedException();
		}

		var vdate = Common.GetVdate();
		try {
			// 行が無い = 他端末が削除済み。楽観排他と同じ扱いで再取得させる
			if (FetchExistingBaseDbItem(update.ItemType, db.Id) is not BaseDbClass org) {
				return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
			}

			if (db.Vdu != org.Vdu) {
				return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
			}
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			// 在庫は差分の加減算なので、DB上の行が変わる前に旧値ぶんを反転しておく
			Effects.Before(WriteOp.Update, update.ItemType, org);
			db.Vdu = vdate;
			_db.Update(item);
			LogEffects(update.ItemType, Effects.After(WriteOp.Update, update.ItemType, item, org, vdate));
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) when (ex is not NotImplementedException) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, update.ItemType, Common.SerializeObject(item));
		}
	}
	/// <summary>
	/// 削除処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="delete"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	private CvMsg HandleDelete(CvFlag flag, DeleteParam delete) {
		LogDetailedRequest("パラメータ DeleteParam.ItemType={ItemType} 内容={Payload}", () => [delete.ItemType, Common.SerializeObject(delete)]);

		var item = delete.GetItemObject();
		if (!typeof(BaseDbClass).IsAssignableFrom(delete.ItemType) || item is not BaseDbClass db) {
			throw new NotImplementedException();
		}

		// 行が無い = 他端末が削除済み。楽観排他と同じ扱いで再取得させる
		if (FetchExistingBaseDbItem(delete.ItemType, db.Id) is not BaseDbClass org) {
			return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
		}

		if (db.Vdu != org.Vdu) {
			return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
		}
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			// 在庫は差分の加減算なので、行を消す前に旧値ぶんを反転しておく
			Effects.Before(WriteOp.Delete, delete.ItemType, org);
			_db.Delete(item);
			// 引当数は削除後のTranHaibunから引き直すので、Deleteの後に実行する
			LogEffects(delete.ItemType, Effects.After(WriteOp.Delete, delete.ItemType, item, org, 0));
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, delete.ItemType, Common.SerializeObject(item));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, delete.ItemType, Common.SerializeObject(item));
		}
	}

	private CvMsg HandleDeleteById(CvFlag flag, DeleteByIdParam deleteById) {
		LogDetailedRequest("パラメータ DeleteByIdParam.ItemType={ItemType} Id={Id} 内容={Payload}", () => [deleteById.ItemType, deleteById.Id, Common.SerializeObject(deleteById)]);

		if (!typeof(BaseDbClass).IsAssignableFrom(deleteById.ItemType)) {
			throw new NotImplementedException();
		}
		// 行が無い = 他端末が削除済み。楽観排他と同じ扱いで再取得させる
		if (FetchExistingBaseDbItem(deleteById.ItemType, deleteById.Id) is not BaseDbClass item) {
			return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage,
				deleteById.ItemType, string.Empty);
		}

		if (deleteById.OriginalVdu != item.Vdu) {
			return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
		}
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			// 在庫は差分の加減算なので、行を消す前に旧値ぶんを反転しておく
			Effects.Before(WriteOp.Delete, deleteById.ItemType, item);
			_db.Delete(item);
			// 引当数は削除後のTranHaibunから引き直すので、Deleteの後に実行する
			LogEffects(deleteById.ItemType, Effects.After(WriteOp.Delete, deleteById.ItemType, item, item, 0));
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, deleteById.ItemType, Common.SerializeObject(item));
		}
	}

	/// <summary>
	/// Id指定の一括削除。洗い替え登録（既存行を消してから入れ直す）を1往復・1トランザクションで行う。
	/// <para>
	/// 楽観排他は行単位で<b>先に全行を検証</b>する。行が無い（他端末が削除済み）か <c>Vdu</c> が
	/// 食い違う行が1件でもあれば、<b>何も削除せず</b>rollbackして再取得させる（部分適用しない）。
	/// </para>
	/// <para>
	/// 引当数は削除後の <see cref="TranHaibun"/> から引き直すため、キーを溜めてループ後に一度だけ処理する
	/// （<see cref="HandleBulkInsert"/> と同じ理由）。在庫は差分の加減算なので行ごとに反転する。
	/// </para>
	/// </summary>
	private CvMsg HandleBulkDelete(CvFlag flag, DeleteBulkParam deleteBulk) {
		LogDetailedRequest("パラメータ DeleteBulkParam.ItemType={ItemType} 行数={RowCount} 内容={Payload}",
			() => [deleteBulk.ItemType, deleteBulk.Rows?.Length ?? 0, Common.SerializeObject(deleteBulk)]);

		if (!typeof(BaseDbClass).IsAssignableFrom(deleteBulk.ItemType)) {
			throw new NotImplementedException();
		}
		// 同じIdを2回渡されると2件目が「行が無い」と判定されるので、先に除いておく
		var rows = (deleteBulk.Rows ?? []).Where(r => r.Id > 0).DistinctBy(r => r.Id).ToList();
		if (rows.Count == 0) {
			return CreateSuccessResponse(flag, typeof(DeleteBulkResult), Common.SerializeObject(new DeleteBulkResult(0)));
		}
		var reserveKeys = new HashSet<ReserveKey>();
		var effects = WriteEffectResult.Empty;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var targets = new List<BaseDbClass>(rows.Count);
			foreach (var row in rows) {
				if (FetchExistingBaseDbItem(deleteBulk.ItemType, row.Id) is not BaseDbClass item || item.Vdu != row.ExpectedVdu) {
					_db.AbortTransaction();
					_logger.LogInformation("一括削除 競合検知 {ItemType} Id={Id} ExpectedVdu={ExpectedVdu}",
						deleteBulk.ItemType.Name, row.Id, row.ExpectedVdu);
					return CreateErrorResponse(flag, CvMsgErrorCode.ConcurrentUpdate, ConcurrentUpdateMessage,
						deleteBulk.ItemType, $"Id={row.Id}");
				}
				targets.Add(item);
			}
			foreach (var item in targets) {
				// 在庫は差分の加減算なので、行を消す前に旧値ぶんを反転しておく
				Effects.Before(WriteOp.Delete, deleteBulk.ItemType, item);
				_db.Delete(item);
				effects = effects.Add(Effects.After(WriteOp.Delete, deleteBulk.ItemType, item, item, 0, reserveKeys));
			}
			effects = effects.Add(new WriteEffectResult(0, Effects.FlushReserve(reserveKeys), 0, 0));
			LogEffects(deleteBulk.ItemType, effects);
			_db.CompleteTransaction();
			_logger.LogInformation("一括削除 {ItemType} 削除行数={Deleted}", deleteBulk.ItemType.Name, targets.Count);
			return CreateSuccessResponse(flag, typeof(DeleteBulkResult), Common.SerializeObject(new DeleteBulkResult(targets.Count)));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, deleteBulk.ItemType, Common.SerializeObject(deleteBulk));
		}
	}
	/// <summary>
	/// ToDo: 出力系の処理を集約して、マスタ以外も対応できるようにする
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvMsg HandleOutData(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		try {
			if (param is OutDataHhtMasterParam outDataParam) {
				LogDetailedRequest("パラメータ HhtMaster isFix={IsFix} OutMasterMei={OutMasterMei} 内容={Payload}", () => [outDataParam.IsFixedLengthFormat, outDataParam.ReservedInt, Common.SerializeObject(outDataParam)]);

				var list = new HhtProcess(_db).CreateMaster(outDataParam.IsFixedLengthFormat, outDataParam.ReservedInt);
				return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(list));
			}
			throw new NotImplementedException();
		}
		catch (Exception ex) {
			return CreateExceptionResponse(request.Flag, ex, typeof(List<string>), request.DataMsg);
		}
	}


	private static Dictionary<string, string> GetEnvironmentVariables() {
		var envVars = Environment.GetEnvironmentVariables();

		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry entry in envVars) {
			var key = entry.Key?.ToString() ?? string.Empty;
			var value = entry.Value?.ToString() ?? string.Empty;
			result[key] = value;
		}

		return result;
	}

	private string BuildQueryListSql(QueryListParam queryList) {
		if (queryList is QueryListSimpleParam) {
			return $"select Id,Vdc,Vdu,Code,Name,Ryaku,Kana From {_db.GetTableName(queryList.ItemType)} {queryList.AddWhereOrder()}";
		}

		return queryList.AddWhereOrder();
	}

	/// <summary>
	/// 追加時の監査値(<c>Vdc</c> / <c>Vdu</c>)をサーバー側で採番する。
	/// </summary>
	/// <returns>採番した更新日時。対象外の型では0</returns>
	private long SetCreatedAuditValues(Type itemType, object item) {
		if (!typeof(BaseDbClass).IsAssignableFrom(itemType) || item is not BaseDbClass db) {
			return 0;
		}
		// SysLoginのLastDateは、ログイン時に更新する
		if (typeof(SysLogin).IsAssignableFrom(itemType)) {
			var login = (SysLogin)item;
			login.LastDate = "";
		}
		var vdate = Common.GetVdate();
		db.Vdc = vdate;
		db.Vdu = vdate;
		return vdate;
	}

	/// <summary>
	/// 旧Oracle DBからの変換処理を組み立てる。接続先の決定はサーバーの責務なのでここに置く。
	/// </summary>
	private ConvertDb CreateConvertDb() {
		var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);
		return new ConvertDb(fromDb, _db);
	}

	/// <summary>
	/// 副作用の実行結果をログへ出す(ログはCvServerの責務)。何も起きていない場合は出さない。
	/// </summary>
	private void LogEffects(Type itemType, WriteEffectResult result) {
		if (result == WriteEffectResult.Empty) {
			return;
		}
		_logger.LogInformation("付随処理 {ItemType} {Effects}", itemType.Name, result.ToString());
	}

	/// <summary>
	/// Id指定でDB上の行を読む。行が無ければ null を返す(他端末が削除済みのケース)。
	/// </summary>
	private BaseDbClass? FetchExistingBaseDbItem(Type itemType, object id) =>
		_db.Fetch(itemType, "where Id=@0", id)?.OfType<BaseDbClass>().FirstOrDefault();

	private static CvMsg CreateSuccessResponse(CvFlag flag, Type? dataType, string? dataMsg) {
		return new CvMsg {
			Flag = flag,
			Code = 0,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvMsg CreateNotFoundResponse(CvFlag flag, Type? dataType = null, string? dataMsg = null) {
		return new CvMsg {
			Flag = flag,
			Code = NotFoundCode,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvMsg CreateExceptionResponse(CvFlag flag, Exception ex, Type? dataType, string? dataMsg) {
		return CreateErrorResponse(flag, CvMsgErrorCode.Unexpected, ex.Message, dataType, dataMsg);
	}

	private static CvMsg CreateErrorResponse(CvFlag flag, int code, string? option, Type? dataType, string? dataMsg) {
		return new CvMsg {
			Flag = flag,
			Code = code,
			Option = option ?? string.Empty,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}
}
