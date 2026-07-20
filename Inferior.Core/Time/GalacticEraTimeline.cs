using System.Globalization;
using System.Text.RegularExpressions;

namespace Inferior.Core.Time;

public enum GalacticEraCode
{
    BE,
    E1,
    O1,
    E2,
    O2,
    E3,
}

public readonly record struct GalacticEraDate(
    GalacticEraCode Era,
    int Year,
    int Month,
    int Day);

public static class GalacticEraTimeline
{
    private readonly record struct EraBoundary(GalacticEraCode Era, int CivilStartYear);

    private static readonly EraBoundary[] Boundaries =
    [
        new(GalacticEraCode.E1, 3047),
        new(GalacticEraCode.O1, 5522),
        new(GalacticEraCode.E2, 5772),
        new(GalacticEraCode.O2, 6413),
        new(GalacticEraCode.E3, 6539),
    ];

    private static readonly Regex FullDatePattern = new(
        @"^(BE|E1|O1|E2|O2|E3)\.([1-9][0-9]*)-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static GameDate InitialGameDate { get; } = GameCalendar.FromCivilDate(6864, 7, 19);

    public static GalacticEraDate ToEraDate(GameDate date)
    {
        CivilDate civil = GameCalendar.ToCivilDate(date);

        for (int i = Boundaries.Length - 1; i >= 0; i--)
        {
            EraBoundary boundary = Boundaries[i];
            if (civil.Year >= boundary.CivilStartYear)
            {
                return new GalacticEraDate(
                    boundary.Era,
                    civil.Year - boundary.CivilStartYear + 1,
                    civil.Month,
                    civil.Day);
            }
        }

        return new GalacticEraDate(
            GalacticEraCode.BE,
            3047 - civil.Year,
            civil.Month,
            civil.Day);
    }

    public static GameDate FromEraDate(GalacticEraDate date)
    {
        if (date.Year < 1)
            throw new ArgumentOutOfRangeException(nameof(date), "Galactic Era year must be at least 1.");

        long civilYearValue;
        if (date.Era == GalacticEraCode.BE)
        {
            civilYearValue = 3047L - date.Year;
        }
        else
        {
            EraBoundary boundary = FindBoundary(date.Era);
            civilYearValue = (long)boundary.CivilStartYear + date.Year - 1;
        }

        if (civilYearValue is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(date), date, "Galactic Era date is outside the supported civil calendar.");

        int civilYear = (int)civilYearValue;
        GameDate result;
        try
        {
            result = GameCalendar.FromCivilDate(civilYear, date.Month, date.Day);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "Galactic Era date is invalid or outside the supported civil calendar.");
        }

        if (ToEraDate(result) != date)
            throw new ArgumentOutOfRangeException(nameof(date), date, "Date does not belong to the requested Galactic Era.");

        return result;
    }

    public static string Format(GameDate date) => Format(ToEraDate(date));

    public static string Format(GalacticEraDate date)
    {
        _ = FromEraDate(date);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{date.Era}.{date.Year}-{date.Month:00}-{date.Day:00}");
    }

    public static bool TryParse(string? text, out GalacticEraDate eraDate)
    {
        eraDate = default;
        if (text is null)
            return false;

        Match match = FullDatePattern.Match(text);
        if (!match.Success ||
            !Enum.TryParse(match.Groups[1].Value, ignoreCase: false, out GalacticEraCode era) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int month) ||
            !int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            return false;
        }

        var candidate = new GalacticEraDate(era, year, month, day);
        try
        {
            _ = FromEraDate(candidate);
            eraDate = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static EraBoundary FindBoundary(GalacticEraCode era)
    {
        foreach (EraBoundary boundary in Boundaries)
        {
            if (boundary.Era == era)
                return boundary;
        }

        throw new ArgumentOutOfRangeException(nameof(era), era, "Unknown Galactic Era code.");
    }
}
