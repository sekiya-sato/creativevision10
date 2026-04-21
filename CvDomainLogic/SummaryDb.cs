using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

public class SummaryDb {
	ExDatabase _db;
	ILogger<SummaryDb> _logger;
	public SummaryDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<SummaryDb>();
	}
	public async IAsyncEnumerable<StreamStepProgress> SummaryAllAsyncStream(SummaryParameter param) {
		_logger.LogInformation("処理開始");
		var start = DateTime.Now;

		// ToDo: 最終的に実行させる処理を整理
		var steps = new (string Name, Func<SummaryParameter, int> Action)[] {
			/*
			*/
			("SummaryZaiko", SummaryZaiko),
		};

		for (var index = 0; index < steps.Length; index++) {
			var (name, action) = steps[index];
			var startProgress = index * 100 / steps.Length;

			// ステップ開始通知
			yield return new StreamStepProgress(name, 0, startProgress, false, false);

			// 処理実行
			int count = 0;
			string? errorMsg = null;
			bool isError = false;
			try {
				count = action(param);
			}
			catch (Exception ex) {
				_logger.LogError(ex, $"処理エラー: {name}");
				isError = true;
				errorMsg = ex.Message;
			}

			var endProgress = (int)Math.Round((index + 1) * 100d / steps.Length, MidpointRounding.AwayFromZero);

			// ステップ完了通知
			yield return new StreamStepProgress(name, count, endProgress, false, isError, errorMsg);
		}

		var elapsed = DateTime.Now - start;
		_logger.LogInformation($"処理終了 {elapsed.TotalSeconds:0.0}s");

		yield return new StreamStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s");
	}

	private int SummaryZaiko(SummaryParameter param) {
		var list = _db.Fetch<Tran00Uriage>(
			"where DenDay between @0 and @1",
			param.DateYymmFrom,
			param.DateYymmTo);


		var sql = @"
select k.*
,s.SizeKu
,m.Name
 from (
SELECT
h.Id,
h.DenDay,
h.Id_Soko,
h.CalcFlag,
h.SuTotal,
json_extract(j.value, '$.Id_Shohin') Id_Shohin,
json_extract(j.value, '$.Id_Col')  Id_Col,
json_extract(j.value, '$.Id_Siz')  Id_Siz,
json_extract(j.value, '$.Code_Siz')  Code_Siz,
json_extract(j.value, '$.Mei_Siz')  mei_Siz,
json_extract(j.value, '$.Su')  Su
FROM Tran00Uriage h,
json_each(h.Jmeisai) j
WHERE h.DenDay BETWEEN '20190519' AND '20190521' and json_type (h.Jmeisai) = 'array' and h.SuTotal> 1 ) k
inner join MasterShohin s on (s.Id = k.Id_Shohin)
left outer join MasterMeisho m on (m.Kubun=s.SizeKu and m.Code=k.Code_Siz)
order by k.DenDay, k.Id_Soko, k.Id_Shohin, k.Id_Col, k.Id_Siz
";

		var list2 = _db.Fetch<dynamic>(sql);

		// _db.CreateTable<SummaryStock>();

		if (list.Count == 0)
			return 0;

		static (string SumMonth, long IdSoko, long IdShohin, long IdCol, long IdSiz) GetKey(SummaryStock x)
			=> (x.SumMonth, x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz);

		var sumMap = new Dictionary<(string SumMonth, long IdSoko, long IdShohin, long IdCol, long IdSiz), SummaryStock>();

		foreach (var item in list) {
			if (item.CalcFlag == 0)
				continue;
			if (string.IsNullOrWhiteSpace(item.DenDay) || item.DenDay.Length < 6)
				continue;
			if (item.Jmeisai == null || item.Jmeisai.Count == 0)
				continue;

			var sumMonth = item.DenDay[..6];

			foreach (var detail in item.Jmeisai) {
				var deltaSu = detail.Su * item.CalcFlag;
				if (deltaSu == 0)
					continue;

				var key = (sumMonth, item.Id_Soko, detail.Id_Shohin, detail.Id_Col, detail.Id_Siz);

				if (!sumMap.TryGetValue(key, out var summary)) {
					summary = new SummaryStock {
						SumMonth = sumMonth,
						Id_Soko = item.Id_Soko,
						Id_Shohin = detail.Id_Shohin,
						Id_Col = detail.Id_Col,
						Id_Siz = detail.Id_Siz,
					};
					sumMap.Add(key, summary);
				}

				summary.Su += deltaSu;
			}
		}

		var sumList = sumMap.Values
			.Where(x => x.Su != 0)
			.ToList();

		if (sumList.Count == 0)
			return 0;

		var minMonth = sumList.Min(x => x.SumMonth);
		var maxMonth = sumList.Max(x => x.SumMonth);

		var existingList = _db.Fetch<SummaryStock>(
			"where SumMonth between @0 and @1",
			minMonth ?? "",
			maxMonth ?? "");

		var existingMap = new Dictionary<(string SumMonth, long IdSoko, long IdShohin, long IdCol, long IdSiz), SummaryStock>();
		var duplicateDeleteList = new List<SummaryStock>();

		foreach (var existing in existingList.OrderBy(x => x.Id)) {
			var key = GetKey(existing);

			if (existingMap.TryGetValue(key, out var merged)) {
				merged.Su += existing.Su;
				duplicateDeleteList.Add(existing);
				continue;
			}

			existingMap.Add(key, existing);
		}

		var nowTicks = DateTime.UtcNow.Ticks;
		var insertList = new List<SummaryStock>();
		var updateList = new List<SummaryStock>();
		var deleteList = new List<SummaryStock>(duplicateDeleteList);

		foreach (var summary in sumList) {
			var key = GetKey(summary);

			if (existingMap.TryGetValue(key, out var existing)) {
				existing.Su += summary.Su;
				existing.Vdu = nowTicks;

				if (existing.Su == 0)
					deleteList.Add(existing);
				else
					updateList.Add(existing);

				continue;
			}

			summary.Vdc = nowTicks;
			summary.Vdu = nowTicks;
			insertList.Add(summary);
		}

		using var transaction = _db.GetTransaction();

		if (insertList.Count > 0)
			_db.InsertBulk<SummaryStock>(insertList);

		foreach (var item in updateList)
			_db.Update(item);

		foreach (var item in deleteList)
			_db.Delete(item);

		transaction.Complete();

		return insertList.Count + updateList.Count + deleteList.Count;
	}
}

public record SummaryParameter(string DateYymmFrom, string DateYymmTo);
