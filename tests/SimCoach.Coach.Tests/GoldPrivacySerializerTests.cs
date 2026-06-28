using System.Text.Json;
using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldPrivacySerializerTests
{
    private static readonly string[] _forbiddenKeys =
    [
        "car_id", "session_id", "world_pos", "tyre_temp_c", "tyre_pressure_kpa", "tyre_wear_pct",
        "brake_temp_c", "wheel_slip", "wheel_load_n", "slip_ratio", "fuel_l", "engine_map",
    ];

    private static IEnumerable<string> Keys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (string nested in Keys(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string nested in Keys(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    [Fact]
    public void Session_gold_carries_no_forbidden_keys_and_no_raw_car_or_session_id()
    {
        string json = GoldSerializer.Serialize(GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()));

        using var doc = JsonDocument.Parse(json);
        IReadOnlyList<string> keys = [.. Keys(doc.RootElement)];

        keys.Should().NotIntersectWith(_forbiddenKeys);
        keys.Should().Contain("car_class");
        json.Should().NotContain("audi_r8_lms_evo_ii");
        json.Should().NotContain("sess-123");
    }

    [Theory]
    [InlineData("corner")]
    [InlineData("sector")]
    [InlineData("lap")]
    public void Real_time_gold_never_carries_a_car_id_key(string cadence)
    {
        GoldArtifactBuilder builder = GoldTestData.Builder();
        string json = cadence switch
        {
            "corner" => GoldSerializer.Serialize(builder.BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx())),
            "sector" => GoldSerializer.Serialize(builder.BuildSector(GoldTestData.Sector(), GoldTestData.Ctx())),
            _ => GoldSerializer.Serialize(builder.BuildLap(GoldTestData.Lap(), GoldTestData.Ctx())),
        };

        using var doc = JsonDocument.Parse(json);
        IReadOnlyList<string> keys = [.. Keys(doc.RootElement)];

        keys.Should().NotContain("car_id");
        keys.Should().Contain("car_class");
    }
}
