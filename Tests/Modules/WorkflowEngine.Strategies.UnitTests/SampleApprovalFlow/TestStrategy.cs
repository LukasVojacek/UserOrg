using System;
using System.Collections.Generic;
using System.Text;

using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Strategies.Tests.SampleApprovalFlow;

class TestStrategy : IStrategyRegistry
{
    public IRequestStrategy Resolve(string strategyName)
    {
        throw new NotImplementedException();
    }

    public IRequestStrategy ResolveByStepType(Type initialStepType)
    {
        throw new NotImplementedException();
    }
}
