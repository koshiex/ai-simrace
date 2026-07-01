using SimCoach.Adapters.ACC;
using SimCoach.Storage;

namespace SimCoach.App;

/// <summary>
/// Composition-edge bridge from the ACC-specific <see cref="AccCarCatalog"/> car→class map to the
/// sim-agnostic <see cref="ICarClassProvider"/> seam consumed by the live ambient state. App is the only
/// project allowed to reference the ACC adapter, so the wrapper lives here; another sim plugs in its own
/// provider the same way.
/// </summary>
internal sealed class AccCarClassProvider : ICarClassProvider
{
    public bool TryGetCarClass(string carId, out string carClass) =>
        AccCarCatalog.TryGetCarClass(carId, out carClass);
}
