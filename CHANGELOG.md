# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- `XsvHcdtOptions.InputQuoting` (`QuoteHandling.Rfc4180` | `QuoteHandling.None`) to control how
  `"` is interpreted in the input. `QuoteHandling.None` splits on the delimiter alone and keeps
  every `"` as literal data, for legacy extracts that never quote their fields.
- `XsvHcdtOptions.RequireHeader` and `XsvHcdtOptions.RequireTrailer` (both default `true`) so a
  slice of a file that was split after export can be normalised on its own, whether or not it
  happens to contain the `H` and `T` records. Records that *are* present are still validated.

### Fixed
- Unquoted feeds whose free-text columns begin with `"` (for example
  `"holding closed in Data cleanse exercise".`) could not be parsed at all: the value was read as
  a quoted field and rejected with "A quoted field must be followed by a delimiter or the end of
  the record." There was previously no way to opt out of RFC 4180 input quoting.
- The header/trailer match no longer runs when there is no header, where it compared the trailer
  filename against `null` and failed.

## [0.1.0] - 2026-07-13
### Added
- Core streaming parser for H/C/D/T files with bounded memory footprint.
- Record-type tag handling: a `D` row's leading tag is kept as the first output value
  (it occupies the first declared column, commonly `RECORD_TYPE`), so data rows align
  1:1 with the `C` column list.
- Envelope hardening: non-numeric trailer record counts and any content after the
  trailer are rejected with `XsvValidationException` (trailing blank lines tolerated).
- `Expected`/`Actual` context properties on `XsvValidationException`.
- Output safety: seekable output streams are truncated on failure, non-seekable
  streams propagate the original error unchanged, and file outputs are only deleted
  when the failed call actually created them.
- Packaging: OGL-UK-3.0 licence expression, embedded symbols and packaging
  validation on every PR build.
- Native sinks for outputting to standard CSV (RFC 4180) and Apache Parquet.
- DI registration (`AddXsvHcdtHelper`) with configuration and builder extensions.
- Extensibility via `IRowSink` and `.AddOutputSink<T>()`.
- Strict validation rules against expected Trailer record counts and Header/Trailer matching.
- Partial file cleanup on validation failure.
- Auto-detection of pipe (`|`) and comma (`,`) delimiters.