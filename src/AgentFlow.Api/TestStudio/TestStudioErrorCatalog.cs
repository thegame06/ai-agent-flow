namespace AgentFlow.Api.TestStudio;

public static class TestStudioErrorCatalog
{
    public const string RuntimeIncompatible = "runtime_incompatible";
    public const string AgentRequired = "agent_required";
    public const string AgentNotFound = "agent_not_found";
    public const string ThreadCreateFailed = "thread_create_failed";
    public const string AttachmentInvalidSize = "attachment_invalid_size";
    public const string AttachmentNotSupported = "attachment_not_supported";
    public const string ChannelRequired = "channel_required";
    public const string ChannelNotFound = "channel_not_found";
    public const string ChannelHandlerMissing = "channel_handler_missing";
    public const string CorrelationRequired = "correlation_required";
    public const string SessionRateLimited = "session_rate_limited";
}
