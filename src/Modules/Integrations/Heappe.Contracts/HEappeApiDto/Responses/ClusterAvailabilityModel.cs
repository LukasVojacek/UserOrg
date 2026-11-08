using System;
using System.Collections.Generic;
using System.Text;

namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ClusterAvailabilityModel(
    string ClusterName,
    bool IsClusterAccessible
);