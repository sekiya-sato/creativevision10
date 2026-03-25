using Cvnet10Prints;

// See https://aka.ms/new-console-template for more information

// dotnet run --project TestPrint/TestPrint.csproj

Console.WriteLine("Hello, World!");
string currentDirectory = AppContext.BaseDirectory;
string baseDirectory = Directory.GetParent(currentDirectory)?.Parent?.Parent?.Parent?.FullName ?? string.Empty;
string baseParent = Directory.GetParent(baseDirectory)?.FullName ?? string.Empty;
string outputPath = "printdata";

var context = new PrintContext {
	BasePath = "",
	FormPath = Path.Combine(baseParent, outputPath, "cvnet57prnhinShouka.qfm"),
	DataPath = Path.Combine(baseParent, outputPath, "data.txt"),
	OutputDir = Path.Combine(baseParent, outputPath),
	OutputFileName = "test.pdf",
};


Console.WriteLine("Printing...");
var printService = new PrintAdapter();
//var products = await printService.CheckLicenseAsync();

var result = await printService.ExecutePrintAsync(context);
Console.WriteLine(result.IsSuccess ? "Print succeeded." : $"Print failed: {result.Message}");

