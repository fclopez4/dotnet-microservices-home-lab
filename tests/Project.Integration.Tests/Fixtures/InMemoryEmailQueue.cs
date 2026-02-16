using System.Collections.Concurrent;
using Project.Domain.Entities;
using Project.Domain.Ports;

namespace Project.Integration.Tests.Fixtures;

public class InMemoryEmailQueue : IEmailQueue
{
    private readonly ConcurrentBag<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages => [.. _messages];

    public Task EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }

    public Task<EmailMessage?> DequeueAsync(CancellationToken ct = default)
        => Task.FromResult(_messages.FirstOrDefault());

    public void Clear() => _messages.Clear();
}
