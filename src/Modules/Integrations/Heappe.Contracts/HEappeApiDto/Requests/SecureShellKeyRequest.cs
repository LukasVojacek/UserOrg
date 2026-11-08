using System;
using System.Collections.Generic;
using System.Text;

namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record SecureShellKeyRequest(
    IList<Credentials> Credentials,
    long ProjectId
);

public sealed record Credentials(
    string Username,
    string? Password
);