using Newtonsoft.Json;
using System.Collections;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CvAsset;

public sealed partial class Common {
	// JsonConvert共通オプション（必要に応じてカスタマイズ／コンバータを追加）
	private static readonly JsonSerializerSettings jsonOptions = new() {
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		DefaultValueHandling = DefaultValueHandling.Ignore,
	};
	/// <summary>
	/// 共通オプションを使ってオブジェクトをシリアライズ
	/// </summary>
	public static string SerializeObject(object obj) {
		return JsonConvert.SerializeObject(obj, jsonOptions);
	}
	/// <summary>
	/// 共通オプションを使ってオブジェクトをデシリアライズ
	/// </summary>
	public static object? DeserializeObject(string obj, Type t) {
		return JsonConvert.DeserializeObject(obj, t, jsonOptions);
	}
	public static T? DeserializeObject<T>(string obj) {
		return JsonConvert.DeserializeObject<T>(obj, jsonOptions);
	}
	/// <summary>
	/// 内容が同じ別オブジェクトを返す
	/// </summary>
	public static T CloneObject<T>(T obj) where T : new() {
		var json = JsonConvert.SerializeObject(obj, jsonOptions);
		return JsonConvert.DeserializeObject<T>(json, jsonOptions) ?? new T();
	}

	private static object? CloneObjectInternal(object source) {
		ArgumentNullException.ThrowIfNull(source);
		var json = JsonConvert.SerializeObject(source, jsonOptions);
		return JsonConvert.DeserializeObject(json, source.GetType(), jsonOptions);
	}
	/// <summary>
	/// srcのプロパティ値をdstにコピーする DeepCopy
	/// [Deep copy property values from src to dst]
	/// </summary>
	/// <remarks>循環参照を含むオブジェクトでは無限ループの可能性があるため注意 [Caution: may cause infinite loop with circular references]</remarks>
	public static void DeepCopyValue(Type type, object? src, object? dst) {
		if (src == null || dst == null) return;
		// プロパティ情報を取得（公開インスタンスプロパティのみ）
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

		foreach (var property in properties) {
			if (!property.CanRead || !property.CanWrite) continue;

			var srcValue = property.GetValue(src);
			if (srcValue == null) {
				property.SetValue(dst, null);
				continue;
			}

			var propertyType = property.PropertyType;

			// 1. 値型、プリミティブ型、または文字列の場合（そのままコピー）
			if (propertyType.IsValueType || propertyType == typeof(string)) {
				property.SetValue(dst, srcValue);
			}
			// 2. コレクション（リスト、配列）の場合
			else if (typeof(IEnumerable).IsAssignableFrom(propertyType)) {
				// コレクション自体のディープコピー
				property.SetValue(dst, CloneObjectInternal(srcValue));
			}
			// 3. 参照型（クラス）の場合
			else {
				// 再帰的にディープコピー
				var dstValue = Activator.CreateInstance(propertyType);
				DeepCopyValue(propertyType, srcValue, dstValue);
				property.SetValue(dst, dstValue);
			}
		}
	}
	/// <summary>
	/// 日付を比較する（文字列 'yyyyMMdd' 形式）ymd1がymd2を超えていれば1,同じ日付は0,ymd1がymd2より古ければ-1,ただしymd2が初期値の場合は必ず-1
	/// </summary>
	/// <param name="ymd1">比較対象の日付</param>
	/// <param name="ymd2">初期値の可能性がある日付文字列</param>
	/// <param name="defaultYmd">初期値 19010101</param>
	/// <returns>ymd1がより新しい日付であれば1,同じ日付は0,古い日付は-1,ただしymd2が初期値の場合は必ず-1</returns>
	/// <exception cref="ArgumentException"></exception>
	public static int CompareYmd(string ymd1, string ymd2, string defaultYmd="19010101") {
		if (ymd1.Length != 8 || ymd2.Length != 8) {
			throw new ArgumentException("日付文字列は 'yyyyMMdd' 形式である必要があります。");
		}
		if (string.CompareOrdinal(ymd2, defaultYmd) <= 0)
			return -1;
		return string.CompareOrdinal(ymd1, ymd2); // 0=同一日付, 1=ymd1がymd2より新しい日付, -1=ymd1がymd2より古い日付
	}

