using System;
using System.Collections.Generic;
using System.Text;

namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record ClusterAvailabilityRequest(
    long ProjectId,
    string Username
);