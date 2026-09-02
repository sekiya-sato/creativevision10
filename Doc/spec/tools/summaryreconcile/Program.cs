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
//   closingcheck  締日を変更→締日変更検査SQLで不一致検出→送信ブロック→締日を復元（Rebuild時の締日変更ブロック）
//   paysakicheck  親子（請求先/支払先↔得意先/仕入先）の締日不一致データを投入し、実DBで警告が発火するか（E7）。検査後に復元
//   all           seed → show → idempotent → closingcheck → paysakicheck を順に実行
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
await UpdateDb.WriteVersionInfoAsync(db); // dbPathのスキーマをUpdateDb.versionsの最新まで追随させる（例: E11 SummaryUriSei.Sonota追加）
var summaryDb = new SummaryDb(db);

long tokui1 = db.Single<MasterTokui>("where Code=@0", "000002").Id;
long tokui2 = db.Single<MasterTokui>("where Code=@0", "000014").Id;
long shiire1 = db.Single<MasterShiire>("where Code=@0", "001").Id;
long shiire2 = db.Single<MasterShiire>("where Code=@0", "002").Id;
long shiire3 = db.Single<MasterShiire>("where Code=@0", "005").Id; // 支払超過（過払い）
long shiire4 = db.Single<MasterShiire>("where Code=@0", "006").Id; // 相殺のみ（現金支払なし）
long shiire5 = db.Single<MasterShiire>("where Code=@0", "007").Id; // 複数明細支払（現金+相殺+手数料）

static string DL(string c) => $"case when length({c})=8 then substr({c},1,4)||'/'||substr({c},5,2)||'/'||substr({c},7,2) else ifnull({c},'') end";

void Clean() {
    db.Execute("DELETE FROM Tran00Uriage  WHERE Id_Tokui IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", tokui1, tokui2, DFrom, DTo);
    db.Execute("DELETE FROM Tran06Nyukin  WHERE Id_Torisaki IN (@0,@1) AND KakeDay BETWEEN @2 AND @3", tokui1, tokui2, DFrom, DTo);
    db.Execute("DELETE FROM Tran03Shiire   WHERE Id_Shiire IN (@0,@1,@2,@3,@4) AND KakeDay BETWEEN @5 AND @6", shiire1, shiire2, shiire3, shiire4, shiire5, DFrom, DTo);
    db.Execute("DELETE FROM Tran07Shiharai WHERE Id_Torisaki IN (@0,@1,@2,@3,@4) AND KakeDay BETWEEN @5 AND @6", shiire1, shiire2, shiire3, shiire4, shiire5, DFrom, DTo);
    db.Execute("DELETE FROM SummaryUriKake WHERE DenMonth=@0", Month);
    db.Execute("DELETE FROM SummaryKaiKake WHERE DenMonth=@0", Month);
    db.Execute("DELETE FROM SummaryUriSei  WHERE DenDay=@0", DTo);
    db.Execute("DELETE FROM SummaryKaiShi  WHERE DenDay=@0", DTo);
}

