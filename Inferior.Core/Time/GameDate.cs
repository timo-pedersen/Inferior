using System.Text.Json.Serialization;

namespace Inferior.Core.Time;

[JsonConverter(typeof(GameDateJsonConverter))]
public readonly record struct GameDate : IComparable<GameDate>
{
    public const int MinAbsoluteDay = 0;
    public static readonly int MaxAbsoluteDay = DateOnly.MaxValue.DayNumber;

    public int AbsoluteDay { get; }

    public GameDate(int absoluteDay)
    {
        if (absoluteDay is < MinAbsoluteDay || absoluteDay > MaxAbsoluteDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteDay),
                absoluteDay,
                $"Absolute day must be between {MinAbsoluteDay} and {MaxAbsoluteDay}.");
        }

        AbsoluteDay = absoluteDay;
    }

    public int CompareTo(GameDate other) => AbsoluteDay.CompareTo(other.AbsoluteDay);

    public static GameDate operator +(GameDate date, int days) => GameCalendar.AddDays(date, days);

    public static GameDate operator -(GameDate date, int days) =>
        GameCalendar.AddDays(date, checked(-days));

    public static int operator -(GameDate left, GameDate right) =>
        GameCalendar.DaysBetween(right, left);
}
