using RogueStars.Rules.Content;
using RogueStars.Rules.Core;
using Xunit.Abstractions;

namespace RogueStars.Rules.Tests.Run;

public class SkillTextTests(ITestOutputHelper output)
{
    [Fact]
    public void EverySkill_HasEffectText()
    {
        var c = new ContentDb();
        foreach (var s in c.Skills.Values)
        {
            var text = SkillText.Full(s);
            output.WriteLine($"{s.Name} [{s.Id}]: {text}");
            if (s.Stats.Count == 0 && s.Triggers.Count == 0) continue;
            Assert.False(string.IsNullOrWhiteSpace(text), s.Id);
        }
    }

    [Fact]
    public void Text_ReadsNaturally()
    {
        var s = new SkillDef { Id = "x", Name = "X" };
        s.Stats.Add(new StatMod(StatId.CritChance, 8));
        s.Stats.Add(new StatMod(StatId.Atk, 0, 12));
        s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnRageAttack, Effect = EffectKind.ApplyStatus, Status = StatusId.Bleed, Stacks = 2, Chance = 100 });
        s.Triggers.Add(new SkillTrigger { When = TriggerKind.OnHit, Effect = EffectKind.Damage, Value = 30, Chance = 25, Target = TargetKind.AllEnemies });
        s.Triggers.Add(new SkillTrigger { When = TriggerKind.Passive, Effect = EffectKind.BuffStat, Param = "cond:vs_status:Burn", Value = 15 });
        Assert.Equal("+8% crit chance. +12% ATK. On rage attack: apply Bleed ×2 to the enemy. On hit: 25% chance to deal 30% ATK damage to all enemies. +15% damage against enemies with Burn.", SkillText.Full(s));
    }
}
