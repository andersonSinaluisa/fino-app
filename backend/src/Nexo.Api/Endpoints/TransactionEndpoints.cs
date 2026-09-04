using Nexo.Application.Abstractions;
using Nexo.Application.Categories;
using Nexo.Application.Common;
using Nexo.Application.Insights;
using Nexo.Application.Transactions;

namespace Nexo.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Transactions")
            .RequireAuthorization();

        group.MapGet("/summary", async (
                ITransactionService transactions,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await transactions.GetHomeSummaryAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithSummary("Todo lo que necesita la pantalla de inicio en una sola llamada.");

        group.MapGet("/transactions", async (
            string? search,
            Guid? accountId,
            Guid? categoryId,
            string? direction,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? includeIgnored,
            int? page,
            int? pageSize,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var filter = new TransactionFilter
            {
                Search = search,
                AccountId = accountId,
                CategoryId = categoryId,
                Direction = direction,
                From = from,
                To = to,
                IncludeIgnored = includeIgnored ?? false,
                Page = new PageRequest { Page = page ?? 1, PageSize = pageSize ?? 30 },
            };

            return Results.Ok(await transactions.QueryAsync(
                currentUser.RequireUserId(),
                filter,
                cancellationToken));
        });

        group.MapGet("/transactions/{id:guid}", async (
            Guid id,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await transactions.GetAsync(currentUser.RequireUserId(), id, cancellationToken)));

        group.MapPut("/transactions/{id:guid}/category", async (
            Guid id,
            UpdateCategoryRequest request,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await transactions.UpdateCategoryAsync(
                currentUser.RequireUserId(),
                id,
                request,
                cancellationToken)))
            .WithSummary("Corrige la categoría y aprende una regla para la próxima vez.");

        group.MapPut("/transactions/{id:guid}/note", async (
            Guid id,
            UpdateNoteRequest request,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await transactions.UpdateNoteAsync(
                currentUser.RequireUserId(),
                id,
                request,
                cancellationToken)));

        group.MapGet("/categories", async (
                ICategoryService categories,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await categories.ListAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithTags("Categories");

        group.MapGet("/insights", async (
                IInsightService insights,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await insights.ListAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithTags("Insights");

        group.MapPost("/insights/refresh", async (
                IInsightService insights,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await insights.RefreshAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithTags("Insights");

        return app;
    }
}
