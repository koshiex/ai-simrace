using System.Globalization;

namespace SimCoach.Coach;

/// <summary>
/// Renders a distance in metres as an intuitive car-length count with the correct Russian plural noun
/// ("1 корпус", "2 корпуса", "5 корпусов") — the one magnitude the voice path is allowed to speak (raw
/// metres/km/h/ms are not). The divisor (a GT3 car length) is config-driven (<see cref="CoachOptions.CarLengthMeters"/>);
/// the plural words come from the resx per the "RU user-facing → resx" rule. A sub-half-car distance rounds up
/// to one length rather than "0 корпусов" — the tip only fires past a real gap, so the smallest speakable
/// correction is a single car.
/// </summary>
internal static class CarLengthGloss
{
    public static string ToRu(double meters, double carLengthMeters)
    {
        if (carLengthMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carLengthMeters), carLengthMeters, "Car length must be positive.");
        }

        long lengths = (long)Math.Round(Math.Abs(meters) / carLengthMeters, MidpointRounding.AwayFromZero);
        if (lengths < 1)
        {
            lengths = 1;
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"{lengths} {CoachStrings.Get(PluralKey(lengths))}");
    }

    // Russian count agreement: 1/21/31… → one; 2–4/22–24… → few; 0/5–20/11–14… → many.
    private static string PluralKey(long n)
    {
        long mod100 = n % 100;
        long mod10 = n % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return "CarLength_Many";
        }

        return mod10 switch
        {
            1 => "CarLength_One",
            >= 2 and <= 4 => "CarLength_Few",
            _ => "CarLength_Many",
        };
    }
}
