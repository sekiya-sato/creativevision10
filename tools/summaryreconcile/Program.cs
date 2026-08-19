using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

// 請求計算・支払計算の実DB突合ハーネス（UAT-05/06 再現）。
// 既存取引先へ管理されたテスト伝票を投入し、売掛/請求・買掛/支払を計算して
// 請求台帳・支払台帳の出力を突合する。テスト月=202607（既存取引ゼロのクリーンルーム）。
//
// 使い方:  summaryreconcile <command> [dbPath]
//   seed          既存テストデータを掃除して再投入し、4計算を実行（既定）
//   show          現在の SummaryUriSei/SummaryKaiShi を台帳SQLで表示（突合）
//   idempotent    計算を2回実行し Summary スナップショットが一致するか（D-02 Rebuild冪等性・D-03 番号維持）
//   closingcheck  締日を変更→締日変更検査SQLで不一致検出→送信ブロック→締日を復元（E7 締日変更警告）
//   all           seed → show → idempotent → closingcheck を順に実行
//   dbPath        省略時 C:\gitroot\new2022\cv10\CvServer\server-user163.db
//
// 前提: dbPath は開発用DB。実運用DBには使わない。実行前にバックアップ推奨（refer/back/）。

var command = args.Length > 0 ? args[0] : "seed";
var dbPath = args.Length > 1 ? args[1] : @"C:\gitroot\new2022\cv10\CvServer\server-user163.db";

const string Month = "202607";
const string DFrom = "20260701";
const string DTo = "20260731";
const int ShimeMatched = 99;   // テスト取引先の実マスタ締日（末日）
const int ShimeChanged = 20;   // 締日変更検査を発火させる別締日

// KIN 区分マスタ Id（開発DB server-user163.db の実値）
const long KinCash = 11783, KinFee = 11782, KinOffset = 11785;

var cs = new SqliteConnectionStringBuilder {
    DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
}.ToString();
using var conn = new SqliteConnection(cs);
conn.Open();
var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
var summaryDb = new SummaryDb(db);

long tokui1 = db.Single<MasterTokui>("where Code=@0", "000002").Id;
long tokui2 = db.Single<MasterTokui>("where Code=@0", "000014").Id;
long shiire1 = db.Single<MasterShiire>("where Code=@0", "001").Id;
long shiire2 = db.Single<MasterShiire>("where Code=@0", "002").Id;

static string DL(string c) => $"case when length({c})=8 then substr({c},1,4)||'/'||substr({c},5,2)||'/'||substr({c},7,2) else ifnull({c},'') end";

void Clean() {
    db.Execute("DELETE FROM Tran00Uriage  WHERE Id_Tokui IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", tokui1, tokui2, DFrom, DTo);
    db.Execute("DELETE FROM Tran06Nyukin  WHERE Id_Torisaki IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", tokui1, tokui2, DFrom, DTo);
    db.Execute("DELETE FROM Tran03Shiire   WHERE Id_Shiire IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", shiire1, shiire2, DFrom, DTo);
    db.Execute("DELETE FROM Tran07Shiharai WHERE Id_Torisaki IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", shiire1, shiire2, DFrom, DTo);
    db.Execute("DELETE FROM SummaryUriKake WHERE DenMonth=@0", Month);
    db.Execute("DELETE FROM SummaryKaiKake WHERE DenMonth=@0", Month);
    db.Execute("DELETE FROM SummaryUriSei  WHERE DenDay=@0", DTo);
    db.Execute("DELETE FROM SummaryKaiShi  WHERE DenDay=@0", DTo);
}

