using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient;

namespace UatVm.Scenarios;

/// <summary>
/// マスター系CRUDの確認（DB切替後の MariaDB / PostgreSQL を想定、SQLiteでも動く）。
/// <para>
/// 画面（BaseMenteViewModel）と同じ gRPC 経路（Msg201_Op_Execute の InsertParam / UpdateParam / DeleteParam、
/// Msg101_Op_Query）で、主要マスタの 登録→読戻し→更新→読戻し→削除→消えたことの確認 を行う。
/// 併せて、DBごとにSQLが変わる副作用を確認する。
/// <list type="bullet">
/// <item>商品の色サイズ(Jcolsiz)が派生表 DerivedShohinColSiz へ展開・削除される</item>
/// <item>社員名の変更が得意先の VShain（JSONの名称スナップショット）へ伝播する（MasterCascadeDb）</item>
/// </list>
/// 試験データのコードは "Z" + 月日時分秒 とし、終了時に必ず削除する。サーバは --manage-server で起動し、
/// DB種別は環境変数 Database__Provider（と ConnectionStrings）で切り替える。
/// </para>
/// </summary>
public static class MasterCrudScenario {
	/// <summary>
	/// JSON往復で壊れやすい文字を含む名称。" と \ と ' 、改行・タブ、サロゲートペア(絵文字)、
	/// JSONエスケープと紛らわしい文字列 あ（6文字のまま残るべき）を含める。
	/// </summary>
	const string Tricky = "U\"A\\T'\n\t😀\\u3042";

