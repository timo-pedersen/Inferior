using System.Text.Json;
using Inferior.Core.Time;
using Xunit;

namespace Inferior.Game.Test;

public class GameCalendarTests
{
    [Theory]
    [InlineData(1, 1, 1, 0)]
    [InlineData(1, 1, 2, 1)]
    [InlineData(6864, 7, 19, 2506859)]
    public void CivilAndAbsoluteDayRoundTrip(int year, int month, int day, int expectedAbsoluteDay)
    {
        GameDate date = GameCalendar.FromCivilDate(year, month, day);

        Assert.Equal(expectedAbsoluteDay, date.AbsoluteDay);
        Assert.Equal(new CivilDate(year, month, day), GameCalendar.ToCivilDate(date));
    }

    [Theory]
    [InlineData(3048, true)]
    [InlineData(3100, false)]
    [InlineData(3200, true)]
    [InlineData(5524, true)]
    [InlineData(5600, true)]
    [InlineData(5772, true)]
    [InlineData(5800, false)]
    [InlineData(6000, true)]
    [InlineData(6416, true)]
    [InlineData(6500, false)]
    [InlineData(6540, true)]
    [InlineData(6600, false)]
    [InlineData(6800, true)]
    public void LeapYearsFollowContinuousGregorianCalendar(int civilYear, bool expected) =>
        Assert.Equal(expected, GameCalendar.IsLeapYear(civilYear));

    [Fact]
    public void FebruaryValidationDistinguishesLeapYears()
    {
        Assert.True(GameCalendar.IsValidCivilDate(3048, 2, 29));
        Assert.False(GameCalendar.IsValidCivilDate(3047, 2, 29));
        Assert.True(GameCalendar.IsValidCivilDate(3100, 2, 28));
        Assert.False(GameCalendar.IsValidCivilDate(3100, 2, 29));
        Assert.True(GameCalendar.IsValidCivilDate(3200, 2, 29));
        Assert.Equal(29, GameCalendar.DaysInMonth(3200, 2));
        Assert.Equal(28, GameCalendar.DaysInMonth(3100, 2));
    }

    [Theory]
    [InlineData(3046, 12, 31, GalacticEraCode.BE, 1)]
    [InlineData(3047, 1, 1, GalacticEraCode.E1, 1)]
    [InlineData(5521, 12, 31, GalacticEraCode.E1, 2475)]
    [InlineData(5522, 1, 1, GalacticEraCode.O1, 1)]
    [InlineData(5771, 12, 31, GalacticEraCode.O1, 250)]
    [InlineData(5772, 1, 1, GalacticEraCode.E2, 1)]
    [InlineData(6412, 12, 31, GalacticEraCode.E2, 641)]
    [InlineData(6413, 1, 1, GalacticEraCode.O2, 1)]
    [InlineData(6538, 12, 31, GalacticEraCode.O2, 126)]
    [InlineData(6539, 1, 1, GalacticEraCode.E3, 1)]
    public void EraBoundariesMapExactly(
        int civilYear,
        int month,
        int day,
        GalacticEraCode expectedEra,
        int expectedEraYear)
    {
        GameDate gameDate = GameCalendar.FromCivilDate(civilYear, month, day);
        var expected = new GalacticEraDate(expectedEra, expectedEraYear, month, day);

        Assert.Equal(expected, GalacticEraTimeline.ToEraDate(gameDate));
        Assert.Equal(gameDate, GalacticEraTimeline.FromEraDate(expected));
    }

    [Theory]
    [InlineData(3046, 6, 6, GalacticEraCode.BE, 1)]
    [InlineData(5521, 8, 16, GalacticEraCode.E1, 2475)]
    [InlineData(5771, 5, 26, GalacticEraCode.O1, 250)]
    [InlineData(6412, 3, 3, GalacticEraCode.E2, 641)]
    [InlineData(6538, 11, 3, GalacticEraCode.O2, 126)]
    [InlineData(6864, 7, 19, GalacticEraCode.E3, 326)]
    public void LoreDatesRoundTrip(
        int civilYear,
        int month,
        int day,
        GalacticEraCode era,
        int eraYear)
    {
        GameDate gameDate = GameCalendar.FromCivilDate(civilYear, month, day);
        var eraDate = new GalacticEraDate(era, eraYear, month, day);

        Assert.Equal(eraDate, GalacticEraTimeline.ToEraDate(gameDate));
        Assert.Equal(gameDate, GalacticEraTimeline.FromEraDate(eraDate));
    }

