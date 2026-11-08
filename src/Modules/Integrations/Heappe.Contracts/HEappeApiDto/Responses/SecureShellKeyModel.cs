using System;
using System.Collections.Generic;
using System.Text;

namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record SecureShellKeyModel(
    string Username,
    int KeyType,
    string PublicKeyOpenSSH,
    string PublicKeyPEM
    );