	public static async Task RunAsync(VmSession session) {
		var code = "Z" + DateTime.Now.ToString("MMddHHmmss");
		session.Note("条件", new { code, provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "(appsettings)" });
		// 後始末用（作成と逆順に削除する）
		var created = new List<Func<Task>>();
		long shohinId = 0;
		try {
			// 1. 名称マスタ（ブランド・カラー・サイズ）
			var brand = await Crud(session, created, "名称(ブランド)",
				new MasterMeisho { Kubun = MasterMeisho.KubunBrand, KubunName = "ブランド", Code = code, Name = "UATブランド" },
				"select * from MasterMeisho where Kubun = @0 and Code = @1", [MasterMeisho.KubunBrand, code],
				m => m.Name = "UATブランド更新", m => m.Name == "UATブランド更新");
			var color = await Crud(session, created, "名称(カラー)",
				new MasterMeisho { Kubun = MasterMeisho.KubunColor, KubunName = "カラー", Code = code, Name = Tricky },
				"select * from MasterMeisho where Kubun = @0 and Code = @1", [MasterMeisho.KubunColor, code],
				m => m.Ryaku = "赤", m => m.Ryaku == "赤");
			var size = await Crud(session, created, "名称(サイズ)",
				new MasterMeisho { Kubun = MasterMeisho.KubunSize, KubunName = "サイズ", Code = code, Name = "UAT-M" },
				"select * from MasterMeisho where Kubun = @0 and Code = @1", [MasterMeisho.KubunSize, code],
				m => m.Name = "UAT-L", m => m.Name == "UAT-L");

			// 2. 社員
			var shain = await Crud(session, created, "社員",
				new MasterShain { Code = code, Name = "UAT社員" },
				"select * from MasterShain where Code = @0", [code],
				m => m.Kana = "ユーエーティー", m => m.Kana == "ユーエーティー");

			// 3. 得意先（担当社員の名称スナップショットを持つ）
			MasterTokui? tokui = null;
			if (shain != null) {
				tokui = await Crud(session, created, "得意先",
					new MasterTokui { Code = code, Name = "UAT得意先", TenType = 1, Id_Shain = shain.Id, VShain = new CodeNameView { Sid = shain.Id, Cd = shain.Code, Mei = Tricky } },
					"select * from MasterTokui where Code = @0", [code],
					m => { m.Address3 = new string('番', 90); m.Address2 = "UAT更新 \\ と ' と \" を含む"; },
					m => m.Address3.Length == 90 && m.Address2 == "UAT更新 \\ と ' と \" を含む");
				if (tokui != null) {
					// JSON列: 型で読み戻した値と、SQLの json_extract で取り出した値の両方が書いた値と一致する
					session.CheckEqual("JSON: 得意先VShain.Mei 型で読戻し", Tricky, tokui.VShain?.Mei);
					await CheckJsonExtract(session, "JSON: 得意先VShain.Mei json_extract",
						"select json_extract(VShain,'$.Mei') as Name from MasterTokui where Code = @0", code, Tricky);
					await CheckJsonExtract(session, "JSON: 得意先VShain.Sid json_extract",
						"select json_extract(VShain,'$.Sid') as Name from MasterTokui where Code = @0", code, shain.Id.ToString());
				}
			}

			// 4. 仕入先
			await Crud(session, created, "仕入先",
				new MasterShiire { Code = code, Name = "UAT仕入先" },
				"select * from MasterShiire where Code = @0", [code],
				m => m.Ryaku = "UAT仕", m => m.Ryaku == "UAT仕");

			// 5. 顧客（コードは移行データに合わせて広げた13桁以上も入ることを確認）
			var customerCode = code + "123";
			await Crud(session, created, "顧客",
				new MasterEndCustomer { Code = customerCode, Name = "UAT顧客" },
				"select * from MasterEndCustomer where Code = @0", [customerCode],
				m => m.Memo = new string('メ', 180), m => m.Memo.Length == 180);

			// 6. 資材
			await Crud(session, created, "資材",
				new MasterMaterial { Code = code, Name = "UAT資材" },
				"select * from MasterMaterial where Code = @0", [code],
				m => m.Name = "UAT資材更新", m => m.Name == "UAT資材更新");

			// 7. 商品（色サイズ2行 → 派生表の展開）
			if (brand != null && color != null && size != null) {
				var shohin = await Crud(session, created, "商品",
					new MasterShohin {
						Code = code, Name = "UAT商品", Id_Brand = brand.Id, VBrand = new CodeNameView { Sid = brand.Id, Cd = brand.Code, Mei = brand.Name },
						TankaGenka = 1000, TankaJodai = 2000, TankaJodaiOrg = 2000,
						Jcolsiz = [
							new MasterShohinColSiz { Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name, Id_Siz = size.Id, Code_Siz = size.Code, Mei_Siz = size.Name },
							new MasterShohinColSiz { Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name, Id_Siz = 0, Code_Siz = "", Mei_Siz = "" },
						],
					},
					"select * from MasterShohin where Code = @0", [code],
					m => m.MakerHin = "MAKER-HIN-0123456789-XY", m => m.MakerHin == "MAKER-HIN-0123456789-XY");
				if (shohin != null) {
					shohinId = shohin.Id;
					var derived = await session.QueryAsync<DerivedShohinColSiz>("select * from DerivedShohinColSiz where Id_Shohin = " + shohin.Id);
					session.Check("商品: 派生表(色サイズ)へ展開", derived.Count >= 1, new { count = derived.Count, jcolsiz = shohin.Jcolsiz?.Count });
					// JSON配列: 2行・全項目が書いた値どおり（順序も保持）
					var lines = shohin.Jcolsiz ?? [];
					var expected = new[] { (color.Id, color.Code, Tricky, size.Id, size.Code, size.Name), (color.Id, color.Code, Tricky, 0L, "", "") };
					var actual = lines.Select(x => (x.Id_Col, x.Code_Col, x.Mei_Col, x.Id_Siz, x.Code_Siz, x.Mei_Siz)).ToArray();
					session.Check("JSON: 商品Jcolsiz 配列の全項目", actual.SequenceEqual(expected),
						new { expected = expected.Select(e => e.ToString()), actual = actual.Select(a => a.ToString()) });
					// 画面(商品マスタ一覧)と同じ json_each 展開で配列の各要素を読む
					try {
						var each = await session.QueryAsync<MasterMeisho>(
							"select json_extract(J.value,'$.Mei_Col') as Name, json_extract(J.value,'$.Code_Siz') as Code from MasterShohin M, json_each(M.Jcolsiz) J where M.Code = @0 order by J.key", code);
						session.Check("JSON: 商品Jcolsiz json_each展開(件数・順序・特殊文字)",
							each.Count == 2 && each.All(e => e.Name == Tricky) && each[0].Code == size.Code && each[1].Code == "",
							new { count = each.Count, rows = each.Select(e => new { e.Name, e.Code }).ToList() });
					}
					catch (Exception ex) {
						session.Fail("JSON: 商品Jcolsiz json_each展開(件数・順序・特殊文字)", ex.Message);
					}
				}
			}

			// 8. 社員名変更 → 得意先の VShain へ伝播（MasterCascadeDb）
			if (shain != null && tokui != null) {
				var latest = (await session.QueryAsync<MasterShain>("select * from MasterShain where Code = @0", code)).Single();
				// 伝播はサーバのJSON更新SQL(MasterCascadeDb)で書かれるため、特殊文字の名称で確認する
				var renamed = "改名" + Tricky;
				latest.Name = renamed;
				await session.UpdateAsync(latest);
				var t = (await session.QueryAsync<MasterTokui>("select * from MasterTokui where Code = @0", code)).Single();
				session.Check("伝播: 社員改名→得意先VShain(特殊文字)", t.VShain?.Mei == renamed && t.VShain?.Sid == shain.Id && t.VShain?.Cd == shain.Code,
					new { t.VShain?.Sid, t.VShain?.Cd, t.VShain?.Mei, expected = renamed });
				await CheckJsonExtract(session, "伝播: json_extract で読戻し",
					"select json_extract(VShain,'$.Mei') as Name from MasterTokui where Code = @0", code, renamed);
			}

			// 9. 一覧系SQL（画面の検索と同じく方言変換を通る LIKE / ifnull / json_extract）
			var like = await session.QueryAsync<MasterTokui>(
				"select * from MasterTokui where Code like @0 and ifnull(Name,'') <> '' and json_extract(VShain,'$.Cd') = @1", code + "%", code);
			session.CheckEqual("一覧SQL: LIKE・ifnull・json_extract", 1, like.Count);
		}
		catch (Exception ex) {
			session.Fail("例外", ex.ToString());
		}
		finally {
			// 作成と逆順に削除し、消えたことを確認する
			for (var i = created.Count - 1; i >= 0; i--) {
				try { await created[i](); }
				catch (Exception ex) { session.Fail("後始末", ex.Message); }
			}
			if (await Exists(session, code) is { Count: > 0 } left) {
				session.Fail("後始末: 試験データが残っている", string.Join(", ", left));
			}
			if (shohinId > 0) {
				var derivedLeft = await session.QueryAsync<DerivedShohinColSiz>("select * from DerivedShohinColSiz where Id_Shohin = " + shohinId);
				session.CheckEqual("商品削除: 派生表(色サイズ)も削除", 0, derivedLeft.Count);
			}
		}
	}

