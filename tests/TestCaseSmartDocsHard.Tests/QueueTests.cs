using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Background;
using TestCaseSmartDocsHard.Models;
using TestCaseSmartDocsHard.Services;

namespace TestCaseSmartDocsHard.Tests;

public sealed class QueueTests
{
    [Fact]
    public async Task StartsEachJobOnceAndPreservesTerminalResult()
    {
        var options = Options.Create(new ProcessingOptions());
        var queue = new InvoiceQueue(options);
        var store = new JobStore(queue, options);
        var submission = store.Submit("hash", "file.pdf", ".pdf");
        Assert.Equal("accepted", store.Get(submission.JobId)!.Status);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(submission.JobId, reader.Current);
        Assert.NotNull(store.Start(submission.JobId));
        Assert.Null(store.Start(submission.JobId));
        Assert.Equal("processing", store.Get(submission.JobId)!.Status);
        store.Complete(new JobResponse(submission.JobId, "ready"));
        store.Complete(new JobResponse(submission.JobId, "error"));
        Assert.Equal("ready", store.Get(submission.JobId)!.Status);
        Assert.True(store.Submit("hash", "another.pdf", ".pdf").IsDuplicate);
    }

    [Fact]
    public void FullQueueDoesNotLeavePhantomDeduplicationEntry()
    {
        var options = Options.Create(new ProcessingOptions { QueueCapacity = 1 });
        var queue = new InvoiceQueue(options);
        var store = new JobStore(queue, options);
        store.Submit("first", "first.pdf", ".pdf");
        Assert.Equal("queue_full", Assert.Throws<IntakeException>(() => store.Submit("second", "second.pdf", ".pdf")).Code);
        Assert.Equal("queue_full", Assert.Throws<IntakeException>(() => store.Submit("second", "second.pdf", ".pdf")).Code);
    }
}
