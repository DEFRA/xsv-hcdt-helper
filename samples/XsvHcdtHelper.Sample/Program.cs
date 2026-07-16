using Microsoft.Extensions.DependencyInjection;
using Parquet;
using XsvHcdtHelper;

if (args.Length < 2)
{
    Console.WriteLine("Usage: XsvHcdtHelper.Sample <input-file> <output-file>");
    return;
}

var inputPath = args[0];
var outputPath = args[1];
var isParquet = outputPath.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase);

if (!File.Exists(inputPath))
{
    Console.WriteLine($"Error: Input file not found: {inputPath}");
    return;
}

Console.WriteLine($"Setting up XsvHcdtHelper to convert {inputPath} -> {outputPath} (Format: {(isParquet ? "Parquet" : "CSV")}) ...");

var services = new ServiceCollection();

services.AddXsvHcdtHelper(options =>
{
    options.OutputFormat = isParquet ? OutputFormat.Parquet : OutputFormat.Csv;
    options.InputDelimiter = FieldDelimiter.Auto;
});

var provider = services.BuildServiceProvider();
var normaliser = provider.GetRequiredService<IXsvHcdtNormaliser>();

Console.WriteLine("Normalising file...");
var report = await normaliser.NormaliseFileAsync(inputPath, outputPath);

Console.WriteLine("\n--- Validation Report ---");
Console.WriteLine($"Declared Records: {report.DeclaredRecordCount}");
Console.WriteLine($"Actual Records:   {report.ActualDataRecords}");
Console.WriteLine($"Trailer matched:  {report.TrailerCountMatched}");
Console.WriteLine($"Header matched:   {report.HeaderTrailerMatched}");
Console.WriteLine("-------------------------\n");

if (isParquet)
{
    await using var stream = File.OpenRead(outputPath);
    await using var reader = await ParquetReader.CreateAsync(stream);
    Console.WriteLine($"Parquet Output Summary:");
    Console.WriteLine($"  Schema Fields: {reader.Schema.Fields.Count}");
    Console.WriteLine($"  Row Groups:    {reader.RowGroupCount}");
}
else
{
    Console.WriteLine("CSV Output Contents:");
    Console.WriteLine(await File.ReadAllTextAsync(outputPath));
}

Console.WriteLine("Done.");