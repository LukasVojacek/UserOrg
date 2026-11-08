namespace UserOrg.Gateway.Features.RouteExistence;

/// <summary>
/// Holds the route surface the gateway currently answers from. Until the first successful load it is
/// <see cref="RouteSurface.Empty"/>, which judges nothing.
/// </summary>
internal sealed class RouteSurfaceProvider
{
    private volatile RouteSurface _current = RouteSurface.Empty;

    public RouteSurface Current => _current;

    // invariant: RouteSurfaceLoader only ever publishes a surface built from every document ApiDocuments.Published lists at once,
    // so a request never sees a half-loaded surface that would report live routes as non-existent.
    public void Publish(RouteSurface surface) => _current = surface;
}