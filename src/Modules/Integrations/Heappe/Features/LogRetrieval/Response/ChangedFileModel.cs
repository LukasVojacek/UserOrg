namespace Heappe.Features.LogRetrieval.Response;

public sealed record ChangedFileModel(
    string FileName,
    DateTime LastModifiedDate
);