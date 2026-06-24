using Google.Protobuf;
using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>Discriminates the four compute domain-event payloads carried by a <see cref="DomainEvent"/>.</summary>
public enum DomainEventKind
{
    Corner,
    Sector,
    Lap,
    Session,
}

/// <summary>
/// In-process envelope for the four compute domain events (<see cref="CornerEvent"/>,
/// <see cref="SectorEvent"/>, <see cref="LapEvent"/>, <see cref="SessionEvent"/>). The proto schema
/// has no oneof wrapper, so this plain record gives <see cref="DomainEventFanOut"/> a single ordered
/// channel: consumers switch on <see cref="Kind"/> (or pattern-match <see cref="Payload"/>) and keep
/// the causal order (corners → sector cross → lap finish → session end).
/// </summary>
public sealed record DomainEvent(DomainEventKind Kind, IMessage Payload)
{
    public static DomainEvent Corner(CornerEvent e) => new(DomainEventKind.Corner, e);

    public static DomainEvent Sector(SectorEvent e) => new(DomainEventKind.Sector, e);

    public static DomainEvent Lap(LapEvent e) => new(DomainEventKind.Lap, e);

    public static DomainEvent Session(SessionEvent e) => new(DomainEventKind.Session, e);
}