// テスト取引先の実マスタは全件が請求単位(TaxCalcUnit=Billing、実データの前提。仕様1.1)であり、
// 伝票のTax1/2/3は0のまま・TaxableAmount1(課税対象額、税抜)だけ確定させる。消費税は
// CalcSummaryUriKake/UriSei/KaiKake/KaiShiが締請求期間でTaxableAmount1×税率を1回丸めて計算する(仕様3.4)。
// 引数のtaxは元々ヘッダへ直接入れていた税額(10%固定)で、いまはSummary側の計算結果と突き合わせる
// 目安値としてShow()の表示だけに使う。
Tran00Uriage Uri(string day, long id, EnumUri00 k, int total, int tax) { var t = new Tran00Uriage { DenDay = day, KakeDay = day, Id_Tokui = id, Total = total, KingakuTotal = total, TaxableAmount1 = total, IsPay = 1 }; t.EnKubun = k; return t; }
Tran03Shiire Shi(string day, long id, EnumShiire k, int total, int tax) { var t = new Tran03Shiire { DenDay = day, KakeDay = day, Id_Shiire = id, Total = total, KingakuTotal = total, TaxableAmount1 = total, IsPay = 1 }; t.EnKubun = k; return t; }
List<TranKinMeisai> Kin((long id, int kin)[] m) => [.. m.Select((x, i) => new TranKinMeisai { No = i + 1, Id_Kin = x.id, Kingaku = x.kin })];
Tran06Nyukin Nyu(string day, long id, (long, int)[] m) => new() { KakeDay = day, Id_Torisaki = id, KingakuTotal = m.Sum(x => x.Item2), Jmeisai = Kin(m) };
Tran07Shiharai Sih(string day, long id, (long, int)[] m) => new() { KakeDay = day, Id_Torisaki = id, KingakuTotal = m.Sum(x => x.Item2), Jmeisai = Kin(m) };

void Seed() {
    db.Insert(Uri("20260705", tokui1, EnumUri00.Uriage, 100000, 10000));
    db.Insert(Uri("20260712", tokui1, EnumUri00.Henpin, 20000, 2000));
    db.Insert(Uri("20260718", tokui1, EnumUri00.Nebiki, 5000, 500));
    db.Insert(Uri("20260720", tokui1, EnumUri00.Other, 8000, 800)); // E11: 区分99=その他売上、請求一覧の算式でのみ分離集計
    db.Insert(Nyu("20260725", tokui1, [(KinCash, 50000), (KinFee, 440)]));
    db.Insert(Uri("20260710", tokui2, EnumUri00.Uriage, 30000, 3000));
    db.Insert(Nyu("20260728", tokui2, [(KinCash, 33000)]));
    db.Insert(Shi("20260706", shiire1, EnumShiire.Shiire, 80000, 8000));
    db.Insert(Shi("20260714", shiire1, EnumShiire.Henpin, 10000, 1000));
    db.Insert(Sih("20260726", shiire1, [(KinCash, 50000), (KinOffset, 20000)]));
    db.Insert(Shi("20260709", shiire2, EnumShiire.Shiire, 15000, 1500));
    db.Insert(Shi("20260707", shiire3, EnumShiire.Shiire, 50000, 5000));
    db.Insert(Sih("20260727", shiire3, [(KinCash, 60000)])); // 支払超過（過払い）：仕入額55,000に対し支払60,000、残高+5,000
    db.Insert(Shi("20260708", shiire4, EnumShiire.Shiire, 30000, 3000));
    db.Insert(Sih("20260727", shiire4, [(KinOffset, 33000)])); // 相殺のみ：現金支払なしで全額相殺、残高0
    db.Insert(Shi("20260711", shiire5, EnumShiire.Shiire, 40000, 4000));
    db.Insert(Sih("20260727", shiire5, [(KinCash, 20000), (KinOffset, 20000), (KinFee, 4000)])); // 複数明細支払：現金+相殺+手数料の3明細、残高0
}

void Calc() {
    var a = summaryDb.CalcSummaryUriKake(Month, Month);
    var b = summaryDb.CalcSummaryUriSei(Month, ShimeMatched, "000002", "000014");
    var c = summaryDb.CalcSummaryKaiKake(Month, Month);
    var d = summaryDb.CalcSummaryKaiShi(Month, ShimeMatched, "001", "007");
    Console.WriteLine($"Calc rows: UriKake={a} UriSei={b} KaiKake={c} KaiShi={d}");
}

