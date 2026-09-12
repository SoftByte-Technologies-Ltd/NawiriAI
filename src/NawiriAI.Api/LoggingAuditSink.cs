using NawiriAI.Abstractions;

namespace NawiriAI.Api;

public sealed class LoggingAuditSink(ILogger<LoggingAuditSink> logger) : IAIAuditSink
{
    public Task WriteAsync(AIAuditEvent entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("NawiriAI request {CorrelationId} tool {Metric} provider {ProviderId} model {ModelId} outcome {Outcome} duration {Milliseconds}ms inputTokens {InputTokens} outputTokens {OutputTokens}",
            entry.CorrelationId, entry.Metric, entry.ProviderId, entry.ModelId, entry.Outcome,
            entry.DurationMilliseconds, entry.Usage?.InputTokens, entry.Usage?.OutputTokens);
        return Task.CompletedTask;
    }
}
