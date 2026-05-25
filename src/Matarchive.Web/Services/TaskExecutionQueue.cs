using System.Threading.Channels;
using Matarchive.Web.Domain;

namespace Matarchive.Web.Services;

public sealed class TaskExecutionQueue
{
    private readonly Channel<TaskRunRequest> _channel = Channel.CreateUnbounded<TaskRunRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(TaskRunRequest request, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public ChannelReader<TaskRunRequest> Reader => _channel.Reader;
}