void Show() {
    Console.WriteLine("\n===== 売掛集計(SummaryUriKake、区分99は売上へ畳み込み) =====");
    foreach (var r in db.Fetch<dynamic>(@"
SELECT t.Code AS CD, t.Name AS 名, k.DenMonth AS 年月,
       k.Uriage, k.Henpin, k.Nebiki, k.Tax1+k.Tax2+k.Tax3 AS Tax, k.TotalSales AS 売上額, k.TotalIn AS 入金額, k.Balance AS 当期間残高
FROM SummaryUriKake k JOIN MasterTokui t ON t.Id=k.Id_Tokui WHERE k.DenMonth=@0 ORDER BY t.Code", Month))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));

    Console.WriteLine("\n===== 買掛集計(SummaryKaiKake、区分99は仕入へ畳み込み) =====");
    foreach (var r in db.Fetch<dynamic>(@"
SELECT s.Code AS CD, s.Name AS 名, k.DenMonth AS 年月,
       k.Shiire, k.Henpin, k.Nebiki, k.Tax1+k.Tax2+k.Tax3 AS Tax, k.TotalShiire AS 仕入額, k.TotalOut AS 支払額, k.Balance AS 当期間残高
FROM SummaryKaiKake k JOIN MasterShiire s ON s.Id=k.Id_Shiire WHERE k.DenMonth=@0 ORDER BY s.Code", Month))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));

    Console.WriteLine("\n===== 請求台帳（発行控え）=====");
    foreach (var r in db.Fetch<dynamic>($@"
SELECT u.SeikyuNo AS 番号, {DL("u.DenDay")} AS 請求日, t.Code AS CD, t.Name AS 名,
       u.Uriage, u.Henpin, u.Nebiki, u.Sonota AS その他売上, u.Tax1+u.Tax2+u.Tax3 AS Tax, u.TotalSales AS 売上額, u.TotalIn AS 入金額, u.Balance AS 当期間残高,
       {DL("u.NyukinYoteiDay")} AS 入金予定日, u.Renban AS 再
FROM SummaryUriSei u JOIN MasterTokui t ON t.Id=u.Id_Tokui WHERE u.DenDay=@0 ORDER BY t.Code", DTo))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));

    Console.WriteLine("\n===== 支払台帳（発行控え）=====");
    foreach (var r in db.Fetch<dynamic>($@"
SELECT {DL("k.DenDay")} AS 支払日, s.Code AS CD, s.Name AS 名,
       k.Shiire, k.Henpin, k.Nebiki, k.Tax1+k.Tax2+k.Tax3 AS Tax, k.TotalShiire AS 仕入額, k.TotalOut AS 支払額, k.Balance AS 当期間残高,
       {DL("k.ShiharaiYoteiDay")} AS 支払予定日
FROM SummaryKaiShi k JOIN MasterShiire s ON s.Id=k.Id_Shiire WHERE k.DenDay=@0 ORDER BY s.Code", DTo))
        Console.WriteLine(string.Join(" | ", ((IDictionary<string, object>)r).Select(kv => $"{kv.Key}={kv.Value ?? "-"}")));
}

string[] Snapshot() => [
    .. db.Fetch<SummaryUriSei>("where DenDay=@0 order by Id_Tokui", DTo)
        .Select(x => $"URI {x.Id_Tokui}:{x.SeikyuNo}:R{x.Renban}:{x.TotalSales}:{x.Sonota}:{x.TotalIn}:{x.Balance}:{x.NyukinYoteiDay}"),
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

/// <summary>
/// E7 親子締日チェックのワーニング表示を、実DBデータで発火させて確認する。
/// 開発DBは `Id_Paysaki` が全件0のため、親子関係と締日不一致をここで投入し、検査後に必ず復元する。
///
/// 画面側と同じ経路を再現する:
///   - 請求計算・支払計算の実行前 = BaseBillingCalculationViewModel.GetPreExecuteWarningAsync
///     （BuildRangeCheckSql + 締日/コード範囲のWHERE。パラメータは QueryListSqlParam と同じ **文字列**）
///   - 得意先/仕入先マスターメンテの保存後 = WarnIfPaysakiClosingMismatchAsync
///     （BuildAffectedRowCheckSql。編集Idを軸に子として／親として双方向で検査）
/// </summary>
bool PaysakiCheck() {
    Console.WriteLine("\n----- paysakicheck (E7 親子締日ワーニング / 実データ発火) -----");
    const int ShimeParent = 20;   // 親だけ別締日にして不一致を作る

    // 親子: 得意先 000002(子,99) → 000016(親,20) = 不一致 / 000014(子,99) → 000023(親,99) = 一致
    //       仕入先 001(子,99)    → 003(親,20)    = 不一致 / 002(子,99)    → 004(親,99)    = 一致
    var tParentNg = db.Single<MasterTokui>("where Code=@0", "000016").Id;
    var tParentOk = db.Single<MasterTokui>("where Code=@0", "000023").Id;
    var sParentNg = db.Single<MasterShiire>("where Code=@0", "003").Id;
    var sParentOk = db.Single<MasterShiire>("where Code=@0", "004").Id;

    // 投入前: Id_Paysaki が全件0なら検出0件であること
    var beforeRows = db.Fetch<PaysakiClosingCheckRow>(
        PaysakiClosingCheck.BuildRangeCheckSql(nameof(MasterTokui), "WHERE c.Id_Paysaki <> 0 AND c.Shime1 = @0 AND p.Shime1 <> c.Shime1"),
        ShimeMatched.ToString());
    Console.WriteLine($"投入前 得意先 不一致: {PaysakiClosingCheck.FindMismatches(beforeRows).Count} 件（Id_Paysaki未設定のため0が正）");

    db.Execute("UPDATE MasterTokui  SET Id_Paysaki=@0 WHERE Code='000002'", tParentNg);
    db.Execute("UPDATE MasterTokui  SET Id_Paysaki=@0 WHERE Code='000014'", tParentOk);
    db.Execute("UPDATE MasterTokui  SET Shime1=@0    WHERE Code='000016'", ShimeParent);
    db.Execute("UPDATE MasterShiire SET Id_Paysaki=@0 WHERE Code='001'", sParentNg);
    db.Execute("UPDATE MasterShiire SET Id_Paysaki=@0 WHERE Code='002'", sParentOk);
    db.Execute("UPDATE MasterShiire SET Shime1=@0     WHERE Code='003'", ShimeParent);
    try {
        var ok = true;

        // (1) 計算画面の実行前警告。パラメータは画面と同じ文字列で渡す（QueryListSqlParam.Parameters は string[]）。
        List<PaysakiClosingCheckRow> Range(string table, string codeFrom, string codeTo) {
            List<string> ps = [ShimeMatched.ToString()];
            var where = "WHERE c.Id_Paysaki <> 0 AND c.Shime1 = @0 AND p.Shime1 <> c.Shime1";
            if (codeFrom.Length > 0) { where += $" AND c.Code >= @{ps.Count}"; ps.Add(codeFrom); }
            if (codeTo.Length > 0) { where += $" AND c.Code <= @{ps.Count}"; ps.Add(codeTo); }
            return db.Fetch<PaysakiClosingCheckRow>(PaysakiClosingCheck.BuildRangeCheckSql(table, where), [.. ps]);
        }

        var tRange = PaysakiClosingCheck.FindMismatches(Range(nameof(MasterTokui), "", ""));
        var sRange = PaysakiClosingCheck.FindMismatches(Range(nameof(MasterShiire), "", ""));
        Console.WriteLine($"請求計算 実行前警告: 不一致={tRange.Count} 件");
        Console.WriteLine(PaysakiClosingCheck.BuildMismatchWarning("請求先", "得意先", tRange));
        Console.WriteLine($"支払計算 実行前警告: 不一致={sRange.Count} 件");
        Console.WriteLine(PaysakiClosingCheck.BuildMismatchWarning("支払先", "仕入先", sRange));
        ok &= tRange.Count == 1 && tRange[0].ChildCode == "000002" && tRange[0].ParentDays.Contains(ShimeParent);
        ok &= sRange.Count == 1 && sRange[0].ChildCode == "001";

        // コード範囲で対象外に絞れば0件（範囲条件が効いていること）
        var tOutOfRange = PaysakiClosingCheck.FindMismatches(Range(nameof(MasterTokui), "000010", "000020"));
        Console.WriteLine($"コード範囲 000010〜000020 に絞った場合: 不一致={tOutOfRange.Count} 件（0が正）");
        ok &= tOutOfRange.Count == 0;

        // (2) マスターメンテ保存後の警告。子を編集した場合と親を編集した場合の双方向。
        List<PaysakiClosingMismatch> Affected(string table, long editedId) =>
            PaysakiClosingCheck.FindMismatches(db.Fetch<PaysakiClosingCheckRow>(PaysakiClosingCheck.BuildAffectedRowCheckSql(table, editedId)));

        var childId = db.Single<MasterTokui>("where Code=@0", "000002").Id;
        var okChildId = db.Single<MasterTokui>("where Code=@0", "000014").Id;
        var byChild = Affected(nameof(MasterTokui), childId);
        var byParent = Affected(nameof(MasterTokui), tParentNg);
        var byOkChild = Affected(nameof(MasterTokui), okChildId);
        Console.WriteLine($"得意先メンテ保存後: 子(000002)編集={byChild.Count} 親(000016)編集={byParent.Count} 一致ペアの子(000014)編集={byOkChild.Count}（1/1/0が正）");
        ok &= byChild.Count == 1 && byParent.Count == 1 && byOkChild.Count == 0;

        var sChildId = db.Single<MasterShiire>("where Code=@0", "001").Id;
        var sByChild = Affected(nameof(MasterShiire), sChildId);
        var sByParent = Affected(nameof(MasterShiire), sParentNg);
        Console.WriteLine($"仕入先メンテ保存後: 子(001)編集={sByChild.Count} 親(003)編集={sByParent.Count}（1/1が正）");
        ok &= sByChild.Count == 1 && sByParent.Count == 1;

        // 警告文に再計算案内が含まれること
        ok &= PaysakiClosingCheck.BuildMismatchWarning("請求先", "得意先", tRange).Contains(PaysakiClosingCheck.MismatchGuidance);

        Console.WriteLine($"親子締日ワーニング(E7): {(ok ? "PASS" : "FAIL")}");
        return ok;
    }
    finally {
        db.Execute("UPDATE MasterTokui  SET Id_Paysaki=0 WHERE Code IN ('000002','000014')");
        db.Execute("UPDATE MasterTokui  SET Shime1=@0    WHERE Code='000016'", ShimeMatched);
        db.Execute("UPDATE MasterShiire SET Id_Paysaki=0 WHERE Code IN ('001','002')");
        db.Execute("UPDATE MasterShiire SET Shime1=@0    WHERE Code='003'", ShimeMatched);
        Console.WriteLine("（Id_Paysaki=0・締日99へ復元）");
    }
}

Console.WriteLine($"db={dbPath}\ntokui1={tokui1} tokui2={tokui2} shiire1={shiire1} shiire2={shiire2}  command={command}");
switch (command) {
    case "seed": Clean(); Seed(); Calc(); Show(); break;
    case "show": Show(); break;
    case "idempotent": Idempotent(); break;
    case "closingcheck": ClosingCheck(); break;
    case "paysakicheck": PaysakiCheck(); break;
    case "all":
        Clean(); Seed(); Calc(); Show();
        var i = Idempotent();
        var cc = ClosingCheck();
        var pc = PaysakiCheck();
        Console.WriteLine($"\n=== ALL: idempotent={(i ? "PASS" : "FAIL")} closingcheck={(cc ? "PASS" : "FAIL")} paysakicheck={(pc ? "PASS" : "FAIL")} ===");
        break;
    default: Console.WriteLine($"unknown command: {command}"); break;
}

db.Close();
Console.WriteLine("(done)");
