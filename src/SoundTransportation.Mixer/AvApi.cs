using System.Security.Cryptography;
using System.Text;

namespace SoundTransportation.Mixer;

public static class AvApi
{
    public static bool Enabled(IConfiguration configuration) => configuration.GetValue("AvIntegration:Enabled", false);

    public static void MapAvApi(this WebApplication app)
    {
        // Protect only new routes; standalone legacy mode is preserved.
        app.Use(async (context, next) =>
        {
            var av = context.Request.Path.StartsWithSegments("/api/av/v1");
            if (av)
            {
                if (!Enabled(app.Configuration)) { await Reject(context, 503, "INTEGRATION_DISABLED"); return; }
                var expected = Environment.GetEnvironmentVariable("MIRRA_AV_TOKEN");
                var supplied = context.Request.Headers.Authorization.ToString();
                if (string.IsNullOrWhiteSpace(expected) || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes("Bearer " + expected), Encoding.UTF8.GetBytes(supplied)))
                { await Reject(context, 401, "UNAUTHORIZED"); return; }
            }
            else if (Enabled(app.Configuration) && context.Request.Path.StartsWithSegments("/api") &&
                context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
            { await Reject(context, 409, "LEGACY_CONTROL_DISABLED"); return; }
            try { await next(context); }
            catch (AvException e) { await Reject(context, e.Status, e.Code); }
            catch (BadHttpRequestException) when (av) { await Reject(context, 400, "INVALID_JSON"); }
        });
        var routes = app.MapGroup("/api/av/v1");
        routes.MapGet("/capabilities", (AvCrossfadeEngine engine) => engine.Capabilities());
        routes.MapGet("/clock", (AvCrossfadeEngine engine) => engine.Clock());
        routes.MapGet("/tracks", (AvCrossfadeEngine engine) => engine.Tracks());
        routes.MapPost("/crossfades", (AvCrossfadeRequest request, AvCrossfadeEngine engine) =>
            Results.Json(engine.Schedule(request), statusCode: 202));
        routes.MapGet("/crossfades/{requestId:guid}", (Guid requestId, Guid engineInstanceId, AvCrossfadeEngine engine) =>
            engine.Get(requestId, engineInstanceId));
        routes.MapPost("/crossfades/{requestId:guid}/cancel", (Guid requestId, AvCancelRequest request, AvCrossfadeEngine engine) =>
            engine.Cancel(requestId, request.EngineInstanceId));
    }

    private static async Task Reject(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { protocolVersion = "1.0", code, message = code });
    }
}
