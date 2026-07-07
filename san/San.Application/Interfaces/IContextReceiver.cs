using San.Application.DTOs;

namespace San.Application.Interfaces;

public interface IContextReceiver
{
    Task<ContextPushResult> ProcessPushAsync(ContextPushRequest request);
}
