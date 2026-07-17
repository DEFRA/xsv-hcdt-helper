namespace XsvHcdtHelper.Tests;

using System.Diagnostics;
using FluentAssertions;
using Xunit;
using XsvHcdtHelper;

public class ThroughputTests
{
    [Fact]
    public async Task NormaliseFileAsync_GivenOneMillionRows_ProcessesWithBoundedPeakMemory()
    {
        const int rowCount = 1_000_000;
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        try
        {
            await WriteLargeInputAsync(inputPath, rowCount);

            var stopwatch = Stopwatch.StartNew();
            XsvValidationReport? report = null;

            var peakBytes = await MeasurePeakManagedMemoryAsync(async () =>
            {
                report = await XsvHcdt.NormaliseFileAsync(inputPath, outputPath, o =>
                {
                    o.InputDelimiter = FieldDelimiter.Pipe;
                    o.OutputFormat = OutputFormat.Csv;
                });
            });
            stopwatch.Stop();

            report!.ActualDataRecords.Should().Be(rowCount);
            report.TrailerCountMatched.Should().BeTrue();

            // Peak LIVE managed memory must stay bounded, i.e. not scale with row count:
            // an implementation buffering this ~30 MB / 1M-row input would hold hundreds of MB.
            var peakMegabytes = peakBytes / 1024 / 1024;
            peakMegabytes.Should().BeLessThan(150);

            // Generous sanity bound only - tight wall-clock asserts flake on shared CI runners.
            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(60.0);

            Console.WriteLine($"CSV: {rowCount:N0} rows in {stopwatch.Elapsed.TotalSeconds:F2}s, peak managed heap {peakMegabytes} MB.");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task NormaliseFileAsync_GivenOneMillionRowsToParquet_ProcessesWithBoundedPeakMemory()
    {
        const int rowCount = 1_000_000;
        var inputPath = Path.GetTempFileName();
        var outputPath = Path.GetTempFileName();

        try
        {
            await WriteLargeInputAsync(inputPath, rowCount);

            var stopwatch = Stopwatch.StartNew();
            XsvValidationReport? report = null;

            var peakBytes = await MeasurePeakManagedMemoryAsync(async () =>
            {
                report = await XsvHcdt.NormaliseFileAsync(inputPath, outputPath, o =>
                {
                    o.InputDelimiter = FieldDelimiter.Pipe;
                    o.OutputFormat = OutputFormat.Parquet;
                    o.RowGroupSize = 50_000; // default - forces ~20 row-group flushes across the run
                });
            });
            stopwatch.Stop();

            report!.ActualDataRecords.Should().Be(rowCount);
            report.TrailerCountMatched.Should().BeTrue();

            // Higher bound than CSV: one row group is intentionally buffered at a time.
            var peakMegabytes = peakBytes / 1024 / 1024;
            peakMegabytes.Should().BeLessThan(250);

            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(60.0);

            Console.WriteLine($"Parquet: {rowCount:N0} rows in {stopwatch.Elapsed.TotalSeconds:F2}s, peak managed heap {peakMegabytes} MB.");
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private static async Task WriteLargeInputAsync(string path, int rowCount)
    {
        await using var writer = new StreamWriter(path);
        await writer.WriteLineAsync("H|LARGE_FILE.csv|22022026 07:46:03");
        await writer.WriteLineAsync("C|RECORD_TYPE|ID|NAME|VALUE");
        for (int i = 0; i < rowCount; i++)
        {
            await writer.WriteLineAsync($"D|{i}|Name_{i}|{i * 10}");
        }
        await writer.WriteLineAsync($"T|LARGE_FILE.csv|22022026 07:46:03|{rowCount}");
    }

    /// <summary>
    /// Samples the live managed heap (forced collection) every 50 ms while the action runs
    /// and returns the peak observed. Forcing collection means readings reflect *reachable*
    /// memory rather than uncollected garbage - which is what "bounded memory" is about -
    /// unlike cumulative thread-local allocation counters, which both scale with row count
    /// and read the wrong thread across await boundaries.
    /// </summary>
    private static async Task<long> MeasurePeakManagedMemoryAsync(Func<Task> action)
    {
        using var cts = new CancellationTokenSource();
        long peak = 0;

        var sampler = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var current = GC.GetTotalMemory(forceFullCollection: true);
                if (current > Interlocked.Read(ref peak))
                {
                    Interlocked.Exchange(ref peak, current);
                }

                try
                {
                    await Task.Delay(50, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });

        try
        {
            await action();
        }
        finally
        {
            cts.Cancel();
            await sampler;
        }

        return Interlocked.Read(ref peak);
    }
}
