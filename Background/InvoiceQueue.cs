using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Background;

public sealed class InvoiceQueue(IOptions<ProcessingOptions> options)
{
    private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    public bool TryEnqueue(Guid jobId) => channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
