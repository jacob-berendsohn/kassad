using Kassad.Policies;
using Kassad.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kassad.AspNetCore.Tests;

/// <summary>
/// Hosts <see cref="KassadInboundMiddleware"/> in a <see cref="Microsoft.AspNetCore.TestHost.TestServer"/> over a
/// <see cref="FakeDecisionModel"/>, followed by a terminal endpoint that echoes the request body and records what it
/// saw in <see cref="Probe"/>. Every test builds its own factory with the options it needs and disposes it afterwards.
/// </summary>
/// <remarks>
/// There is no application entry point to discover, so <see cref="CreateHostBuilder"/> starts from an empty
/// <see cref="HostBuilder"/> and <see cref="ConfigureWebHost"/> registers the services and the pipeline itself, the
/// way a <c>Program.cs</c> would. The type argument only tells the base class which assembly to inspect: this one,
/// whose entry point Microsoft.NET.Test.Sdk generates. With no content-root manifest entry for that assembly, the
/// base class falls back to a directory next to the solution file that does not exist; <see cref="ConfigureWebHost"/>
/// replaces it with the test output directory before the host is built.
/// </remarks>
internal sealed class KassadWebApplicationFactory : WebApplicationFactory<KassadWebApplicationFactory>
{
    private readonly PolicySet _policies;
    private readonly Action<KassadOptions>? _configure;

    /// <summary>Create a factory for one pipeline configuration.</summary>
    /// <param name="model">The decision model the engine calls. Its <see cref="FakeDecisionModel.Requests"/> show whether the engine ran.</param>
    /// <param name="configure">Applied through <c>AddKassadAspNetCore</c>; <c>null</c> keeps the <see cref="KassadOptions"/> defaults.</param>
    /// <param name="policies">Defaults to <see cref="TestPolicies.Injection"/> alone: fail_closed, Flag 0.4 / Review 0.6 / Block 0.85.</param>
    public KassadWebApplicationFactory(FakeDecisionModel model, Action<KassadOptions>? configure = null, PolicySet? policies = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _configure = configure;
        _policies = policies ?? PolicySet.FromPolicies([TestPolicies.Injection()]);
    }

    /// <summary>The decision model behind the engine.</summary>
    public FakeDecisionModel Model { get; }

    /// <summary>What the pipeline observed on the most recent request.</summary>
    public EndpointProbe Probe { get; } = new();

    protected override IHostBuilder CreateHostBuilder() => new HostBuilder();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IDecisionModel>(Model);
            services.AddKassad(_policies);
            services.AddKassadAspNetCore(_configure);
        });

        builder.Configure(app =>
        {
            app.Use(async (context, next) =>
            {
                Probe.Begin(context.TraceIdentifier);
                await next(context);
            });

            app.UseKassadInbound();

            app.Run(async context =>
            {
                using var reader = new StreamReader(context.Request.Body);
                string body = await reader.ReadToEndAsync(context.RequestAborted);
                Probe.MarkReached(context.GetKassadInboundResult(), body);
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(body, context.RequestAborted);
            });
        });
    }
}

/// <summary>Records, for the most recent request, what happened on either side of the middleware.</summary>
internal sealed class EndpointProbe
{
    /// <summary>The request's <see cref="HttpContext.TraceIdentifier"/>, captured ahead of the middleware.</summary>
    public string? TraceIdentifier { get; private set; }

    /// <summary>True when the terminal endpoint ran, i.e. the middleware did not short-circuit the request.</summary>
    public bool EndpointReached { get; private set; }

    /// <summary>What <c>GetKassadInboundResult()</c> returned inside the endpoint.</summary>
    public StageResult? InboundResult { get; private set; }

    /// <summary>The request body as the endpoint read it, after the middleware.</summary>
    public string? Body { get; private set; }

    internal void Begin(string traceIdentifier)
    {
        TraceIdentifier = traceIdentifier;
        EndpointReached = false;
        InboundResult = null;
        Body = null;
    }

    internal void MarkReached(StageResult? inboundResult, string body)
    {
        EndpointReached = true;
        InboundResult = inboundResult;
        Body = body;
    }
}