Tran00Uriage Uri(string day, long id, EnumUri00 k, int total, int tax) { var t = new Tran00Uriage { DenDay = day, KakeDay = day, Id_Tokui = id, Total = total, KingakuTotal = total, Tax = tax, IsPay = 1 }; t.EnKubun = k; return t; }
Tran03Shiire Shi(string day, long id, EnumShiire k, int total, int tax) { var t = new Tran03Shiire { DenDay = day, KakeDay = day, Id_Shiire = id, Total = total, KingakuTotal = total, Tax = tax, IsPay = 1 }; t.EnKubun = k; return t; }
List<TranKinMeisai> Kin((long id, int kin)[] m) => [.. m.Select((x, i) => new TranKinMeisai { No = i + 1, Id_Kin = x.id, Kingaku = x.kin })];
Tran06Nyukin Nyu(string day, long id, (long, int)[] m) => new() { KakeDay = day, Id_Torisaki = id, KingakuTotal = m.Sum(x => x.Item2), Jmeisai = Kin(m) };
Tran07Shiharai Sih(string day, long id, (long, int)[] m) => new() { KakeDay = day, Id_Torisaki = id, KingakuTotal = m.Sum(x => x.Item2), Jmeisai = Kin(m) };

void Seed() {
    db.Insert(Uri("20260705", tokui1, EnumUri00.Uriage, 100000, 10000));
    db.Insert(Uri("20260712", tokui1, EnumUri00.Henpin, 20000, 2000));
    db.Insert(Uri("20260718", tokui1, EnumUri00.Nebiki, 5000, 500));
    db.Insert(Nyu("20260725", tokui1, [(KinCash, 50000), (KinFee, 440)]));
    db.Insert(Uri("20260710", tokui2, EnumUri00.Uriage, 30000, 3000));
    db.Insert(Nyu("20260728", tokui2, [(KinCash, 33000)]));
    db.Insert(Shi("20260706", shiire1, EnumShiire.Shiire, 80000, 8000));
    db.Insert(Shi("20260714", shiire1, EnumShiire.Henpin, 10000, 1000));
    db.Insert(Sih("20260726", shiire1, [(KinCash, 50000), (KinOffset, 20000)]));
    db.Insert(Shi("20260709", shiire2, EnumShiire.Shiire, 15000, 1500));
}

void Calc() {
    var a = summaryDb.CalcSummaryUriKake(Month, Month);
    var b = summaryDb.CalcSummaryUriSei(Month, ShimeMatched, "000002", "000014");
    var c = summaryDb.CalcSummaryKaiKake(Month, Month);
    var d = summaryDb.CalcSummaryKaiShi(Month, ShimeMatched, "001", "002");
    Console.WriteLine($"Calc rows: UriKake={a} UriSei={b} KaiKake={c} KaiShi={d}");
}

