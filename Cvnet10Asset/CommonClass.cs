using Newtonsoft.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;


namespace Cvnet10Asset;
public sealed class Common {
	public static string Version { get; } = "1.00.00";

    // JsonConvert共通オプション（必要に応じてカスタマイズ／コンバータを追加）
    private static readonly JsonSerializerSettings jsonOptions = new () {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        DefaultValueHandling = DefaultValueHandling.Ignore,
    };
    /// <summary>
    /// 共通オプションを使ってオブジェクトをシリアライズ
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static string SerializeObject(object obj){
        var ret = JsonConvert.SerializeObject(obj, jsonOptions);
        return ret;
    }
    /// <summary>
    /// 共通オプションを使ってオブジェクトをデシリアライズ
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    public static object? DeserializeObject(string obj, Type t) {
        var ret = JsonConvert.DeserializeObject(obj, t, jsonOptions);
        return ret;
    }
	public static T? DeserializeObject<T>(string obj) {
		var ret = JsonConvert.DeserializeObject<T>(obj, jsonOptions);
		return ret;
	}

	static readonly Aes algorithm = Aes.Create();
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
		byte[] salt = Encoding.UTF8.GetBytes("salt-20240801");
		var keyBytes = keySize / 8;
		var ivBytes = blockSize / 8;
		var derivedLength = keyBytes + ivBytes;
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
		catch (Exception) {
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
		catch (Exception) {
		}
		return "";
	}
	/// <summary>
	/// LoginRequest のパスワードを暗号化する(内部でUTC変換)
	/// [Encrypt the password of LoginRequest]
	/// </summary>
	/// <returns></returns>
	public static string EncryptLoginRequest(string planePass, DateTime dateValue) {
		var cryptPassword = EncryptString(planePass, dateValue.ToUniversalTime().ToString("ff.yyyyMMddHHmmss"));
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
	/// VUpdatedにいれる値を取得する
	/// </summary>
	/// <returns></returns>
	public static long GetVdate() {
		return DateTime.Now.ToUniversalTime().Ticks;
	}
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
	/// <returns></returns>
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
		return retList
			.OrderBy(x => x.IPAddress.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
			.ThenBy(x => x.IPAddress.ToString())
			.ToList();
	}

}

