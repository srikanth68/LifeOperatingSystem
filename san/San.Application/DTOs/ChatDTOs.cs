namespace San.Application.DTOs;

public record ChatMessageResult(Guid Id, string Role, string Content, DateTime CreatedAt);

public record ChatSendRequest(string Content);

public record ChatSendResult(ChatMessageResult UserMessage, ChatMessageResult AssistantMessage, string Provider, string Model);