void Show() {
    Console.WriteLine("\n===== 請求台帳（発行控え）=====");
    foreach (var r in db.Fetch<dynamic>($@"
SELECT u.SeikyuNo AS 番号, {DL("u.DenDay")} AS 請求日, t.Code AS CD, t.Name AS 名,
       u.Uriage, u.Henpin, u.Nebiki, u.Tax, u.TotalSales AS 売上額, u.TotalIn AS 入金額, u.Balance AS 残高,
       {DL("u.NyukinYoteiDay")} AS 入金予定日, u.Renban AS 再
FROM SummaryUriSei u JOIN MasterTokui t ON t.Id=u.Id_Tokui WHERE u.DenDay=@0 ORDER BY t.Code", DTo))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));

    Console.WriteLine("\n===== 支払台帳（発行控え）=====");
    foreach (var r in db.Fetch<dynamic>($@"
SELECT {DL("k.DenDay")} AS 支払日, s.Code AS CD, s.Name AS 名,
       k.Shiire, k.Henpin, k.Nebiki, k.Tax, k.TotalShiire AS 仕入額, k.TotalOut AS 支払額, k.Balance AS 残高,
       {DL("k.ShiharaiYoteiDay")} AS 支払予定日
FROM SummaryKaiShi k JOIN MasterShiire s ON s.Id=k.Id_Shiire WHERE k.DenDay=@0 ORDER BY s.Code", DTo))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));
}

string[] Snapshot() => [
    .. db.Fetch<SummaryUriSei>("where DenDay=@0 order by Id_Tokui", DTo)
        .Select(x => $"URI {x.Id_Tokui}:{x.SeikyuNo}:R{x.Renban}:{x.TotalSales}:{x.TotalIn}:{x.Balance}:{x.NyukinYoteiDay}"),
    .. db.Fetch<SummaryKaiShi>("where DenDay=@0 order by Id_Shiire", DTo)
        .Select(x => $"KAI {x.Id_Shiire}:{x.TotalShiire}:{x.TotalOut}:{x.Balance}:{x.ShiharaiYoteiDay}"),
];

bool Idempotent() {
    Console.WriteLine("\n----- idempotent (D-02/D-03) -----");
    var first = Snapshot();
    Calc(); // 2回目（＝Rebuild相当の再計算）
    var second = Snapshot();
    var same = first.SequenceEqual(second);
    Console.WriteLine($"1回目/2回目のスナップショット一致: {(same ? "PASS" : "FAIL")}");
    if (!same) foreach (var d in first.Zip(second).Where(z => z.First != z.Second)) Console.WriteLine($"  DIFF: {d.First}  <>  {d.Second}");
    return same;
}

bool ClosingCheck() {
    Console.WriteLine("\n----- closingcheck (E7 締日変更警告) -----");
    // 変更前: 締日一致 → 不一致0件
    var uriBefore = SummaryRebuildClosingCheck.FindMismatches("売掛", db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.UriClosingCheckSql, Month, Month));
    var kaiBefore = SummaryRebuildClosingCheck.FindMismatches("買掛", db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.KaiClosingCheckSql, Month, Month));
    Console.WriteLine($"変更前 不一致: 売掛={uriBefore.Count} 買掛={kaiBefore.Count}  送信可={SummaryRebuildClosingCheck.CanStartRequestDispatch([.. uriBefore, .. kaiBefore])}");

    // 締日をテスト取引先だけ 99→20 に変更
    db.Execute("UPDATE MasterTokui  SET Shime1=@0 WHERE Code IN ('000002','000014')", ShimeChanged);
    db.Execute("UPDATE MasterShiire SET Shime1=@0 WHERE Code IN ('001','002')", ShimeChanged);
    try {
        var uriAfter = SummaryRebuildClosingCheck.FindMismatches("売掛", db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.UriClosingCheckSql, Month, Month));
        var kaiAfter = SummaryRebuildClosingCheck.FindMismatches("買掛", db.Fetch<SummaryClosingCheckRow>(SummaryRebuildClosingCheck.KaiClosingCheckSql, Month, Month));
        var canStart = SummaryRebuildClosingCheck.CanStartRequestDispatch([.. uriAfter, .. kaiAfter]);
        Console.WriteLine($"変更後 不一致: 売掛={uriAfter.Count} 買掛={kaiAfter.Count}  送信可={canStart}（false=ブロック）");
        Console.WriteLine(SummaryRebuildClosingCheck.BuildMismatchWarning([.. uriAfter, .. kaiAfter]));
        var ok = uriBefore.Count == 0 && kaiBefore.Count == 0 && uriAfter.Count == 2 && kaiAfter.Count == 2 && !canStart;
        Console.WriteLine($"締日変更警告: {(ok ? "PASS" : "FAIL")}");
        return ok;
    }
    finally {
        // 締日を必ず復元
        db.Execute("UPDATE MasterTokui  SET Shime1=@0 WHERE Code IN ('000002','000014')", ShimeMatched);
        db.Execute("UPDATE MasterShiire SET Shime1=@0 WHERE Code IN ('001','002')", ShimeMatched);
        Console.WriteLine("（締日を99へ復元）");
    }
}

Console.WriteLine($"db={dbPath}\ntokui1={tokui1} tokui2={tokui2} shiire1={shiire1} shiire2={shiire2}  command={command}");
switch (command) {
    case "seed": Clean(); Seed(); Calc(); Show(); break;
    case "show": Show(); break;
    case "idempotent": Idempotent(); break;
    case "closingcheck": ClosingCheck(); break;
    case "all":
        Clean(); Seed(); Calc(); Show();
        var i = Idempotent();
        var cc = ClosingCheck();
        Console.WriteLine($"\n=== ALL: idempotent={(i ? "PASS" : "FAIL")} closingcheck={(cc ? "PASS" : "FAIL")} ===");
        break;
    default: Console.WriteLine($"unknown command: {command}"); break;
}

db.Close();
Console.WriteLine("(done)");
