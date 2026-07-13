namespace XsvHcdtHelper;

public sealed record XsvValidationReport(
    string? FileName,
    string? Timestamp,
    long DeclaredRecordCount,
    long ActualDataRecords,
    bool TrailerCountMatched,
    bool HeaderTrailerMatched,
    IReadOnlyList<string> Columns);