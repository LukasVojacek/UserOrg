using System.Collections.Generic;

namespace Heappe.Contracts;

public sealed record DownloadedFileModel(
    string? FileName,
    string? TextContent
);