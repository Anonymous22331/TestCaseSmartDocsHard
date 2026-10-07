using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;

namespace TestCaseSmartDocsHard.Background;

public sealed class InvoiceWorker(
    InvoiceQueue queue,
    JobStore jobs,
    InvoiceProcessingService processing,
    FileStorage storage,
    IOptions<ProcessingOptions> options,
    ILogger<InvoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var id in queue.ReadAllAsync(stoppingToken))
            {
                var input = jobs.Start(id);
                if (input is null)
                    continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.JobTimeoutSeconds));
                try
                {
                    jobs.Complete(await processing.ProcessAsync(input, timeout.Token));
                    logger.LogInformation("Invoice job {JobId} completed with status {Status}.", id, jobs.Get(id)?.Status);
                }
                catch (OperationCanceledException)
                {
                    jobs.Complete(new JobResponse(id, "error", Error: new ApiError(
                        stoppingToken.IsCancellationRequested ? "processing_stopped" : "job_timeout",
                        stoppingToken.IsCancellationRequested ? "Processing stopped during shutdown." : "Processing exceeded its time limit.")));
                }
                catch (ProcessingException exception)
                {
                    jobs.Complete(new JobResponse(id, "error", Error: new ApiError(exception.Code, exception.Message)));
                    logger.LogWarning("Invoice job {JobId} failed with code {Code}.", id, exception.Code);
                }
                catch (Exception exception)
                {
                    jobs.Complete(new JobResponse(id, "error", Error: new ApiError("processing_failed", "The document could not be processed.")));
                    logger.LogError("Invoice job {JobId} failed ({ErrorType}).", id, exception.GetType().Name);
                }
                finally
                {
                    storage.Delete(input.Path);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
