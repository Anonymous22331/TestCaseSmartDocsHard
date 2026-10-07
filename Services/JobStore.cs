using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Background;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class JobStore(InvoiceQueue queue, IOptions<ProcessingOptions> options)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, JobResponse> jobs = [];
    private readonly Dictionary<Guid, JobInput> inputs = [];
    private readonly Dictionary<string, Guid> hashes = new(StringComparer.Ordinal);

    public Submission Submit(string hash, string path, string extension)
    {
        lock (gate)
        {
            if (hashes.TryGetValue(hash, out var existing))
                return new Submission(existing, true);
            if (jobs.Count >= options.Value.MaxStoredJobs)
                throw new IntakeException("capacity_exceeded", "Job storage is full. Restart the local service to clear it.");

            var id = Guid.NewGuid();
            jobs.Add(id, new JobResponse(id, "accepted"));
            inputs.Add(id, new JobInput(id, path, extension));
            hashes.Add(hash, id);
            if (queue.TryEnqueue(id))
                return new Submission(id, false);

            jobs.Remove(id);
            inputs.Remove(id);
            hashes.Remove(hash);
            throw new IntakeException("queue_full", "The processing queue is full. Try again later.");
        }
    }

    public JobResponse? Get(Guid id)
    {
        lock (gate)
            return jobs.GetValueOrDefault(id);
    }

    public JobInput? Start(Guid id)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(id, out var job) || job.Status != "accepted")
                return null;
            jobs[id] = job with { Status = "processing" };
            return inputs[id];
        }
    }

    public void Complete(JobResponse result)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(result.JobId, out var job) && job.Status == "processing")
            {
                jobs[result.JobId] = result;
                inputs.Remove(result.JobId);
            }
        }
    }
}
