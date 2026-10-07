using Microsoft.AspNetCore.Mvc;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;

namespace TestCaseSmartDocsHard.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController(InvoiceIntakeService intake, JobStore jobs) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<JobAccepted>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<JobAccepted>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JobAccepted>> Post([FromForm] UploadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var submission = await intake.SubmitAsync(request.File, cancellationToken);
            var response = new JobAccepted(submission.JobId);
            return submission.IsDuplicate ? Ok(response) : AcceptedAtAction(nameof(Get), new { jobId = submission.JobId }, response);
        }
        catch (IntakeException exception)
        {
            var error = new ApiError(exception.Code, exception.Message);
            return exception.Code is "queue_full" or "capacity_exceeded"
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, error) : BadRequest(error);
        }
    }

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType<JobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public ActionResult<JobResponse> Get(Guid jobId)
    {
        var job = jobs.Get(jobId);
        return job is null ? NotFound(new ApiError("job_not_found", "The requested job does not exist.")) : Ok(job);
    }
}
