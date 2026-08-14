namespace San.Application.DTOs;

public record ChatMessageResult(Guid Id, string Role, string Content, DateTime CreatedAt);

// ImageDataUrl: an optional "data:image/jpeg;base64,..." attached to THIS message.
// The browser downscales before sending — see SanModule's attachImage — because a
// phone photo is several megabytes and the vision encoder gains nothing from
// resolution the model can't use.
// Mode: "voice" when the user spoke this turn (push-to-talk or call mode) and the reply
// will be read aloud. Anything else, including absent, means a typed turn. Optional and
// last so every existing caller is unaffected.
public record ChatSendRequest(string Content, string? ImageDataUrl = null, string? Mode = null);

public record ChatSendResult(ChatMessageResult UserMessage, ChatMessageResult AssistantMessage, string Provider, string Model);
