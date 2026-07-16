# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-07-13
### Added
- Core streaming parser for H/C/D/T files with bounded memory footprint.
- Native sinks for outputting to standard CSV (RFC 4180) and Apache Parquet.
- DI registration (`AddXsvHcdtHelper`) with configuration and builder extensions.
- Extensibility via `IRowSink` and `.AddOutputSink<T>()`.
- Strict validation rules against expected Trailer record counts and Header/Trailer matching.
- Partial file cleanup on validation failure.
- Auto-detection of pipe (`|`) and comma (`,`) delimiters.