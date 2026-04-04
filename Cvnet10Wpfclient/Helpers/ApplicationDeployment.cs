namespace Cvnet10Wpfclient.Helpers;

public class ApplicationDeployment {
	private static ApplicationDeployment currentDeployment = new();
	private static bool currentDeploymentInitialized = false;

	private static bool isNetworkDeployed = false;
	private static bool isNetworkDeployedInitialized = false;

	public static bool IsNetworkDeployed {
		get {
			if (!isNetworkDeployedInitialized) {
				bool.TryParse(Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed") ?? "", out ApplicationDeployment.isNetworkDeployed);
				ApplicationDeployment.isNetworkDeployedInitialized = true;
			}

			return ApplicationDeployment.isNetworkDeployed;
		}
	}

	public static ApplicationDeployment CurrentDeployment {
		get {
			if (!currentDeploymentInitialized) {
				ApplicationDeployment.currentDeploymentInitialized = true;
			}

			return ApplicationDeployment.currentDeployment;
		}
	}

	public Uri ActivationUri {
		get {
			try {
				Uri.TryCreate(Environment.GetEnvironmentVariable("ClickOnce_ActivationUri") ?? "", UriKind.Absolute, out Uri? val);
				return val ?? new Uri("http://localhost");
			}
			catch (Exception) {
				return new Uri("http://localhost");
			}
		}
	}

	public Version CurrentVersion {
		get {
			try {
				Version.TryParse(Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion") ?? "", out Version? val);
				return val ?? new Version();
			}
			catch (Exception) {
				return new Version();
			}
		}
	}
	public string DataDirectory {
		get { return Environment.GetEnvironmentVariable("ClickOnce_DataDirectory") ?? string.Empty; }
	}

	public bool IsFirstRun {
		get {
			bool.TryParse(Environment.GetEnvironmentVariable("ClickOnce_IsFirstRun") ?? "", out bool val);
			return val;
		}
	}

	public DateTime TimeOfLastUpdateCheck {
		get {
			DateTime.TryParse(Environment.GetEnvironmentVariable("ClickOnce_TimeOfLastUpdateCheck") ?? "", out DateTime value);
			return value;
		}
	}
	public string UpdatedApplicationFullName {
		get {
			return Environment.GetEnvironmentVariable("ClickOnce_UpdatedApplicationFullName") ?? string.Empty;
		}
	}

	public Version UpdatedVersion {
		get {
			try {
				Version.TryParse(Environment.GetEnvironmentVariable("ClickOnce_UpdatedVersion"), out Version? val);
				return val ?? new Version();
			}
			catch (Exception) {
				return new Version();
			}
		}
	}

	public Uri UpdateLocation {
		get {
			try {
				Uri.TryCreate(Environment.GetEnvironmentVariable("ClickOnce_UpdateLocation"), UriKind.Absolute, out Uri? val);
				return val ?? new Uri("http://localhost");
			}
			catch (Exception) {
				return new Uri("http://localhost");
			}
		}
	}

	public Version LauncherVersion {
		get {
			try {
				Version.TryParse(Environment.GetEnvironmentVariable("ClickOnce_LauncherVersion"), out Version? val);
				return val ?? new Version();
			}
			catch (Exception) {
				return new Version();
			}
		}
	}

	private ApplicationDeployment() {
		// As an alternative solution, we could initialize all properties here
	}
}
