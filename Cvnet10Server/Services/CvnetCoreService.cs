using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10AppShared;
using Cvnet10Base;
using Cvnet10Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Oracle.ManagedDataAccess.Client;
using ProtoBuf.Grpc;
using System.Collections;


namespace Cvnet10Server.Services;

public partial class CvnetCoreService : ICvnetCoreService {
	private readonly ILogger<CvnetCoreService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IWebHostEnvironment _env;
	private readonly ExDatabase _db;
	// private readonly IScheduler _scheduler;
	private readonly IHttpContextAccessor _httpContextAccessor;
	public CvnetCoreService(ILogger<CvnetCoreService> logger, IConfiguration configuration, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, ExDatabase db) {
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(env);
		ArgumentNullException.ThrowIfNull(httpContextAccessor);
		_logger = logger;
		_configuration = configuration;
		_env = env;
		/*
		var connStr = _configuration.GetConnectionString("sqlite");
		if (string.IsNullOrWhiteSpace(connStr)) {
			throw new InvalidOperationException("Connection string 'sqlite' is not configured.");
		}
		_db = ExSqliteDatabase.GetDbConn(connStr);
		 */
		_db = db;

		// _scheduler = scheduler;
		_httpContextAccessor = httpContextAccessor;
	}
	// ToDo : テストが終わったら、[AllowAnonymous] を [Authorize] へ変更