	/// <summary>
	/// 1件の 登録→読戻し→更新→読戻し を行い、削除処理を後始末リストへ積む。失敗したら null を返す。
	/// </summary>
	static async Task<T?> Crud<T>(VmSession session, List<Func<Task>> created, string label, T item,
		string selectSql, string[] keys, Action<T> modify, Func<T, bool> isModified) where T : BaseDbClass {
		try {
			var inserted = await session.InsertAsync(item);
			session.Check($"{label}: 登録", inserted.Id > 0, new { inserted.Id });
			created.Add(async () => {
				var rows = await session.QueryAsync<T>(selectSql, keys);
				foreach (var row in rows) await DeleteAsync(row);
				var after = await session.QueryAsync<T>(selectSql, keys);
				session.CheckEqual($"{label}: 削除", 0, after.Count);
			});

			var read = await session.QueryAsync<T>(selectSql, keys);
			session.Check($"{label}: 読戻し", read.Count == 1 && read[0].Id == inserted.Id, new { count = read.Count });
			var row = read.Single();
			modify(row);
			await session.UpdateAsync(row);
			var reread = (await session.QueryAsync<T>(selectSql, keys)).Single();
			session.Check($"{label}: 更新", isModified(reread) && reread.Vdu != inserted.Vdu, new { reread.Id, reread.Vdu });
			return reread;
		}
		catch (Exception ex) {
			session.Fail($"{label}: CRUD", ex.Message);
			return null;
		}
	}

	/// <summary>
	/// SQLの JSON 関数で取り出した値を確認する。結果は MasterMeisho.Name 列へ別名で受ける
	/// （QueryAsync はサーバでも解決できる共有型が必要なため）。
	/// </summary>
	static async Task CheckJsonExtract(VmSession session, string name, string sql, string code, string expected) {
		try {
			var rows = await session.QueryAsync<MasterMeisho>(sql, code);
			session.Check(name, rows.Count == 1 && rows[0].Name == expected, new { expected, actual = rows.Select(r => r.Name).ToList() });
		}
		catch (Exception ex) {
			session.Fail(name, ex.Message);
		}
	}

	/// <summary>画面(BaseMenteViewModel)と同じ DeleteParam で削除する。</summary>
	static async Task DeleteAsync<T>(T item) where T : BaseDbClass {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(DeleteParam),
			DataMsg = Common.SerializeObject(new DeleteParam(typeof(T), Common.SerializeObject(item))),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext());
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバDeleteでエラーが発生しました");
		}
	}

	/// <summary>試験コードの行が残っていないか（表名を返す）</summary>
	static async Task<List<string>> Exists(VmSession session, string code) {
		var left = new List<string>();
		if ((await session.QueryAsync<MasterMeisho>("select * from MasterMeisho where Code = @0", code)).Count > 0) left.Add("MasterMeisho");
		if ((await session.QueryAsync<MasterShain>("select * from MasterShain where Code = @0", code)).Count > 0) left.Add("MasterShain");
		if ((await session.QueryAsync<MasterTokui>("select * from MasterTokui where Code = @0", code)).Count > 0) left.Add("MasterTokui");
		if ((await session.QueryAsync<MasterShiire>("select * from MasterShiire where Code = @0", code)).Count > 0) left.Add("MasterShiire");
		if ((await session.QueryAsync<MasterEndCustomer>("select * from MasterEndCustomer where Code like @0", code + "%")).Count > 0) left.Add("MasterEndCustomer");
		if ((await session.QueryAsync<MasterMaterial>("select * from MasterMaterial where Code = @0", code)).Count > 0) left.Add("MasterMaterial");
		if ((await session.QueryAsync<MasterShohin>("select * from MasterShohin where Code = @0", code)).Count > 0) left.Add("MasterShohin");
		return left;
	}
}
