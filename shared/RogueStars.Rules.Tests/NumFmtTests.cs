using RogueStars.Rules.Core;
using Xunit;

namespace RogueStars.Rules.Tests
{
    /// <summary>Owner rule G3: whole numbers by default; one decimal only for small rates and small abbreviated amounts.</summary>
    public class NumFmtTests
    {
        [Theory]
        [InlineData(12.6, "13")]
        [InlineData(-3.4, "-3")]
        [InlineData(2.5, "3")]
        public void Whole_Rounds(double v, string expected) => Assert.Equal(expected, NumFmt.Whole(v));

        [Theory]
        [InlineData(1.5, "1.5")]
        [InlineData(2.0, "2")]
        [InlineData(9.94, "9.9")]
        [InlineData(15.6, "16")]
        [InlineData(62.4, "62")]
        [InlineData(11.2, "11")]
        public void OneDecimal_OnlyBelowTen_NoTrailingZero(double v, string expected) => Assert.Equal(expected, NumFmt.OneDecimal(v));

        [Theory]
        [InlineData(4212, "4212")]
        [InlineData(20300, "20K")]
        [InlineData(61400, "61K")]
        [InlineData(12600, "12K")]
        [InlineData(9_500_000, "9.5M")]
        [InlineData(2_000_000, "2M")]
        [InlineData(25_000_000, "25M")]
        public void Abbrev_WholeAboveTenScaled(double v, string expected) => Assert.Equal(expected, NumFmt.Abbrev(v));

        [Fact]
        public void SkillText_NoDecimalsAtTenOrMore()
        {
            var c = Content.ContentDb.Default;
            foreach (var s in c.Skills.Values)
            {
                var text = SkillText.Full(s) ?? "";
                Assert.DoesNotMatch(@"(?<![\d.])[1-9]\d+\.\d", text);
            }
        }
    }
}
