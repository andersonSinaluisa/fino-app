using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Auth;
using Nexo.Application.Common;
using Nexo.Domain.Common;

namespace Nexo.Application.Onboarding;

public interface IOnboardingService
{
    Task<OnboardingStatusDto> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Called when the person actually reaches the post-login onboarding flow (not on every app open -- see app/index.tsx's redirect logic on the client).</summary>
    Task<OnboardingStatusDto> StartAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Called when the bank-tutorial animation is finished (not skipped).</summary>
    Task<OnboardingStatusDto> CompleteTutorialAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Called from the "Ahora no" bottom sheet's "Configurar después".</summary>
    Task<OnboardingStatusDto> SkipAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Onboarding funcional (rediseño post-login): the four transitions the app
/// drives explicitly. The other two milestones in <see cref="OnboardingStatusDto"/>
/// -- first account, first import -- are deliberately NOT here: they are
/// recorded from AccountService.CreateAsync and ImportService.ConfirmAsync
/// themselves, because "first account added" is a fact about accounts, not
/// about which screen created one. That also means those two stay correct
/// even for someone who skips the onboarding and adds their first account
/// later from Cuentas -- there is exactly one place either milestone can be
/// set, so it can never be missed or duplicated depending on which flow was used.
/// </summary>
public sealed class OnboardingService(INexoDbContext db, IClock clock) : IOnboardingService
{
    public async Task<OnboardingStatusDto> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Map(await RequireUserAsync(userId, cancellationToken));

    public async Task<OnboardingStatusDto> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.StartOnboarding(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<OnboardingStatusDto> CompleteTutorialAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.CompleteOnboardingTutorial(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<OnboardingStatusDto> SkipAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.SkipOnboarding(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    private async Task<Domain.Users.User> RequireUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new NotFoundException("User", userId);

    private static OnboardingStatusDto Map(Domain.Users.User user) => new(
        user.OnboardingStartedAt,
        user.OnboardingTutorialCompletedAt,
        user.OnboardingSkippedAt,
        user.FirstAccountAddedAt,
        user.FirstImportCompletedAt);
}
