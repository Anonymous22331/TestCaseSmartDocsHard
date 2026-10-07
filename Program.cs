using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using TestCaseSmartDocsHard.Background;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;
using TestCaseSmartDocsHard.Validation;

namespace TestCaseSmartDocsHard;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = InvoiceIntakeService.MaxRequestBytes);
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = InvoiceIntakeService.MaxFileBytes;
            options.MemoryBufferThreshold = 65536;
        });
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressMapClientErrors = true;
            options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(
                new ApiError("invalid_request", "Supply one non-empty file of at most 20 MB in the multipart field 'file'."));
        });
        builder.Services.AddOptions<ProcessingOptions>()
            .Bind(builder.Configuration.GetSection("Processing"))
            .Validate(options => options.QueueCapacity > 0 && options.MaxStoredJobs > 0
                && options.ProcessTimeoutSeconds > 0 && options.JobTimeoutSeconds > 0
                && !string.IsNullOrWhiteSpace(options.StoragePath), "Processing limits and storage path must be valid.")
            .ValidateOnStart();
        builder.Services.AddSingleton<InvoiceQueue>();
        builder.Services.AddSingleton<JobStore>();
        builder.Services.AddSingleton<FileStorage>();
        builder.Services.AddSingleton<InvoiceIntakeService>();
        builder.Services.AddSingleton<LocalProcessRunner>();
        builder.Services.AddSingleton<DocumentRecognitionService>();
        builder.Services.AddSingleton<InvoiceTableParser>();
        builder.Services.AddSingleton<InvoiceExtractionService>();
        builder.Services.AddSingleton<InvoiceValidator>();
        builder.Services.AddSingleton<InvoiceProcessingService>();
        builder.Services.AddHostedService<InvoiceWorker>();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options => options.MapType<decimal>(() => new OpenApiSchema { Type = "string" }));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try
            {
                if (context.Request.ContentLength > InvoiceIntakeService.MaxRequestBytes)
                    throw new BadHttpRequestException("Request exceeds the upload limit.", StatusCodes.Status413PayloadTooLarge);
                await next(context);
            }
            catch (Exception exception) when (exception is BadHttpRequestException or InvalidDataException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new ApiError("invalid_request", "The upload is malformed or exceeds the 20 MB file limit."), context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (Exception exception)
            {
                app.Logger.LogError("Request failed ({ErrorType}).", exception.GetType().Name);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new ApiError("internal_error", "The request could not be completed."), context.RequestAborted);
            }
        });
        app.UseSwagger(options => options.RouteTemplate = "api/swagger/{documentName}/swagger.json");
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "api/swagger";
            options.SwaggerEndpoint("/api/swagger/v1/swagger.json", "TestCaseSmartDocsHard API");
        });
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapControllers();
        app.Run();
    }
}
