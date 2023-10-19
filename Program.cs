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
using Microsoft.Extensions.Configuration;
using Serilog;
using Amazon.CloudWatchLogs;
using Serilog.Sinks.AwsCloudWatch;
using Amazon.Runtime;
using Amazon;
using Serilog.Sinks.OpenTelemetry;
using AWS.Logger;
using AWS.Logger.SeriLog;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Options;

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
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();


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

        var client = new AmazonCloudWatchLogsClient(RegionEndpoint.USEast2);
        var logger = new LoggerConfiguration()
            .WriteTo.AmazonCloudWatch(
                // The name of the log group to log to
                logGroup: "/metrics/otel",
                logStreamPrefix: DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
        // The AWS CloudWatch client to use
        cloudWatchClient: client)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} ({TraceId}:{SpanId})] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();

        appBuilder.Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddConfiguration(appBuilder.Configuration.GetSection("Logging"));
            loggingBuilder.AddSerilog(logger);
            loggingBuilder.Configure(options =>
            {
                options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
                                                   | ActivityTrackingOptions.TraceId
                                                   | ActivityTrackingOptions.ParentId
                                                   | ActivityTrackingOptions.Baggage
                                                   | ActivityTrackingOptions.Tags;
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