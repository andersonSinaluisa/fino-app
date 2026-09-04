using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Providers;

namespace Nexo.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/providers", async (
                IProviderCatalogService providers,
                CancellationToken cancellationToken) =>
            Results.Ok(await providers.ListAsync(cancellationToken)))
            .WithTags("Providers")
            .RequireAuthorization()
            .WithSummary("Catálogo de bancos y billeteras con sus capacidades reales.");

        var group = app.MapGroup("/api/v1/accounts")
            .WithTags("Accounts")
            .RequireAuthorization();

        group.MapGet("", async (
            bool? includeArchived,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await accounts.ListAsync(
                currentUser.RequireUserId(),
                includeArchived ?? false,
                cancellationToken)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await accounts.GetAsync(currentUser.RequireUserId(), id, cancellationToken)));

        group.MapPost("", async (
            CreateAccountRequest request,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var created = await accounts.CreateAsync(currentUser.RequireUserId(), request, cancellationToken);
            return Results.Created($"/api/v1/accounts/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateAccountRequest request,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await accounts.UpdateAsync(currentUser.RequireUserId(), id, request, cancellationToken)));

        group.MapPost("/{id:guid}/verified-balance", async (
            Guid id,
            SetVerifiedBalanceRequest request,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
            Results.Ok(await accounts.SetVerifiedBalanceAsync(
                currentUser.RequireUserId(),
                id,
                request,
                cancellationToken)))
            .WithSummary("Registra el saldo que el usuario vio en el banco; a partir de ahí el saldo vuelve a ser verificado.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IAccountService accounts,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            await accounts.ArchiveAsync(currentUser.RequireUserId(), id, cancellationToken);
            return Results.NoContent();
        });

        return app;
    }
}
