using System;
using System.Collections.Generic;
using System.Text;

namespace RemI.Features.Users;

public abstract record BaseUserResponse(
    string IdpUserId,
    string UserName
);