namespace Inferior.Core.Time;

public readonly record struct CivilDate(int Year, int Month, int Day);

public static class GameCalendar
{
    public static GameDate FromCivilDate(int year, int month, int day) =>
        new(new DateOnly(year, month, day).DayNumber);

    public static CivilDate ToCivilDate(GameDate date)
    {
        DateOnly civil = DateOnly.FromDayNumber(date.AbsoluteDay);
        return new CivilDate(civil.Year, civil.Month, civil.Day);
    }

    public static bool IsLeapYear(int civilYear)
    {
        ValidateYear(civilYear);
        return civilYear % 4 == 0 && (civilYear % 100 != 0 || civilYear % 400 == 0);
    }

    public static int DaysInMonth(int civilYear, int month)
    {
        ValidateYear(civilYear);
        return month switch
        {
            2 => IsLeapYear(civilYear) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            _ => throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12."),
        };
    }

    public static bool IsValidCivilDate(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return false;

        return day >= 1 && day <= DaysInMonth(year, month);
    }

    public static GameDate AddDays(GameDate date, int days)
    {
        long result = (long)date.AbsoluteDay + days;
        if (result is < GameDate.MinAbsoluteDay || result > GameDate.MaxAbsoluteDay)
            throw new ArgumentOutOfRangeException(nameof(days), "Date arithmetic exceeded the supported calendar range.");

        return new GameDate((int)result);
    }

    public static GameDate AddMonths(GameDate date, int months)
    {
        DateOnly result = DateOnly.FromDayNumber(date.AbsoluteDay).AddMonths(months);
        return new GameDate(result.DayNumber);
    }

    public static GameDate AddYears(GameDate date, int years)
    {
        DateOnly result = DateOnly.FromDayNumber(date.AbsoluteDay).AddYears(years);
        return new GameDate(result.DayNumber);
    }

    public static int DaysBetween(GameDate from, GameDate to) => to.AbsoluteDay - from.AbsoluteDay;

    public static int DayOfYear(GameDate date) => DateOnly.FromDayNumber(date.AbsoluteDay).DayOfYear;

    public static DayOfWeek GetDayOfWeek(GameDate date) =>
        DateOnly.FromDayNumber(date.AbsoluteDay).DayOfWeek;

    private static void ValidateYear(int civilYear)
    {
        if (civilYear is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(civilYear), civilYear, "Civil year must be between 1 and 9999.");
    }
}
