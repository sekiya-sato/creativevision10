using jp.axissoft.printstream;


namespace Cvnet10Prints;

/// <summary>
/// PrintStreamのAPIを.NETから呼び出すためのアダプタークラス
/// </summary>
public class PrintAdapter : IPrintService {
	/// <summary>
	/// PrintStreamの実行(PDF)
	/// </summary>
	/// <param name="context"></param>
	/// <returns></returns>
	public async Task<PrintResult> ExecutePrintAsync(PrintContext context) {
		// レガシーなJava同期処理を別スレッドで実行
		return await Task.Run(() => {
			// メソッド内でインスタンス化し、スレッドセーフを確保
			try {
				var writer = new FormWriter();
				if (!string.IsNullOrEmpty(context.BasePath))
					writer.setPath(new java.io.File(context.BasePath));
				writer.setDevice(FormWriter.PDF); // PDF固定
				writer.setForm(new java.io.File(context.FormPath));
				writer.setData(context.DataPath);
				if (!string.IsNullOrEmpty(context.OutputDir))
					writer.setSpool(new java.io.File(Path.GetFullPath(context.OutputDir)));
				writer.setFile(context.OutputFileName);

				// Java側の同期メソッドを実行
				writer.submit();

				return new PrintResult(true, context.OutputFileName);
			}
			catch (FormWriterException e) {
				// PrintStream固有のエラーハンドリング
				return new PrintResult(false, e.getMessage());
			}
			catch (Exception ex) {
				// .NETレイヤーのエラー
				return new PrintResult(false, ex.Message);
			}
		});
	}
	/// <summary>
	/// ライセンスの登録
	/// </summary>
	/// <param name="product"></param>
	/// <param name="serial"></param>
	/// <param name="key"></param>
	/// <returns></returns>
	public async Task<bool> RegisterLicenseAsync(string product, string serial, string key) {
		return await Task.Run(() => {
			try {
				var writer = new FormWriter();
				return writer.registerLicense(product, serial, key);
			}
			catch (FormWriterException) {
				return false;
			}
			catch (Exception) {
				return false;
			}
		});
	}
	/// <summary>
	/// ライセンス状態の確認
	/// </summary>
	/// <returns></returns>
	public async Task<List<PrintProduct>> CheckLicenseAsync() {
		return await Task.Run(() => {
			List<PrintProduct> ret = [];
			try {
				var writer = new FormWriter();
				foreach (var item in writer.getProducts()) {
					ret.Add(new PrintProduct {
						Product = item,
						Status = writer.checkLicense(item)
					});
				}
				return ret;
			}
			catch (FormWriterException) {
				return ret;
			}
			catch (Exception) {
				return ret;
			}
		});
	}

}
