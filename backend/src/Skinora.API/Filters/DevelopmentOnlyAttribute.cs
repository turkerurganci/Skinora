using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Skinora.API.Filters;

/// <summary>
/// T166 — marks a controller that exists only for development and tests
/// (today: <c>DiagnosticsController</c>, <c>/api/v1/_diag/*</c>). Outside the
/// <c>Development</c> environment <see cref="DevelopmentOnlyControllerConvention"/>
/// removes such a controller from the application model, so none of its routes
/// are registered and every request to them is a plain 404.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DevelopmentOnlyAttribute : Attribute;

/// <summary>
/// Removes <see cref="DevelopmentOnlyAttribute"/> controllers unless the host
/// environment is <c>Development</c>.
/// </summary>
/// <remarks>
/// <para>
/// Removal happens at model-build time rather than per request: a route that
/// was never registered cannot be reached by any header, method or auth trick,
/// and a forgotten check inside one action cannot reopen it. The rule is
/// "allow in Development only" (not "deny in Production") so a staging or
/// mis-spelled environment name stays closed — the safe default.
/// </para>
/// <para>
/// The <c>_diag</c> endpoints throw arbitrary exceptions and expose rate-limit
/// buckets; they were documented as "will be restricted in production (T06+)"
/// and never were (T166, F9 "küçük düzeltmeler").
/// </para>
/// </remarks>
public sealed class DevelopmentOnlyControllerConvention : IApplicationModelConvention
{
    private readonly bool _isDevelopment;

    public DevelopmentOnlyControllerConvention(bool isDevelopment)
    {
        _isDevelopment = isDevelopment;
    }

    public void Apply(ApplicationModel application)
    {
        if (_isDevelopment) return;

        var devOnly = application.Controllers
            .Where(c => c.ControllerType.IsDefined(typeof(DevelopmentOnlyAttribute), inherit: false))
            .ToList();

        foreach (var controller in devOnly)
        {
            application.Controllers.Remove(controller);
        }
    }
}
