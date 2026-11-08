using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Domain.Provider;

public enum EProjectResourceState
{
    Failed = -1,
    Draft = 0,
    CheckHeappeStep = 1,
    InitializeProjectResourceStep = 2,
    InitializeHeappeStep = 3,
    WaitingForCreationAccounts = 4,
    FinalizeCreationProjectResourceStep = 5,
    Published = 100,
    Deleted = 200
}