	/// <summary>
	/// パスワードから共有キーと初期化ベクタを生成する
	/// [Generate a shared key and initialization vector from a password]
	/// </summary>
	/// <param name="password">基になるパスワード</param> [Base password]
	/// <param name="keySize">共有キーのサイズ（ビット）</param> [Size of the shared key (in bits)]
	/// <param name="key">作成された共有キー</param> [Generated shared key]
	/// <param name="blockSize">初期化ベクタのサイズ（ビット）</param> [Size of the initialization vector (in bits)]
	/// <param name="iv">作成された初期化ベクタ</param> [Generated initialization vector]
	static void GenerateKeyFromPassword(string password,
		int keySize, out byte[] key, int blockSize, out byte[] iv) {
		//パスワードから共有キーと初期化ベクタを作成する
		//[Create shared key and initialization vector from the password]
		//saltを決める 8byte以上
		//[Determine the salt (at least 8 bytes)]
		const string SaltValue = "salt-20240801";
		byte[] salt = Encoding.UTF8.GetBytes(SaltValue);
		var keyBytes = keySize / 8;
		var ivBytes = blockSize / 8;
		var derivedLength = keyBytes + ivBytes;
		var iterationCount = 600_000; // イテレーション回数を現代標準(600,000以上)へ
		var derivedBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterationCount, HashAlgorithmName.SHA256, derivedLength);

