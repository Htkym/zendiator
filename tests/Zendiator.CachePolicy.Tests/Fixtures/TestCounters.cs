using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public static class TestCounters
{
    public static void ResetAll()
    {
        Val0Handler.FactoryCalls = 0; Val0Handler.Constructions = 0;
        Guid0Handler.FactoryCalls = 0; Guid0Handler.Constructions = 0;
        ProbeHandler.FactoryCalls = 0; ProbeHandler.Constructions = 0;
        PipeHandler.FactoryCalls = 0; PipeHandler.Constructions = 0;
        PipeB0.FactoryCalls = 0; PipeB0.Constructions = 0; PipeB0.HandleCalls = 0;
        PipeB1.FactoryCalls = 0; PipeB1.Constructions = 0; PipeB1.HandleCalls = 0;
        PipeB2.FactoryCalls = 0; PipeB2.Constructions = 0; PipeB2.HandleCalls = 0;
        GateHandler.FactoryCalls = 0; GateHandler.Constructions = 0;
        GateBehavior.HandleCalls = 0;
        RetryHandler.FactoryCalls = 0; RetryHandler.Constructions = 0;
        RetryTwiceBehavior.HandleCalls = 0;
        TokHandler.FactoryCalls = 0; TokHandler.Constructions = 0;
        TokBehavior.HandleCalls = 0;
        AddHandler.FactoryCalls = 0; AddHandler.Constructions = 0;
        AddTenBehavior.HandleCalls = 0;
        RefHandler.FactoryCalls = 0; RefHandler.Constructions = 0;
        NullPassBehavior.HandleCalls = 0;
        ExpHandler.FactoryCalls = 0; ExpHandler.Constructions = 0;
        ExpPipeHandler.FactoryCalls = 0; ExpPipeHandler.Constructions = 0;
        ExpPipeBehavior.HandleCalls = 0;
        SuspHandler.FactoryCalls = 0; SuspHandler.Constructions = 0;
        SuspPipeHandler.FactoryCalls = 0; SuspPipeHandler.Constructions = 0;
        SuspBehavior.HandleCalls = 0; SuspBehavior.FinallyCalls = 0;
        FailHandler.FactoryCalls = 0; FailHandler.Constructions = 0;
        FailFinallyBehavior.HandleCalls = 0; FailFinallyBehavior.FinallyCalls = 0;
        DispHandler.FactoryCalls = 0; DispHandler.Constructions = 0; DispHandler.DisposeCount = 0;
        ReHandler.FactoryCalls = 0; ReHandler.Constructions = 0;
        ReenterBehavior.HandleCalls = 0;
    }
}
