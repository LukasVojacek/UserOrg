using System;
using System.Collections.Generic;
using System.Text;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow.Steps;

internal sealed record SendPrimaryInvestigatorEmailStep(
    Guid RequestId,
    string PrimaryInvestigatorEmail
    ) : StepRequest(RequestId, "SendPrimaryInvestigatorEmail");