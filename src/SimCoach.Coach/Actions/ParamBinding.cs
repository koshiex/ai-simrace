namespace SimCoach.Coach.Actions;

/// <summary>
/// Binds a template placeholder <c>{Name}</c> to a Gold field <see cref="From"/>, optionally transformed and
/// suffixed with a <see cref="Unit"/> (e.g. <c>"м"</c>). The rendered value (incl. sign + unit) becomes the
/// tip's <c>RenderedParam</c> chip.
/// </summary>
public sealed record ParamBinding(string Name, string From, ParamTransform Transform, string? Unit);
