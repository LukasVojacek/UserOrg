using System;
using System.Collections.Generic;
using System.Text;

namespace RemI.Features.Users;

public sealed record ProjectSystemRoleRecord(
    string RoleName,
    string? ProjectShortName,
    IReadOnlyList<string> SystemPermissionTypes
);