namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record AccountingModel
(
    long Id,
    string Formula,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    DateTime ValidityFrom,
    DateTime? ValidityTo
);