    [Theory]
    [InlineData(GalacticEraCode.E1, 0, 1, 1)]
    [InlineData(GalacticEraCode.E1, 2476, 1, 1)]
    [InlineData(GalacticEraCode.O1, 251, 1, 1)]
    [InlineData(GalacticEraCode.E2, 642, 1, 1)]
    [InlineData(GalacticEraCode.O2, 127, 1, 1)]
    [InlineData(GalacticEraCode.BE, 3047, 1, 1)]
    public void EraDatesOutsideTheirActualRangeAreRejected(
        GalacticEraCode era,
        int year,
        int month,
        int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GalacticEraTimeline.FromEraDate(new GalacticEraDate(era, year, month, day)));
    }

    [Fact]
    public void DateArithmeticClampsEndOfMonth()
    {
        AssertCivil(GameCalendar.AddMonths(GameCalendar.FromCivilDate(2023, 1, 31), 1), 2023, 2, 28);
        AssertCivil(GameCalendar.AddMonths(GameCalendar.FromCivilDate(2024, 1, 31), 1), 2024, 2, 29);
        AssertCivil(GameCalendar.AddYears(GameCalendar.FromCivilDate(2024, 2, 29), 1), 2025, 2, 28);
        AssertCivil(GameCalendar.AddMonths(GameCalendar.FromCivilDate(2024, 3, 31), -1), 2024, 2, 29);
    }

    [Fact]
    public void DayArithmeticCrossesMonthsYearsAndLeapDay()
    {
        AssertCivil(GameCalendar.FromCivilDate(2024, 2, 28) + 1, 2024, 2, 29);
        AssertCivil(GameCalendar.FromCivilDate(2024, 2, 29) + 1, 2024, 3, 1);
        AssertCivil(GameCalendar.FromCivilDate(2024, 1, 1) - 1, 2023, 12, 31);
    }

    [Theory]
    [InlineData(3047)]
    [InlineData(5522)]
    [InlineData(5772)]
    [InlineData(6413)]
    [InlineData(6539)]
    public void ArithmeticIsContinuousAcrossEraBoundaries(int boundaryYear)
    {
        GameDate boundary = GameCalendar.FromCivilDate(boundaryYear, 1, 1);
        AssertCivil(boundary - 1, boundaryYear - 1, 12, 31);
        Assert.Equal(1, GameCalendar.DaysBetween(boundary - 1, boundary));
    }

    [Fact]
    public void DaysBetweenHasDocumentedDirectionAndSymmetry()
    {
        GameDate earlier = GameCalendar.FromCivilDate(2024, 1, 1);
        GameDate later = GameCalendar.FromCivilDate(2024, 2, 1);

        Assert.Equal(31, GameCalendar.DaysBetween(earlier, later));
        Assert.Equal(-31, GameCalendar.DaysBetween(later, earlier));
        Assert.Equal(31, later - earlier);
        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.Equal(0, earlier.CompareTo(earlier));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3047, 1, 1)]
    [InlineData(6864, 7, 19)]
    public void WeekdayMatchesProlepticGregorianDateOnly(int year, int month, int day)
    {
        GameDate gameDate = GameCalendar.FromCivilDate(year, month, day);
        Assert.Equal(new DateOnly(year, month, day).DayOfWeek, GameCalendar.GetDayOfWeek(gameDate));
    }

    [Fact]
    public void AbsoluteDayZeroIsMonday()
    {
        Assert.Equal(DayOfWeek.Monday, GameCalendar.GetDayOfWeek(new GameDate(0)));
        Assert.Equal(1, GameCalendar.DayOfYear(new GameDate(0)));
    }

    [Theory]
    [InlineData(GalacticEraCode.BE, 1, 6, 6, "BE.1-06-06")]
    [InlineData(GalacticEraCode.E1, 1, 1, 1, "E1.1-01-01")]
    [InlineData(GalacticEraCode.O1, 250, 5, 26, "O1.250-05-26")]
    [InlineData(GalacticEraCode.E2, 641, 3, 3, "E2.641-03-03")]
    [InlineData(GalacticEraCode.O2, 126, 11, 3, "O2.126-11-03")]
    [InlineData(GalacticEraCode.E3, 326, 7, 19, "E3.326-07-19")]
    public void CanonicalFormatAndParseRoundTrip(
        GalacticEraCode era,
        int year,
        int month,
        int day,
        string expected)
    {
        var eraDate = new GalacticEraDate(era, year, month, day);

        Assert.Equal(expected, GalacticEraTimeline.Format(eraDate));
        Assert.True(GalacticEraTimeline.TryParse(expected, out GalacticEraDate parsed));
        Assert.Equal(eraDate, parsed);
        Assert.Equal(expected, GalacticEraTimeline.Format(GalacticEraTimeline.FromEraDate(eraDate)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("e3.326-07-19")]
    [InlineData("E3.0326-07-19")]
    [InlineData("E3.0-07-19")]
    [InlineData("E3.326-7-19")]
    [InlineData("E3.326-07-9")]
    [InlineData("E3-326-07-19")]
    [InlineData("E4.1-01-01")]
    [InlineData("E1.2476-01-01")]
    [InlineData("O1.251-01-01")]
    [InlineData("E3.326-02-30")]
    [InlineData(" E3.326-07-19")]
    [InlineData("E3.326-07-19 ")]
    public void StrictParserRejectsNonCanonicalOrInvalidValues(string text) =>
        Assert.False(GalacticEraTimeline.TryParse(text, out _));

    [Fact]
    public void InitialGameDateIsFixedAndNotWallClockDerived()
    {
        AssertCivil(GalacticEraTimeline.InitialGameDate, 6864, 7, 19);
        Assert.Equal("E3.326-07-19", GalacticEraTimeline.Format(GalacticEraTimeline.InitialGameDate));
    }

    [Fact]
    public void GameDatePersistsAsNumericAbsoluteDay()
    {
        GameDate original = GalacticEraTimeline.InitialGameDate;

        string json = JsonSerializer.Serialize(original);
        GameDate roundTrip = JsonSerializer.Deserialize<GameDate>(json);

        Assert.Equal("2506859", json);
        Assert.Equal(original, roundTrip);
        Assert.DoesNotContain("E3", json);
        Assert.DoesNotContain("Year", json);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3652059")]
    [InlineData("\"2506859\"")]
    [InlineData("{\"absoluteDay\":2506859}")]
    public void InvalidPersistedGameDatesFailClearly(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameDate>(json));

    [Fact]
    public void UnsupportedDateArithmeticIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(GameDate.MaxAbsoluteDay + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(0) - 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(GameDate.MaxAbsoluteDay) + 1);
    }

    private static void AssertCivil(GameDate actual, int year, int month, int day) =>
        Assert.Equal(new CivilDate(year, month, day), GameCalendar.ToCivilDate(actual));
}