	[AllowAnonymous]
	public Task<CvnetMsg> QueryMsgAsync(CvnetMsg request, CallContext context = default) {
		var result = new CvnetMsg() { Flag = CvnetFlag.Msg800_Error_Start };
		result.Code = -1;

		if (request.Flag == CvnetFlag.Msg001_CopyReply) {
			result.Code = 0;
			result.Flag = request.Flag;
			result.DataType = request.DataType;
			result.DataMsg = request.DataMsg;
		}
		else if (request.Flag == CvnetFlag.Msg002_GetVersion) {
			result.Code = 0;
			result.DataType = typeof(string);
			result.DataMsg = Common.Version;
		}
		else if (request.Flag == CvnetFlag.Msg003_GetEnv) {
			result.Code = 0;
            // 環境変数を取得して Dictionary<string,string> に変換、JSON シリアライズして返す
            var envVars = Environment.GetEnvironmentVariables();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in envVars) {
                var key = entry.Key?.ToString() ?? string.Empty;
                var value = entry.Value?.ToString() ?? string.Empty;
                dict[key] = value;
            }

            result.DataType = typeof(Dictionary<string, string>);
            result.DataMsg = Common.SerializeObject(dict);
        }
		else if (request.Flag == CvnetFlag.MSg004_ConvertDb) {
			result.Code = 0;
			var fromDb = new ExDatabase(new OracleConnection("User Id=CV00PKG;Password=CV00PKG;Data Source=192.168.9.243/cvnet;"));
			var toDb = new ExDatabase(new SqliteConnection("Data Source=sample.db"));
			var start = DateTime.Now;
			var cnvDb = new ConvertDb(fromDb, toDb);
			var dict = new Dictionary<string, string>();
			try {
				cnvDb.ConvertAll();
				dict["Status"] = "Success";
				var timespan = DateTime.Now - start;
				dict["Timesec"] = timespan.TotalSeconds.ToString();
			}
			catch (Exception ex) {
				dict["Status"] = "Error";
				dict["Message"] = ex.Message;
			}
			result.DataType = typeof(Dictionary<string, string>);
			result.DataMsg = Common.SerializeObject(dict);
		}
		else if (request.Flag == CvnetFlag.MSg005_Test) {
			result.Code = 0;
			var start = DateTime.Now;
			_db.CreateTable<MasterShain>(isForce: true);

			var result0 = _db.Fetch<Dictionary<string, object>>("""
				SELECT 
				    m.Id,
				    m.Code, -- PRD001等
				    m.Name, -- ブランドA...等
				    -- JSON内の各フィールドを展開
				    json_extract(value, '$.Id_MeiCol') AS Id_MeiCol,
				    json_extract(value, '$.Id_MeiSiz') AS Id_MeiSiz,
				    json_extract(value, '$.Mei_Col')   AS ColorName,
				    json_extract(value, '$.Mei_Siz')   AS SizeName,
				    json_extract(value, '$.Jan2')      AS SizeCode
				FROM 
				    Test202601Master m,
				    json_each(m.Jcolsiz);
				""");


			var wrk = result0.Count();

			/*
			_db.CreateTable<MasterMeisho>(isForce: true);

			var meishoList = new List<MasterMeisho>();
			meishoList.Add(new MasterMeisho { Id = 1, Kubun= "BRD", Code = "001", Name = "ブランドA", Ryaku = "brand-A", Kana = "ぶらんどえー" });
			meishoList.Add(new MasterMeisho { Id = 2, Kubun = "BRD", Code = "002", Name = "ブランドB", Ryaku = "brand-B", Kana = "ぶらんどびー" });
			meishoList.Add(new MasterMeisho { Id = 3, Kubun = "ITM", Code = "001", Name = "アイテムC", Ryaku = "item-C", Kana = "あいてむしー" });	
			meishoList.Add(new MasterMeisho { Id = 4, Kubun = "ITM", Code = "002", Name = "アイテムD", Ryaku = "item-D", Kana = "あいてむでー" });	
			meishoList.Add(new MasterMeisho { Id = 5, Kubun = "TNJ", Code = "202510", Name = "2025冬", Ryaku = "2025win", Kana = "2025ふゆ" });
			meishoList.Add(new MasterMeisho { Id = 6, Kubun = "TNJ", Code = "202601", Name = "2026春夏", Ryaku = "2026spring", Kana = "2026はるなつ" });
			meishoList.Add(new MasterMeisho { Id = 7, Kubun = "COL", Code = "0011", Name = "ブラック", Ryaku = "Black", Kana = "ブラック" });
			meishoList.Add(new MasterMeisho { Id = 8, Kubun = "COL", Code = "0022", Name = "ホワイト", Ryaku = "White", Kana = "ホワイト" });
			meishoList.Add(new MasterMeisho { Id = 9, Kubun = "SIZ", Code = "0000", Name = "フリー", Ryaku = "Free", Kana = "ふりー" });
			_db.InsertBulk<MasterMeisho>(meishoList);
			*/
			/*
			_db.CreateTable<Test202601Master>(isForce: true);
			var productSeeds = new[] {
				new { Code = "PRD001", Name = "ブランドA アイテムC 冬展#1", Ryaku = "A-C-W1", Kana = "えーしーふゆいち", Brand = "001", Item = "001", Tenji = "202510", Id_Brd=1,Id_Itm=3,Id_Tnj=5 },
				new { Code = "PRD002", Name = "ブランドA アイテムD 冬展#2", Ryaku = "A-D-W2", Kana = "えーでーふゆに", Brand = "001", Item = "002", Tenji = "202510" , Id_Brd=1,Id_Itm=4,Id_Tnj=5},
				new { Code = "PRD003", Name = "ブランドB アイテムC 冬展#1", Ryaku = "B-C-W1", Kana = "びーしーふゆいち", Brand = "002", Item = "001", Tenji = "202510" , Id_Brd=2,Id_Itm=3,Id_Tnj=5},
				new { Code = "PRD004", Name = "ブランドB アイテムD 冬展#2", Ryaku = "B-D-W2", Kana = "びーでーふゆに", Brand = "002", Item = "002", Tenji = "202510" , Id_Brd=2,Id_Itm=4,Id_Tnj=5},
				new { Code = "PRD005", Name = "ブランドA アイテムC 春夏#1", Ryaku = "A-C-S1", Kana = "えーしーはるなついち", Brand = "001", Item = "001", Tenji = "202601" , Id_Brd=1,Id_Itm=3,Id_Tnj=6},
				new { Code = "PRD006", Name = "ブランドA アイテムD 春夏#2", Ryaku = "A-D-S2", Kana = "えーではるなつに", Brand = "001", Item = "002", Tenji = "202601" , Id_Brd=1,Id_Itm=4,Id_Tnj=6},
				new { Code = "PRD007", Name = "ブランドB アイテムC 春夏#1", Ryaku = "B-C-S1", Kana = "びーしーはるなついち", Brand = "002", Item = "001", Tenji = "202601" , Id_Brd=2,Id_Itm=3,Id_Tnj=6},
				new { Code = "PRD008", Name = "ブランドB アイテムD 春夏#2", Ryaku = "B-D-S2", Kana = "びーではるなつに", Brand = "002", Item = "002", Tenji = "202601" , Id_Brd=2,Id_Itm=4,Id_Tnj=6},
				new { Code = "PRD009", Name = "ブランドA アイテムC 春夏限定", Ryaku = "A-C-LTD", Kana = "えーしーげんてい", Brand = "001", Item = "001", Tenji = "202601" , Id_Brd=1,Id_Itm=3,Id_Tnj=6},
				new { Code = "PRD010", Name = "ブランドB アイテムD 冬限定", Ryaku = "B-D-LTD", Kana = "びーでーげんてい", Brand = "002", Item = "002", Tenji = "202510" , Id_Brd=2,Id_Itm=4,Id_Tnj=5},
			};
			foreach (var seed in productSeeds) {
				var product = new Test202601Master {
					Code = seed.Code,
					Name = seed.Name,
					Ryaku = seed.Ryaku,
					Kana = seed.Kana,
					Id_MeiBrand = seed.Id_Brd,
					Id_MeiItem = seed.Id_Itm,
					Id_MeiTenji = seed.Id_Tnj,
					Jcolsiz = new List<MasterShohinColSiz>() { new MasterShohinColSiz { Id_MeiCol=7,Id_MeiSiz=9, Jan1 = "SIZE", Jan2 = "M" }, new MasterShohinColSiz { Id_MeiCol = 8, Id_MeiSiz = 9, Jan1 = "SIZE", Jan2 = "L" } }
				};
				_db.Insert(product);
			}
			*/
			const string selectSql = """
select T.*, m1.Name as Mei_Brand, m2.Name as  Mei_Item, m3.Name as  Mei_Tenji
from Test202601Master T
left join MasterMeisho m1 on T.Id_MeiBrand = m1.Id
left join MasterMeisho m2 on T.Id_MeiItem = m2.Id
left join MasterMeisho m3 on T.Id_MeiTenji = m3.Id
order by T.Id
""";
			var retdb = _db.Fetch<Test202601Master>(selectSql);
			foreach(var item in retdb) {
				if(item.Jcolsiz != null) {
					item.Jcolsiz?[0]?.Jan3 = $"{item.Id} : 作成 {DateTime.Now.ToLongTimeString()}";
					foreach (var sub in item.Jcolsiz!) {
						var meiColData = _db.Fetch<MasterMeisho>("where Id = @Id", new { Id = sub.Id_MeiCol }).FirstOrDefault();
						var meiSizData = _db.Fetch<MasterMeisho>("where Id = @Id", new { Id = sub.Id_MeiSiz }).FirstOrDefault();
						if(meiColData != null) {
							sub.Mei_Col = meiColData.Name;
						}
						if (meiSizData != null) {
							sub.Mei_Siz = meiSizData.Name;
						}
					}
					_db.Update(item);
				}
			}
			result.Flag = request.Flag;
			result.DataType = typeof(List<Test202601Master>);
			result.DataMsg = Common.SerializeObject(retdb);
			var timespan = DateTime.Now - start;

		}
		return Task.FromResult(result);
	}
}