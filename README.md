# XsvHcdtHelper

A small, reusable, bounded-memory .NET library for streaming and validating pipe- or comma-delimited flat files wrapped in an **H/C/D/T** envelope. It parses these files, validates them against their own internal headers and trailers, and outputs standard RFC 4180 CSV or Apache Parquet files.

## H/C/D/T Format

Legacy upstream systems frequently deliver bulk data wrapped in an H/C/D/T envelope.

- **H — Header:** `H|<filename>|<timestamp>`
- **C — Columns:** Declares the column names for the data rows.
- **D — Data:** Zero or more records containing the actual payload.
- **T — Trailer:** `T|<filename>|<timestamp>|<record_count>`

The trailer ensures the file is complete and untruncated. This library guarantees the file matches this envelope before processing succeeds.

### Record-type tag column

The `C` record declares its column names *after* the `C` tag, and the first declared column (commonly `RECORD_TYPE`) is occupied by each data row's leading `D` tag. A `D` row **including its tag** therefore has exactly as many fields as the declared column list, and the tag is emitted as the first output value:

```
C|RECORD_TYPE|ID|NAME      ->  RECORD_TYPE,ID,NAME
D|1|Alice                  ->  D,1,Alice
```

---

## Installation

From the DEFRA GitHub Packages feed:

```bash
dotnet add package XsvHcdtHelper
```

---

## Setup (Dependency Injection)

Register the services in your `IServiceCollection`. By default, the library uses:

- Automatic delimiter detection
- CSV output
- Full H/C/D/T envelope validation

### 1. Zero-configuration (recommended)

```csharp
services.AddXsvHcdtHelper();
```

### 2. Inline configuration

```csharp
services.AddXsvHcdtHelper(options =>
{
    options.OutputFormat = OutputFormat.Parquet;
    options.InputDelimiter = FieldDelimiter.Pipe;
    options.InputQuoting = QuoteHandling.None; // Treat '"' in the input as literal data
    options.StrictFieldCount = true; // Throws if a row doesn't match the column count
});
```

### 3. IConfiguration binding

```csharp
services.AddXsvHcdtHelper(configuration.GetSection("XsvHcdt"));
```

For example, from `appsettings.json`.

---

## Unquoted (legacy) input

By default the parser applies RFC 4180 quoting to the **input**: a field whose first character
is `"` is treated as quoted, so the delimiter and line breaks are literal until the closing
quote, and `""` is an escaped quote.

Many legacy extracts never quote anything — they emit raw delimited text in which `"` is just
another character. A free-text column such as:

```
D|100|"holding closed in Data cleanse exercise".
```

looks like a quoted field followed by stray text, and fails with:

```
XsvValidationException: A quoted field must be followed by a delimiter or the end of the record.
```

Set `InputQuoting` to `QuoteHandling.None` for those feeds. Fields are then split on the
delimiter alone and every `"` is preserved as data:

```csharp
services.AddXsvHcdtHelper(options =>
{
    options.InputDelimiter = FieldDelimiter.Pipe;
    options.InputQuoting = QuoteHandling.None;
});
```

This affects **input parsing only**. CSV output is always written as valid RFC 4180, so a value
containing quotes is escaped correctly on the way out.

> With `QuoteHandling.None` a delimiter between quotes is a real delimiter, so `"x|y"` is two
> fields rather than one. Only use it for feeds that genuinely never quote.

---

## Usage

### File-to-file

```csharp
public class MyJob(IXsvHcdtNormaliser normaliser)
{
    public async Task RunAsync()
    {
        var report = await normaliser.NormaliseFileAsync(
            "input.psv",
            "output.parquet");

        Console.WriteLine($"Processed {report.ActualDataRecords} records.");
    }
}
```

### Stream-to-stream

You retain ownership of the streams; the library does **not** dispose them.

```csharp
await using var inputStream = ...;
await using var outputStream = ...;

var report = await normaliser.NormaliseAsync(
    inputStream,
    outputStream,
    ct: cancellationToken);
```

### No DI container? Use the static facade

`XsvHcdt` offers the same operations with the same defaults:

```csharp
var report = await XsvHcdt.NormaliseFileAsync("input.psv", "output.csv");
```

### Lower-level record access (`IXsvHcdtReader`)

If you want the parsed records rather than a normalised file, inject `IXsvHcdtReader`. It yields the `C` record (column names) followed by one `XsvRecord` per `D` row — whose fields include the leading tag, aligned 1:1 with the columns — while still enforcing the full envelope validation as the stream is consumed:

```csharp
await foreach (var record in reader.ReadAsync(inputStream, ct: ct))
{
    // record.Tag is 'C' or 'D'; record.Fields align with the declared columns.
}
```

---

## Extensibility (Custom Output Sinks)

The library supports CSV and Parquet out of the box.

If you need a different output format (for example JSON), implement `IRowSink` and register it. The parser continues to handle streaming, validation, and memory management, while your sink is responsible only for writing rows.

```csharp
services
    .AddXsvHcdtHelper()
    .AddOutputSink<MyCustomJsonSink>();
```

---

## Validation Behaviour

By default, the library fails fast and throws an `XsvValidationException` if:

- The file does not start with `H` or end with `T`, or any content follows the trailer.
- The `C` (Columns) record is missing.
- The trailer record count is non-numeric, or does not exactly match the number of `D` records processed.
- The header and trailer filenames or timestamps do not match.

Where applicable the exception carries the `Expected` and `Actual` values as properties.

### Output Safety

If validation fails at any point (including when processing the trailer), the library never leaves partial output behind:

- **File outputs** created by `NormaliseFileAsync` are deleted. A pre-existing file at the output path is only ever touched once processing has actually started.
- **Seekable streams** are truncated back to zero length.
- **Non-seekable streams** (network/pipe) cannot be truncated; the validation exception still propagates unchanged, and the caller owns any downstream cleanup.