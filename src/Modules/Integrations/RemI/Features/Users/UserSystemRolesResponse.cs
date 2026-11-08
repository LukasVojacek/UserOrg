using System;
using System.Collections.Generic;
using System.Text;

namespace RemI.Features.Users;

public sealed record UserSystemRolesResponse(
    string IdpUserId,
    string UserName,
    IReadOnlyList<ProjectSystemRoleRecord> SystemRoles
) : BaseUserResponse(IdpUserId, UserName);