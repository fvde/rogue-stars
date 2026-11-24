using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;

namespace RogueStars.Rules.Tests.Run;

/// <summary>
/// Skill redesign, step 4: a run holds up to three signatures. Extra ones come from minibosses (one of the three Mythic choices,
/// 25%) and the rare Resonance event (1 of 2, or coins), never from shops; each awakens on its own synergy count; every owned
/// signature's synergy gets the draft weight; none is ever lost.
/// </summary>
public class MultiSignatureTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static GameState RunWith(GameEngine e, string signature, long seed = 3)
    {
        var s = TestHelpers.NewPlayer(e, seed);
        Assert.True(RunTestKit.Exec(e, ref s, new RunStart { LevelId = "ember_wastes_1", SignatureId = signature }).Ok);
        return s;
    }

    private static void Pick(GameEngine e, ref GameState s, string id, bool mythic)
    {
        var run = s.Run.Active!;
        run.Phase = RunPhase.Draft; run.MythicDraft = mythic; run.DraftOffer.Clear(); run.DraftOffer.Add(id);
        if (mythic) run.PendingMythicDrafts = 1; else run.PendingDrafts = 1;
        var r = RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = id });
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void AMinibossOffer_AddsASignature_ThatAwakensOnItsOwnSynergy()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_fire");
        foreach (var id in C.Skills.Values.Where(x => x.Family == SkillFamily.Bleed && x.Source == SkillSource.Run && x.Draftable && x.Rarity != Rarity.Mythic)
                     .OrderBy(x => x.Id, StringComparer.Ordinal).Take(3).Select(x => x.Id).ToList()) Pick(e, ref s, id, false);
        int bleed = RunDraft.FamilyCount(C, s.Run.Active!, SkillFamily.Bleed);
        Pick(e, ref s, "sig_bleed", true);
        var run = s.Run.Active!;
        Assert.Equal(new[] { "sig_fire", "sig_bleed" }, RunSystem.Signatures(run));
        Assert.Equal("sig_fire", run.SignatureId); // the start pick stays the first
        // found late, it awakens at once with enough skills of its synergy; Fire (no Fire skills) stays asleep
        Assert.Equal(RunSystem.SignatureLevel(C, bleed), RunDraft.Level(run, "sig_bleed"));
        Assert.Equal(1, RunDraft.Level(run, "sig_fire"));
        Assert.Equal(0, run.PendingMythicDrafts);
        // both synergies get the draft weight
        Assert.Equal(1.4, RunDraft.OfferWeight(C, run, C.Skill("fire_kindle")!, 1), 6);
        var freshBleed = C.Skills.Values.First(x => x.Family == SkillFamily.Bleed && x.Source == SkillSource.Run && x.Draftable && !run.Skills.Contains(x.Id));
        Assert.Equal(1.4, RunDraft.OfferWeight(C, run, freshBleed, 1), 6);
        Assert.Equal(1, RunDraft.OfferWeight(C, run, C.Skill("poison_spit")!, 1), 6);
        // a signature is never drafted, bought or offered again once owned
        Assert.DoesNotContain("sig_bleed", RunSystem.FindableSignatures(C, run));
    }

    [Fact]
    public void TheCap_IsThreeSignatures_AndNoneIsEverLost()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_fire");
        Pick(e, ref s, "sig_bleed", true);
        Pick(e, ref s, "sig_radiation", true);
        var run = s.Run.Active!;
        Assert.Equal(3, RunSystem.Signatures(run).Count);
        Assert.Empty(RunSystem.FindableSignatures(C, run));
        // a fourth is refused
        run.Phase = RunPhase.Draft; run.MythicDraft = true; run.DraftOffer.Clear(); run.DraftOffer.Add("sig_ice"); run.PendingMythicDrafts = 1;
        Assert.False(RunTestKit.Exec(e, ref s, new RunDraftPick { SkillId = "sig_ice" }).Ok);
        run = s.Run.Active!;
        run.Phase = RunPhase.AwaitRoll; run.DraftOffer.Clear(); run.PendingMythicDrafts = 0;
        for (int i = 0; i < 30; i++)
        {
            run.Skills.Add("gen_power");
            var ctx = RunTestKit.Ctx(e, s);
            typeof(RunSystem).GetMethod("ApplyEffects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { ctx, run, new List<RunEffect> { new RunEffect(RunEffectKind.LoseRandomSkill, 1) }, 0 });
        }
        Assert.Equal(new[] { "sig_fire", "sig_bleed", "sig_radiation" }, RunSystem.Signatures(run));
    }

    [Fact]
    public void MinibossChoices_OfferAnUnownedSignature_AboutAQuarterOfTheTime_FromUnlockedSynergies()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        int offers = 0, total = 0;
        for (long seed = 1; seed <= 120; seed++)
        {
            var s = RunWith(e, "sig_fire", seed);
            var run = s.Run.Active!;
            run.PendingMythicDrafts = 1;
            var ctx = RunTestKit.Ctx(e, s);
            typeof(RunSystem).GetMethod("OpenMythicDraft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, new object[] { ctx, run });
            total++;
            var sigs = run.DraftOffer.Where(id => C.Skill(id)!.Signature).ToList();
            Assert.True(sigs.Count <= 1);
            Assert.Equal(C.RunTuning.MythicChoices, run.DraftOffer.Count);
            if (sigs.Count == 0) continue;
            offers++;
            Assert.Contains(sigs[0], new[] { "sig_radiation", "sig_bleed" }); // only unlocked (starter) synergies, never the owned one
        }
        double share = offers / (double)total;
        Assert.InRange(share, 0.12, 0.40);
    }

    [Fact]
    public void Resonance_OffersTwoSignaturesOrCoins_AndIsNotDrawnWhenNoneIsLeft()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var ev = C.RunEvents[RunSystem.ResonanceEvent];
        Assert.Equal(3, ev.Options.Count);
        Assert.Contains(C.RandomRunEvents, w => w.Id == RunSystem.ResonanceEvent && w.Weight < 1); // rare

        GameState Open(long seed)
        {
            var s = RunWith(e, "sig_fire", seed);
            var run = s.Run.Active!;
            run.Phase = RunPhase.Event; run.EventId = RunSystem.ResonanceEvent; run.EventTile = -1;
            run.SignatureChoice.Clear(); run.SignatureChoice.AddRange(new[] { "sig_bleed", "sig_radiation" });
            return s;
        }
        var a = Open(5);
        Assert.True(RunTestKit.Exec(e, ref a, new RunChooseEventOption { Index = 1 }).Ok);
        Assert.Equal(new[] { "sig_fire", "sig_radiation" }, RunSystem.Signatures(a.Run.Active!));
        var b = Open(6);
        long coins = b.Run.Active!.Coins;
        Assert.True(RunTestKit.Exec(e, ref b, new RunChooseEventOption { Index = 2 }).Ok);
        Assert.Equal(coins + C.RunTuning.ResonanceCoins, b.Run.Active!.Coins);
        Assert.Single(RunSystem.Signatures(b.Run.Active!));
        // an offer with one signature refuses the second option
        var d = Open(7);
        d.Run.Active!.SignatureChoice.RemoveAt(1);
        Assert.False(RunTestKit.Exec(e, ref d, new RunChooseEventOption { Index = 1 }).Ok);

        // with every unlocked signature owned, the event is never drawn
        var f = RunWith(e, "sig_fire", 8);
        Pick(e, ref f, "sig_bleed", true);
        Pick(e, ref f, "sig_radiation", true);
        Assert.Empty(RunSystem.FindableSignatures(C, f.Run.Active!));
        var pick = typeof(RunSystem).GetMethod("PickRandomEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        for (int i = 0; i < 300; i++) Assert.NotEqual(RunSystem.ResonanceEvent, (string)pick.Invoke(null, new object[] { C, f.Run.Active! })!);
    }

    [Fact]
    public void TheSynergySlot_CyclesBetweenTheSignatures()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_fire");
        Pick(e, ref s, "sig_bleed", true);
        var run = s.Run.Active!;
        // first draft (0 drafts so far): the slot cycles to Fire; after one draft gain: Bleed
        int fire = 0, bleed = 0;
        for (ulong seed = 1; seed <= 30; seed++) if (RunDraft.Generate(C, run, new Rng(seed)).Any(id => C.Skill(id)!.Family == SkillFamily.Fire)) fire++;
        run.Gains.Add(new RunGain("draft", "gen_power", 1));
        for (ulong seed = 1; seed <= 30; seed++) if (RunDraft.Generate(C, run, new Rng(seed)).Any(id => C.Skill(id)!.Family == SkillFamily.Bleed)) bleed++;
        Assert.Equal(30, fire);
        Assert.Equal(30, bleed);
    }

    [Fact]
    public void ARunSavedWithOneSignature_KeepsIt_AndCanFindMore()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_fire");
        var run = s.Run.Active!;
        run.SignatureIds.Clear(); // as saved before the redesign: only SignatureId
        Assert.Equal(new[] { "sig_fire" }, RunSystem.Signatures(run));
        Pick(e, ref s, "sig_bleed", true);
        Assert.Equal(new[] { "sig_fire", "sig_bleed" }, s.Run.Active!.SignatureIds);
        RunTestKit.AutoToEnd(e, ref s);
        Assert.True(s.Run.LastSummary!.Won);
    }

    [Fact]
    public void BothSignatures_ActInBattle()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunWith(e, "sig_fire");
        Pick(e, ref s, "sig_bleed", true);
        var run = s.Run.Active!;
        var setup = new BattleSetup();
        setup.Player.AddRange(BattleSimulator.BuildPlayerSide(run.Loadout, C, run.Skills, null, run.SkillLevels));
        setup.Enemy.Add(BattleLab.Bag());
        var r = BattleSimulator.Simulate(setup, C, new Rng(1));
        Assert.Contains(r.Log, x => x.SkillId == "sig_fire" && x.Status == StatusId.Heat);
        Assert.Contains(r.Log, x => x.SkillId == "sig_bleed" && x.Status == StatusId.Bleed);
    }
}
