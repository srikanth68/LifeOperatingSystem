namespace San.Application.DTOs;

public record ChatMessageResult(Guid Id, string Role, string Content, DateTime CreatedAt);

// ImageDataUrl: an optional "data:image/jpeg;base64,..." attached to THIS message.
// The browser downscales before sending — see SanModule's attachImage — because a
// phone photo is several megabytes and the vision encoder gains nothing from
// resolution the model can't use.
public record ChatSendRequest(string Content, string? ImageDataUrl = null);

public record ChatSendResult(ChatMessageResult UserMessage, ChatMessageResult AssistantMessage, string Provider, string Model);
