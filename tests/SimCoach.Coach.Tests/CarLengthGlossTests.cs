using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CarLengthGlossTests
{
    // carLengthMeters = 1.0 so metres == car lengths, isolating the Russian count agreement from the division.
    [Theory]
    [InlineData(1.0, "1 корпус")]      // one: n%10==1, n%100!=11
    [InlineData(2.0, "2 корпуса")]     // few: n%10 in 2..4
    [InlineData(4.0, "4 корпуса")]
    [InlineData(5.0, "5 корпусов")]    // many: n%10 in 5..9
    [InlineData(11.0, "11 корпусов")]  // many: the 11–14 mod100 exception (mod10==1 would wrongly say "one")
    [InlineData(14.0, "14 корпусов")]  // many: upper edge of the 11–14 exception
    [InlineData(21.0, "21 корпус")]    // one: mod10==1, mod100==21 (outside 11–14)
    [InlineData(22.0, "22 корпуса")]   // few: mod10==2, mod100==22
    public void ToRu_agrees_the_russian_plural_with_the_count(double meters, string expected)
    {
        CarLengthGloss.ToRu(meters, carLengthMeters: 1.0).Should().Be(expected);
    }

    [Fact]
    public void ToRu_rounds_a_sub_half_car_distance_up_to_one_length()
    {
        // A tip only fires past a real gap, so the smallest speakable correction is a single car — never "0 корпусов".
        CarLengthGloss.ToRu(meters: 1.0, carLengthMeters: 4.6).Should().Be("1 корпус");
    }

    [Fact]
    public void ToRu_drops_the_sign_before_counting()
    {
        CarLengthGloss.ToRu(meters: -9.2, carLengthMeters: 4.6).Should().Be("2 корпуса");
    }
}
