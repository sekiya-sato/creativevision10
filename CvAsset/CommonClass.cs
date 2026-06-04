using Newtonsoft.Json;
using System.Collections;
using System.Data;
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
	/// <param name="obj"></param>
	/// <returns></returns>
	public static string SerializeObject(object obj) {
		return JsonConvert.SerializeObject(obj, jsonOptions);
	}
	/// <summary>
	/// 共通オプションを使ってオブジェクトをデシリアライズ
	/// </summary>
	/// <param name="obj"></param>
	/// <param name="t"></param>
	/// <returns></returns>
	public static object? DeserializeObject(string obj, Type t) {
		return JsonConvert.DeserializeObject(obj, t, jsonOptions);
	}
	public static T? DeserializeObject<T>(string obj) {
		return JsonConvert.DeserializeObject<T>(obj, jsonOptions);
	}
	/// <summary>
	/// 内容が同じ別オブジェクトを返す
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="obj"></param>
	/// <returns></returns>
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
	/// <param name="type"></param>
	/// <param name="src"></param>
	/// <param name="dst"></param>
	public static void DeepCopyValue(Type type, object? src, object? dst) {
		if (src == null || dst == null) return;
		// プロパティ情報を取得（インスタンス、公開、非公開を含む）
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
	/// BaseDbClass継承型の一覧をDataTableへ変換する
	/// [Convert a list of BaseDbClass derived types to a DataTable]
	/// </summary>
	/// <remarks>リフレクションを使用するため大量データでの呼び出しには注意 [Caution: uses reflection, avoid for large datasets]</remarks>
	public static DataTable ToDataTable<T>(IEnumerable<T> items) where T : class {
		ArgumentNullException.ThrowIfNull(items);
		if (!IsBaseDbClassType(typeof(T))) {
			throw new ArgumentException($"{typeof(T).FullName} は BaseDbClass 継承型ではありません。");
		}

		var table = new DataTable(typeof(T).Name);
		var properties = GetDbTableProperties(typeof(T));

		foreach (var property in properties) {
			table.Columns.Add(property.Name, GetDataTableColumnType(property.PropertyType));
		}

		foreach (var item in items) {
			var row = table.NewRow();
			foreach (var property in properties) {
				row[property.Name] = ConvertToDataTableValue(property.GetValue(item), property.PropertyType);
			}
			table.Rows.Add(row);
		}

		return table;
	}

	static PropertyInfo[] GetDbTableProperties(Type type) {
		var properties = type
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.CanRead)
			.Where(property => property.GetIndexParameters().Length == 0)
			.Where(IsDbTableProperty)
			.ToList();

		return [
			.. properties.Where(property => property.Name == "Id"),
			.. properties.Where(property => property.Name == "Vdc"),
			.. properties.Where(property => property.Name == "Vdu"),
			.. properties.Where(property => property.Name != "Id" && property.Name != "Vdc" && property.Name != "Vdu")
		];
	}

	static bool IsDbTableProperty(PropertyInfo property) {
		return !HasAttribute(property, "IgnoreAttribute")
			&& !HasAttribute(property, "JsonIgnoreAttribute")
			&& !HasAttribute(property, "ComputedColumnAttribute")
			&& !HasAttribute(property, "ResultColumnAttribute");
	}

	static Type GetDataTableColumnType(Type propertyType) {
		var actualType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

		if (actualType.IsEnum) {
			return typeof(int);
		}

		if (IsSimpleDbValueType(actualType)) {
			return actualType;
		}

		return typeof(string);
	}

	static object ConvertToDataTableValue(object? value, Type propertyType) {
		if (value == null) {
			return DBNull.Value;
		}

		var actualType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

		if (actualType.IsEnum) {
			return (int)value;
		}

		if (IsSimpleDbValueType(actualType)) {
			return value;
		}

		return SerializeObject(value);
	}

	static bool IsSimpleDbValueType(Type type) {
		return type.IsPrimitive
			|| type == typeof(string)
			|| type == typeof(decimal)
			|| type == typeof(DateTime)
			|| type == typeof(DateTimeOffset)
			|| type == typeof(Guid)
			|| type == typeof(TimeSpan);
	}

	static bool IsBaseDbClassType(Type type) {
		for (var current = type; current != null; current = current.BaseType) {
			if (current.FullName == "CvBase.BaseDbClass") {
				return true;
			}
		}

		return false;
	}

	static bool HasAttribute(MemberInfo member, string attributeTypeName) {
		return member
			.GetCustomAttributes(inherit: true)
			.Any(attribute => attribute.GetType().Name == attributeTypeName);
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
		// TODO: イテレーション回数を現代標準(600,000以上)へ見直す（既存暗号化データ互換性に注意）
		var derivedBytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100, HashAlgorithmName.SHA256, derivedLength);

		//共有キーと初期化ベクタを生成する
		//[Generate the shared key and initialization vector]
		key = new byte[keyBytes];
		iv = new byte[ivBytes];
		Buffer.BlockCopy(derivedBytes, 0, key, 0, keyBytes);
		Buffer.BlockCopy(derivedBytes, keyBytes, iv, 0, ivBytes);
	}

	/// <summary>
	/// 文字列を暗号化する(失敗したら空白文字列)
	/// [Encrypt a string (returns an empty string if encryption fails)]
	/// </summary>
	/// <param name="sourceString">暗号化する文字列</param> [String to encrypt]
	/// <param name="password">暗号化に使用するパスワード</param> [Password for encryption]
	/// <returns>暗号化された文字列</returns> [Encrypted string]
	public static string EncryptString(string sourceString, string password) {
		try {
			using var algorithm = Aes.Create();
			//パスワードから共有キーと初期化ベクタを作成
			//[Create shared key and initialization vector from the password]
			byte[] key, iv;
			GenerateKeyFromPassword(
				password, algorithm.KeySize, out key, algorithm.BlockSize, out iv);
			algorithm.Key = key;
			algorithm.IV = iv;

			//文字列をバイト型配列に変換する
			//[Convert string to byte array]
			byte[] strBytes = Encoding.UTF8.GetBytes(sourceString);

			//対称暗号化オブジェクトの作成
			//[Create symmetric encryption object]
			ICryptoTransform encryptor = algorithm.CreateEncryptor();
			//バイト型配列を暗号化する
			//[Encrypt byte array]
			byte[] encBytes = encryptor.TransformFinalBlock(strBytes, 0, strBytes.Length);
			//閉じる
			//[Close]
			encryptor.Dispose();

			//バイト型配列を文字列に変換して返す
			//[Convert byte array to string and return]
			return Convert.ToBase64String(encBytes);

		}
		catch (Exception ex) {
			// TODO: 暗号化失敗時の例外処理を見直す（現状は後方互換のため空文字を返す）
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
	public static string DecryptString(string sourceString, string password) {
		try {
			using var algorithm = Aes.Create();
			//パスワードから共有キーと初期化ベクタを作成
			//[Create shared key and initialization vector from the password]
			byte[] key, iv;
			GenerateKeyFromPassword(
				password, algorithm.KeySize, out key, algorithm.BlockSize, out iv);
			algorithm.Key = key;
			algorithm.IV = iv;

			//文字列をバイト型配列に戻す
			//[Convert string to byte array]
			byte[] strBytes = Convert.FromBase64String(sourceString);

			//対称暗号化オブジェクトの作成
			//[Create symmetric encryption object]
			ICryptoTransform decryptor = algorithm.CreateDecryptor();
			//バイト型配列を復号化する
			//[Decrypt byte array]
			//復号化に失敗すると例外CryptographicExceptionが発生
			//[If decryption fails, a CryptographicException exception occurs]
			byte[] decBytes = decryptor.TransformFinalBlock(strBytes, 0, strBytes.Length);
			//閉じる [Close]
			decryptor.Dispose();

			//バイト型配列を文字列に戻して返す
			//[Convert byte array to string and return]
			return Encoding.UTF8.GetString(decBytes);
		}
		catch (Exception ex) {
			// TODO: 復号化失敗時の例外処理を見直す（現状は後方互換のため空文字を返す）
			System.Diagnostics.Debug.WriteLine(ex, "DecryptString failed");
		}
		return "";
	}
	/// <summary>
	/// LoginRequest のパスワードを暗号化する(内部でUTC変換)
	/// [Encrypt the password of LoginRequest]
	/// </summary>
	/// <returns></returns>
	public static string EncryptLoginRequest(string plainPass, DateTime dateValue) {
		var cryptPassword = EncryptString(plainPass, dateValue.ToUniversalTime().ToString("ff.yyyyMMddHHmmss"));
		return cryptPassword;
	}
	/// <summary>
	/// LoginRequest のパスワードを復号化する(内部でUTC変換)
	/// [Decrypt the password of LoginRequest]
	/// </summary>
	/// <returns></returns>
	public static string DecryptLoginRequest(string cryptPass, DateTime dateValue) {
		string orgPassword = DecryptString(cryptPass, dateValue.ToUniversalTime().ToString("ff.yyyyMMddHHmmss"));
		return orgPassword;
	}
	/// <summary>
	/// VUpdatedにいれる値を取得する（UTCのTicks）
	/// [Get the value for VUpdated (UTC Ticks)]
	/// </summary>
	/// <returns></returns>
	public static long GetVdate() {
		return DateTime.UtcNow.Ticks;
	}
	/// <summary>
	/// UTC TicksからDateTimeを生成する
	/// [Generate DateTime from UTC Ticks]
	/// </summary>
	/// <param name="ticks"></param>
	/// <returns></returns>
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
	/// 和名の月名を返す
	/// [Return Japanese month names]
	/// </summary>
	/// <returns>和名の月名配列（1月〜12月＋空文字） [Array of Japanese month names (Jan-Dec + empty)]</returns>
	/// example: 標準の月名を和風月名で上書きする場合 culture.DateTimeFormat.MonthNames = Common.MonthNames(); DateTime.Now.ToString("MMMM", culture);
	public static string[] MonthNames() => new[]{
			"睦月", "如月", "弥生", "卯月", "皐月", "水無月",
			"文月", "葉月", "長月", "神無月", "霜月", "師走", ""};

	/// <summary>
	/// classTypeのpropertyNameプロパティの値を取得する
	/// </summary>
	/// <param name="classType"></param>
	/// <param name="propertyName"></param>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	public static string GetRequiredSql(Type classType, string propertyName) {
		var property = classType.GetProperty(propertyName);
		if (property?.GetValue(null) is not string sql || string.IsNullOrWhiteSpace(sql)) {
			throw new InvalidOperationException($"{classType.FullName}.{propertyName} が定義されていません。");
		}
		return sql;
	}

	/// <summary>
	/// itemのIdプロパティの値を取得する
	/// </summary>
	/// <param name="item"></param>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	public static object GetId(object item) {
		var idProperty = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
		if (idProperty == null) {
			throw new InvalidOperationException($"{item.GetType().FullName}.Id が見つかりません。");
		}
		return idProperty.GetValue(item)
			?? throw new InvalidOperationException($"{item.GetType().FullName}.Id が null です。");
	}


}

