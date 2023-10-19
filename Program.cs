using aws_dotnet_otlp;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;
using System.Reflection.PortableExecutable;

internal class Program
{
    private static void Main(string[] args)
    {
        var appBuilder = WebApplication.CreateBuilder(args);

        // Add services to the container.

        var collectorEndpoint = new Uri(appBuilder.Configuration.GetValue("OpenTelemetry:CollectorEndpoint", defaultValue: "http://localhost:4317")!);
        Action<ResourceBuilder> configureResource = r => r.AddService(
            serviceName: appBuilder.Configuration.GetValue("ServiceName", defaultValue: "otel-test")!,
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            serviceInstanceId: Environment.MachineName);

        appBuilder.Services.AddSingleton<Instrumentation>();

        appBuilder.Services.AddOpenTelemetry()
            .ConfigureResource(configureResource)
            .WithTracing(builder =>
            {
                // Tracing

                // Ensure the TracerProvider subscribes to any custom ActivitySources.
                builder.AddAspNetCoreInstrumentation()
                    .AddXRayTraceId()
                    .AddAWSInstrumentation()
                    .AddHttpClientInstrumentation();
                    

                // Use IConfiguration binding for AspNetCore instrumentation options.
                appBuilder.Services.Configure<AspNetCoreInstrumentationOptions>(appBuilder.Configuration.GetSection("AspNetCoreInstrumentation"));

                builder.AddOtlpExporter(otlpOptions =>
                {
                    // Use IConfiguration directly for Otlp exporter endpoint option.
                    otlpOptions.Endpoint = collectorEndpoint;
                });

                Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
            });
            //.WithMetrics(builder =>
            //{
            //    builder
            //        .AddMeter(Instrumentation.MeterName)
            //        .AddRuntimeInstrumentation()
            //        .AddHttpClientInstrumentation()
            //        .AddAspNetCoreInstrumentation();

            //    builder.AddView(instrument =>
            //    {
            //        return instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
            //            ? new Base2ExponentialBucketHistogramConfiguration()
            //            : null;
            //    });

            //    builder.AddOtlpExporter(otlpOptions =>
            //    {
            //        // Use IConfiguration directly for Otlp exporter endpoint option.
            //        otlpOptions.Endpoint = collectorEndpoint;
            //    });
            //});

        // Configure OpenTelemetry Logging.
        appBuilder.Logging.AddOpenTelemetry(options =>
        {
            // Note: See appsettings.json Logging:OpenTelemetry section for configuration.

            var resourceBuilder = ResourceBuilder.CreateDefault();
            configureResource(resourceBuilder);
            options.SetResourceBuilder(resourceBuilder);
            options.AddOtlpExporter(otlpOptions =>
            {
                // Use IConfiguration directly for Otlp exporter endpoint option.
                otlpOptions.Endpoint = collectorEndpoint;
            });
        });

        appBuilder.Services.AddControllers();

        var app = appBuilder.Build();

        // Configure the HTTP request pipeline.

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}