		//共有キーと初期化ベクタを生成する
		//[Generate the shared key and initialization vector]
		key = new byte[keyBytes];
		iv = new byte[ivBytes];
		Buffer.BlockCopy(derivedBytes, 0, key, 0, keyBytes);
		Buffer.BlockCopy(derivedBytes, keyBytes, iv, 0, ivBytes);
	}

	/// <summary>
	/// パスワードから作成した共有キーと初期化ベクタをAESへ設定する
	/// [Set the shared key and initialization vector derived from the password to AES]
	/// </summary>
	static void SetKeyFromPassword(Aes algorithm, string password) {
		GenerateKeyFromPassword(
			password, algorithm.KeySize, out var key, algorithm.BlockSize, out var iv);
		algorithm.Key = key;
		algorithm.IV = iv;
	}

	/// <summary>
	/// 文字列を暗号化する(失敗したら空白文字列)
	/// [Encrypt a string (returns an empty string if encryption fails)]
	/// </summary>
	/// <param name="sourceString">暗号化する文字列</param> [String to encrypt]
	/// <param name="password">暗号化に使用するパスワード</param> [Password for encryption]
	/// <returns>暗号化された文字列</returns> [Encrypted string]
	static string EncryptString(string sourceString, string password) {
		try {
			using var algorithm = Aes.Create();
			SetKeyFromPassword(algorithm, password);

			//文字列をバイト型配列に変換して暗号化し、Base64文字列で返す
			//[Convert string to byte array, encrypt it and return as Base64 string]
			byte[] strBytes = Encoding.UTF8.GetBytes(sourceString);
			using var encryptor = algorithm.CreateEncryptor();
			byte[] encBytes = encryptor.TransformFinalBlock(strBytes, 0, strBytes.Length);
			return Convert.ToBase64String(encBytes);
		}
		catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine(ex, "EncryptString failed");
		}
		return "";
	}

	/// <summary>
	/// 暗号化された文字列を復号化する(失敗したら空白文字列)
	/// [Decrypt an encrypted string (returns an empty string if decryption fails)]
	/// </summary>
	/// <param name="sourceString">暗号化された文字列</param> [Encrypted string]
	/// <param name="password">暗号化に使用したパスワード</param> [Password used for encryption]
	/// <returns>復号化された文字列</returns> [Decrypted string]
	static string DecryptString(string sourceString, string password) {
		try {
			using var algorithm = Aes.Create();
			SetKeyFromPassword(algorithm, password);

			//Base64文字列をバイト型配列に戻して復号化する
			//[Convert Base64 string to byte array and decrypt it]
			//復号化に失敗すると例外CryptographicExceptionが発生
			//[If decryption fails, a CryptographicException exception occurs]
			byte[] strBytes = Convert.FromBase64String(sourceString);
			using var decryptor = algorithm.CreateDecryptor();
			byte[] decBytes = decryptor.TransformFinalBlock(strBytes, 0, strBytes.Length);
			return Encoding.UTF8.GetString(decBytes);
		}
		catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine(ex, "DecryptString failed");
		}
		return "";
	}
	/// <summary>
	/// LoginRequest のパスワードを暗号化する(内部でUTC変換)
	/// [Encrypt the password of LoginRequest]
	/// </summary>
	public static string EncryptLoginRequest(string plainPass, DateTime dateValue) {
		var cryptPassword = EncryptString(plainPass, dateValue.ToUniversalTime().ToString("ff.yyyyMMddHHmmss"));
		return cryptPassword;
	}
	/// <summary>
	/// LoginRequest のパスワードを復号化する(内部でUTC変換)
	/// [Decrypt the password of LoginRequest]
	/// </summary>
	public static string DecryptLoginRequest(string cryptPass, DateTime dateValue) {
		string orgPassword = DecryptString(cryptPass, dateValue.ToUniversalTime().ToString("ff.yyyyMMddHHmmss"));
		return orgPassword;
	}
	/// <summary>
	/// Vdc / Vdu にいれる値を取得する（UTCのTicks）
	/// [Get the value for Vdc / Vdu (UTC Ticks)]
	/// </summary>
	public static long GetVdate() {
		return DateTime.UtcNow.Ticks;
	}
	/// <summary>
	/// UTC TicksからDateTimeを生成する
	/// [Generate DateTime from UTC Ticks]
	/// </summary>
	public static DateTime FromUtcTicks(long ticks) {
		return new DateTime(ticks, DateTimeKind.Utc);
	}
	public struct IPData {
		public System.Net.IPAddress IPAddress; // IPアドレス [IP address]
		public string MacAddress; // MACアドレス [MAC address]
		public bool Enable; // パケット送受信可能かどうか [Whether packet transmission and reception are possible]
	}
	/// <summary>
	/// 自端末のIPアドレスを取得する
	/// [Retrieve the IP address of the local device]
	/// </summary>
	/// <remarks>頻繁に呼び出す場合は結果をキャッシュすることを検討 [Consider caching results if called frequently]</remarks>
	/// <returns>IPアドレスとMACアドレスのリスト [List of IP and MAC addresses]</returns>
	public static List<IPData> GetIPAddress() {
		var nis = NetworkInterface.GetAllNetworkInterfaces();
		var retList = new List<IPData>();

		foreach (var ni in nis) {
			// 1. 基本フィルタリング（ループバック、トンネル、非稼働を除外）
			if (ni.OperationalStatus != OperationalStatus.Up) continue;
			if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
				ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

			var macAddr = ni.GetPhysicalAddress().ToString();
			var ips = ni.GetIPProperties();

			foreach (var ipinfo in ips.UnicastAddresses) {
				// 2. リンクローカル(fe80::)を除外
				if (ipinfo.Address.IsIPv6LinkLocal) continue;

				retList.Add(new IPData {
					IPAddress = ipinfo.Address, // インスタンスをそのまま代入（GetAddressBytesは不要）
					MacAddress = macAddr,
					Enable = true
				});
			}
		}
		// 3. 優先順位に基づいたソート
		// AddressFamilyがInterNetwork(IPv4)を0、InterNetworkV6(IPv6)を1として昇順ソート
		return [.. retList
			.OrderBy(x => x.IPAddress.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
			.ThenBy(x => x.IPAddress.ToString())];
	}
	public static string ExtractSubPath(string? url) {
		if (string.IsNullOrWhiteSpace(url)) return string.Empty;

		if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
			// パスが "/" だけ（ルート）の場合は空文字を、それ以外はトリムして返す
			return uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.Trim('/');
		}
		return string.Empty;
	}
	/// <summary>
	/// classTypeのpropertyNameプロパティの値を取得する
	/// </summary>
	/// <exception cref="InvalidOperationException"></exception>
	public static string GetRequiredSql(Type classType, string propertyName) {
		var property = classType.GetProperty(propertyName);
		if (property?.GetValue(null) is not string sql || string.IsNullOrWhiteSpace(sql)) {
			throw new InvalidOperationException($"{classType.FullName}.{propertyName} が定義されていません。");
		}
		return sql;
	}

}

