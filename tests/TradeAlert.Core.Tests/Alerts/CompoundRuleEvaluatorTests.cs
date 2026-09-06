using System.Collections.Generic;
using TradeAlert.Core.Models.Alerts;
using Xunit;

namespace TradeAlert.Core.Tests.Alerts;

public sealed class CompoundRuleEvaluatorTests
{
    sealed class FakeEvalContext : ICompoundRuleEvalContext
    {
        readonly Dictionary<(string Tf, AlertConditionId Id), bool> _state = new();
        readonly Dictionary<(string Tf, AlertConditionId Id), int> _eventBars = new();

        public void SetState(string tf, AlertConditionId id, bool active) =>
            _state[(tf, id)] = active;

        public void SetEventBar(string tf, AlertConditionId id, int bar) =>
            _eventBars[(tf, id)] = bar;

        public bool IsStateConditionActive(string tfToken, AlertConditionId condId) =>
            _state.TryGetValue((tfToken, condId), out var ok) && ok;

        public bool IsConditionActive(
            string tfToken, AlertConditionId condId, CompoundEvalSyncMode syncMode, int eventValidBars) =>
            _eventBars.ContainsKey((tfToken, condId));

        public bool TryGetEventFiredBarIndex(string tfToken, AlertConditionId condId, out int firedBarIndex)
        {
            return _eventBars.TryGetValue((tfToken, condId), out firedBarIndex);
        }
    }

    [Fact]
    public void EvaluateRuleAtSnapshot_requires_primary_event_on_candidate_bar()
    {
        CompoundRuleParser.TryParse(CompoundAlertPresets.R2BuyM5, 1, out var rule, out _);

        var ctx = new FakeEvalContext();
        ctx.SetEventBar("5", AlertConditionId.CondBuyEventM5, 10050);
        ctx.SetState("5", AlertConditionId.CanBuyReal, true);
        ctx.SetState("15", AlertConditionId.CanBuyReal, true);
        ctx.SetState("60", AlertConditionId.CanBuyTouchM5, true);
        ctx.SetState("240", AlertConditionId.CanBuyTouchM5, true);

        var candidate = new CompoundSourceEventCandidate
        {
            SourceTfToken = "5",
            SourceBarIndex = 10050,
        };
        var primary = CompoundRuleSourceResolver.Resolve(rule);

        var ok = CompoundRuleEvaluator.EvaluateRuleAtSnapshot(ctx, rule, candidate, primary, new CompoundRuntimeOptions());
        Assert.True(ok.AllTrue);

        var wrongBar = new CompoundSourceEventCandidate
        {
            SourceTfToken = "5",
            SourceBarIndex = 10049,
        };
        var miss = CompoundRuleEvaluator.EvaluateRuleAtSnapshot(ctx, rule, wrongBar, primary, new CompoundRuntimeOptions());
        Assert.False(miss.AllTrue);
        Assert.Contains("5:CondBuyEventM5", miss.MissingLegs);
    }
}
