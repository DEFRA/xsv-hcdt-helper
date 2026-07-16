namespace XsvHcdtHelper.Tests;

using System.Diagnostics;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

public class ThroughputTests
{
    [Fact]
    public async Task NormaliseFileAsync_GivenOneMillionRows_ProcessesQuicklyWithBoundedMemory()
    {
        const int rowCount = 1_000_000;
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        try
        {
            await using (var writer = new StreamWriter(inputPath))
            {
                await writer.WriteLineAsync("H|LARGE_FILE.csv|22022026 07:46:03");
                await writer.WriteLineAsync("C|ID|NAME|VALUE");
                for (int i = 0; i < rowCount; i++)
                {
                    await writer.WriteLineAsync($"D|{i}|Name_{i}|{i * 10}");
                }
                await writer.WriteLineAsync($"T|LARGE_FILE.csv|22022026 07:46:03|{rowCount}");
            }

            var stopwatch = new Stopwatch();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var initialMemory = GC.GetAllocatedBytesForCurrentThread();

            stopwatch.Start();
            var report = await XsvHcdt.NormaliseFileAsync(inputPath, outputPath, o =>
            {
                o.InputDelimiter = FieldDelimiter.Pipe;
                o.OutputFormat = OutputFormat.Csv;
            });
            stopwatch.Stop();

            var finalMemory = GC.GetAllocatedBytesForCurrentThread();
            var allocatedMegabytes = (finalMemory - initialMemory) / 1024 / 1024;

            report.ActualDataRecords.Should().Be(rowCount);
            report.TrailerCountMatched.Should().BeTrue();

            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(10.0);

            allocatedMegabytes.Should().BeLessThan(250);

            Console.WriteLine($"Processed {rowCount:N0} rows in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
            Console.WriteLine($"Memory allocated on thread: {allocatedMegabytes} MB");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task NormaliseFileAsync_GivenOneMillionRowsToParquet_ProcessesQuicklyWithBoundedMemory()
    {
        const int rowCount = 1_000_000;
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        try
        {
            await using (var writer = new StreamWriter(inputPath))
            {
                await writer.WriteLineAsync("H|LARGE_FILE.csv|22022026 07:46:03");
                await writer.WriteLineAsync("C|ID|NAME|VALUE");
                for (int i = 0; i < rowCount; i++)
                {
                    await writer.WriteLineAsync($"D|{i}|Name_{i}|{i * 10}");
                }
                await writer.WriteLineAsync($"T|LARGE_FILE.csv|22022026 07:46:03|{rowCount}");
            }

            var stopwatch = new Stopwatch();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var initialMemory = GC.GetAllocatedBytesForCurrentThread();

            stopwatch.Start();
            var report = await XsvHcdt.NormaliseFileAsync(inputPath, outputPath, o =>
            {
                o.InputDelimiter = FieldDelimiter.Pipe;
                o.OutputFormat = OutputFormat.Parquet;
                o.RowGroupSize = 50_000; // default - forces ~20 flushes across the run
            });
            stopwatch.Stop();

            var finalMemory = GC.GetAllocatedBytesForCurrentThread();
            var allocatedMegabytes = (finalMemory - initialMemory) / 1024 / 1024;

            report.ActualDataRecords.Should().Be(rowCount);
            report.TrailerCountMatched.Should().BeTrue();
            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(15.0);

            allocatedMegabytes.Should().BeLessThan(500);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
