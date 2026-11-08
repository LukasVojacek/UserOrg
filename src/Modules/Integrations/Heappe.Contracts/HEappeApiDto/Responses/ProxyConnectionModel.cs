namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ProxyConnectionModel
(
      long Id,
      string Host,
      long Port,
      int Type,
      string? Username,
      string? Password
);