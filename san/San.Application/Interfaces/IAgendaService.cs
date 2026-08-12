using San.Application;

namespace San.Application.Interfaces;

// The ranked "what am I supposed to be doing" list, shared by the API endpoint that
// serves it on request and the worker that pushes it each morning. San.API and
// San.Worker are separate containers, so this cannot live in a controller.
public interface IAgendaService
{
    Task<List<AgendaItem>> BuildAsync(int limit = 12, CancellationToken ct = default);
}
