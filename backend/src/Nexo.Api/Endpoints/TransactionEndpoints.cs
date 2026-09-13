using Nexo.Application.Abstractions;
using Nexo.Application.Categories;
using Nexo.Application.Common;
using Nexo.Application.Insights;
using Nexo.Application.Transactions;
using CategoryUpdateCategoryRequest = Nexo.Application.Categories.UpdateCategoryRequest;
using TransactionUpdateCategoryRequest = Nexo.Application.Transactions.UpdateCategoryRequest;

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
            string? providerCode,
            decimal? minAmount,
            decimal? maxAmount,
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
                ProviderCode = providerCode,
                MinAmount = minAmount,
                MaxAmount = maxAmount,
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
            TransactionUpdateCategoryRequest request,
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

        group.MapPut("/transactions/{id:guid}/merchant", async (
            Guid id,
            UpdateMerchantRequest request,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await transactions.UpdateMerchantAsync(
                currentUser.RequireUserId(),
                id,
                request,
                cancellationToken)))
            .WithSummary("Corrige el nombre del comercio sin tocar la descripción original del banco.");

        group.MapPut("/transactions/{id:guid}/duplicate-review", async (
            Guid id,
            ResolveDuplicateRequest request,
            ITransactionService transactions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await transactions.ResolveDuplicateAsync(
                currentUser.RequireUserId(),
                id,
                request,
                cancellationToken)))
            .WithSummary("Resuelve un posible duplicado: lo mantiene como movimiento propio o lo ignora. Nunca lo borra.");

        group.MapGet("/categories", async (
                ICategoryService categories,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await categories.ListAsync(currentUser.RequireUserId(), cancellationToken)))
            .WithTags("Categories");

        // Categorías personalizadas: crear/editar una categoría propia con
        // ícono y color. Nunca toca ni permite tocar las del sistema.
        group.MapPost("/categories", async (
                CreateCategoryRequest request,
                ICategoryService categories,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await categories.CreateAsync(currentUser.RequireUserId(), request, cancellationToken)))
            .WithTags("Categories")
            .WithSummary("Crea una categoría propia con ícono y color. 409 si ya tienes una con ese nombre.");

        group.MapPut("/categories/{id:guid}", async (
                Guid id,
                CategoryUpdateCategoryRequest request,
                ICategoryService categories,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            Results.Ok(await categories.UpdateAsync(currentUser.RequireUserId(), id, request, cancellationToken)))
            .WithTags("Categories")
            .WithSummary("Cambia nombre/ícono/color de una categoría propia. 404 si es del sistema o de otro usuario.");

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
