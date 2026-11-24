using System.Text.Json;
using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using RogueStars.Rules.Systems;
using RogueStars.Rules.Tests.Run;

namespace RogueStars.Rules.Tests.Progression;

/// <summary>
/// Old saves (hero and mount kits): stars, awakening and levels are kept and the kits come from them; saved loadouts (an active
/// run's, arena snapshots) that still list the old ProgFamilySkill step ids and mount stats fight with the kit at that step.
/// </summary>
public class KitMigrationTests
{
    private static readonly ContentDb C = ContentDb.Default;

    private static CombatLoadout OldLoadout()
    {
        var lo = new CombatLoadout { DisplayName = "Old", HeroId = "hero_chef", MountId = "mount_frost_frigate" };
        lo.WormStats.Add(new StatMod(StatId.Hp, 4000)).Add(new StatMod(StatId.Atk, 120)).Add(new StatMod(StatId.Speed, 100));
        // a 7★ Chef (a1 → a2 → a3, b1) and a 5★ A3 Frost Frigate (w3, s2) as the old contributors listed them
        lo.MetaSkillIds.AddRange(new[] { "hero_chef_a3", "hero_chef_b1", "mount_frost_frigate_w3", "mount_frost_frigate_s2", "arm_tesla_whip_sk1" });
        lo.MountStats.Add(new StatMod(StatId.Atk, 90)).Add(new StatMod(StatId.Hp, 900)).Add(new StatMod(StatId.DamageReduction, 5));
        return lo;
    }

    [Fact]
    public void OldStepIds_ReadAsTheKitAtThatStep()
    {
        var ids = KitMigration.MetaSkillIds(C, OldLoadout());
        Assert.Equal(new[] { "arm_tesla_whip_sk1" }.Concat(HeroSystem.TraitSkills(C, "hero_chef", 7)).Concat(MountSystem.WeaponSkills(C, "mount_frost_frigate", 5, 3)), ids);
        Assert.Contains("hero_chef_c7", ids);                 // 7★: Second Course
        Assert.Contains("mount_frost_frigate_passive_4", ids); // A3
        // the highest old step wins (b2 = 10★; w4 = A5); old steps between kit steps land on the kit step below (w2 = 2★ → 1★)
        var top = new CombatLoadout(); top.MetaSkillIds.AddRange(new[] { "hero_star_knight_a1", "hero_star_knight_b2", "mount_scout_saucer_w4" });
        Assert.Equal(HeroSystem.TraitSkills(C, "hero_star_knight", 10).Concat(MountSystem.WeaponSkills(C, "mount_scout_saucer", 5, 5)), KitMigration.MetaSkillIds(C, top));
        var two = new CombatLoadout(); two.MetaSkillIds.Add("mount_magma_barge_w2");
        Assert.Equal(MountSystem.WeaponSkills(C, "mount_magma_barge", 1, 0), KitMigration.MetaSkillIds(C, two));
        // a new loadout is untouched
        var now = new CombatLoadout(); now.MetaSkillIds.AddRange(HeroSystem.TraitSkills(C, "hero_chef", 3));
        Assert.False(KitMigration.HasLegacy(C, now));
        Assert.Equal(now.MetaSkillIds, KitMigration.MetaSkillIds(C, now));
    }

    [Fact]
    public void Migrate_RewritesTheLoadout_AndDropsMountStats()
    {
        var lo = OldLoadout();
        Assert.True(KitMigration.HasLegacy(C, lo));
        KitMigration.Migrate(C, lo);
        Assert.False(KitMigration.HasLegacy(C, lo));
        Assert.All(lo.MetaSkillIds, id => Assert.NotNull(C.Skill(id)));
        Assert.Equal(0, lo.MountStats.Value(StatId.Atk));
        Assert.Equal(0, lo.MountStats.Value(StatId.Hp));
    }

    /// <summary>An arena snapshot from before the kits fights with the kits (both sides), the same either way.</summary>
    [Fact]
    public void AnOldArenaSnapshot_FightsWithTheKits()
    {
        var old = OldLoadout();
        var migrated = OldLoadout();
        KitMigration.Migrate(C, migrated);
        var a = BattleSimulator.PlayerVsSnapshot(C, migrated, old, new Rng(9UL));
        var b = BattleSimulator.PlayerVsSnapshot(C, migrated, migrated, new Rng(9UL));
        Assert.Equal(a.Log.Count, b.Log.Count);
        Assert.Equal(a.Winner, b.Winner);
        Assert.Contains(a.Log, e => e.Action == BattleAction.Volley);                          // the old mount fires its kit
        Assert.Contains(a.Log, e => e.SkillId == "mount_frost_frigate_passive_4");
        Assert.DoesNotContain(a.Fighters, f => f.Role == CombatRole.Worm && f.StartShield > 0); // no mount-HP shield any more
    }

    /// <summary>A save from before the kits (hero 7★ Lv 40, mount 5★ A3 Lv 55, an active run holding the old loadout) loads and plays.</summary>
    [Fact]
    public void AnOldSave_LoadsKeepsStarsAndLevels_AndItsRunPlays()
    {
        var e = RunTestKit.Engine(RunTestKit.Strong());
        var s = RunTestKit.StartRun(e);
        s.Heroes.Owned["hero_chef"] = new HeroEntry { Stars = 7, Level = 40 };
        s.Heroes.Equipped = "hero_chef";
        s.Mounts.Owned["mount_frost_frigate"] = new MountEntry { Stars = 5, Awaken = 3, Level = 55 };
        s.Mounts.Active = "mount_frost_frigate";
        var old = OldLoadout();
        s.Run.Active!.Loadout.MetaSkillIds.AddRange(old.MetaSkillIds);
        s.Run.Active.Loadout.MountId = old.MountId;
        s.Run.Active.Loadout.MountStats.AddBlock(old.MountStats);
        string json = JsonSerializer.Serialize(s, TestHelpers.Json);
        var loaded = JsonSerializer.Deserialize<GameState>(json, TestHelpers.Json)!;
        Assert.Equal(7, loaded.Heroes.Owned["hero_chef"].Stars);
        Assert.Equal(40, loaded.Heroes.Owned["hero_chef"].Level);
        Assert.Equal((5, 3, 55), (loaded.Mounts.Owned["mount_frost_frigate"].Stars, loaded.Mounts.Owned["mount_frost_frigate"].Awaken, loaded.Mounts.Owned["mount_frost_frigate"].Level));
        // the kits come from the kept stars (the progression contributors build the loadout from these)
        Assert.Contains("hero_chef_c7", HeroSystem.TraitSkills(C, "hero_chef", loaded.Heroes.Owned["hero_chef"].Stars));
        var m = loaded.Mounts.Owned["mount_frost_frigate"];
        Assert.Contains("mount_frost_frigate_active_4", MountSystem.WeaponSkills(C, "mount_frost_frigate", m.Stars, m.Awaken));
        Assert.Equal(20 + 54, MountSystem.MountStats(C, "mount_frost_frigate", m).Value(StatId.MountPower), 6); // 2★, 4★, A1, A2 × 5% + 54 levels × 1%
        RunTestKit.AutoToEnd(e, ref loaded);
        Assert.NotNull(loaded.Run.LastSummary);
    }
}
