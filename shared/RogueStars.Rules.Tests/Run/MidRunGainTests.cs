using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Owner playtest 0.1.200 ("the shop sells 'start of run' skills"): a skill gained mid-run — draft, shop, Mythic choice,
/// event — must do its job from that moment. Run-start effects of run skills (OnRunStart triggers) apply when the skill
/// is gained (RunSystem.GrantSkill), and their card never says "Run start".
/// </summary>
public class MidRunGainTests
{
    private static readonly ContentDb C = ContentDb.Default;

    /// <summary>Effects RunSystem.ApplyRunTrigger applies at once when an OnRunStart trigger fires (on gain, mid-run too).</summary>
    private static readonly HashSet<EffectKind> AppliedOnGain = new()
    {
        EffectKind.SlowAwakening, EffectKind.GainRunCoins, EffectKind.RunStatGrowth, EffectKind.ExtraDice, EffectKind.SkipNegativeEvent,
    };

    /// <summary>Every skill a run can gain after it started: drafts, shop, Mythic choices (run + laboratory pool) and event rewards.</summary>
    private static List<SkillDef> MidRunSkills()
    {
        var ids = new HashSet<string>(C.Skills.Values
            .Where(s => s.Draftable && !s.Signature && (s.Source == SkillSource.Run || s.Source == SkillSource.Laboratory)).Select(s => s.Id));
        foreach (var ev in C.RunEvents.Values)
            foreach (var o in ev.Options)
                foreach (var oc in o.Outcomes)
                    foreach (var fx in oc.Effects)
                        if (fx.Kind == RunEffectKind.GainSpecificSkill && fx.Param != null) ids.Add(fx.Param);
        return ids.Select(id => C.Skill(id)).Where(s => s != null).Select(s => s!).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Audit_NoSkillGainedMidRun_HasAStartOnlyEffect()
    {
        var bad = new List<string>();
        foreach (var s in MidRunSkills())
            foreach (var t in s.Triggers)
            {
                if (t.When != TriggerKind.OnRunStart) continue;
                bool grant = t.Param != null && (t.Param == "skip_negative_events" || t.Param.StartsWith("random_skill:") || t.Param.StartsWith("skill:") || C.Skill(t.Param) != null);
                if (!grant && !AppliedOnGain.Contains(t.Effect)) bad.Add(s.Id + " (" + t.Effect + ")");
            }
        Assert.True(bad.Count == 0, "start-only effects on skills gained mid-run: " + string.Join(", ", bad));
    }

    [Fact]
    public void Audit_NoSkillGainedMidRun_ReadsAsRunStart()
    {
        foreach (var s in MidRunSkills())
            for (int lv = 1; lv <= C.RunTuning.MaxSkillLevelOf(s.Rarity); lv++)
            {
                string text = SkillText.Full(C, s, lv);
                Assert.DoesNotContain("Run start", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("start of the run", text, StringComparison.OrdinalIgnoreCase);
            }
        Assert.Equal("From now on, the boss awakens 9% slower.", SkillText.Full(C, C.Skill("gen_calm_mind")!, 1));
    }

    [Fact]
    public void CalmMind_BoughtMidRun_SlowsTheRestOfTheClock()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        var run = s.Run.Active!;
        RunTestKit.LineBoard(run, Enumerable.Repeat("empty", 80).ToArray());
        for (int i = 0; i < 5; i++) { s.Run.Active!.Phase = RunPhase.AwaitRoll; Assert.True(RunTestKit.Exec(e, ref s, new RunRoll()).Ok); }
        run = s.Run.Active!;
        double before = run.AwakeningReductionPct;
        // the shop sells Calm Mind halfway through the run
        run.Phase = RunPhase.Shop; run.Coins = 10_000;
        run.Shop.Clear();
        run.Shop.Add(new ShopOffer { Kind = ShopOfferKind.Skill, SkillId = "gen_calm_mind", Price = 10 });
        Assert.True(RunTestKit.Exec(e, ref s, new RunShopBuy { Index = 0 }).Ok);
        run = s.Run.Active!;
        Assert.Contains("gen_calm_mind", run.Skills);
        Assert.Equal(before + 9, run.AwakeningReductionPct, 6);
        // the same rolls awaken the boss less than without it
        double slowed = RunSystem.AwakeningPerRoll(C, run);
        run.AwakeningReductionPct = before;
        Assert.True(slowed > 0 && slowed < RunSystem.AwakeningPerRoll(C, run), "the rest of the clock runs slower");
    }
}
