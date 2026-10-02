using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Analytics;
using Nexo.Application.Audit;
using Nexo.Application.Auth;
using Nexo.Application.Budgets;
using Nexo.Application.Categories;
using Nexo.Application.Categorization;
using Nexo.Application.CreditCards;
using Nexo.Application.Deduplication;
using Nexo.Application.EmailIngestion;
using Nexo.Application.EmailIngestion.Parsers;
using Nexo.Application.Imports;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Insights;
using Nexo.Application.Notifications;
using Nexo.Application.Onboarding;
using Nexo.Application.Privacy;
using Nexo.Application.Providers;
using Nexo.Application.Pulses;
using Nexo.Application.QuickEntry;
using Nexo.Application.Transactions;
using Nexo.Application.Transfers;
using Nexo.Application.Withdrawals;

namespace Nexo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNexoApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<ImportOptions>(configuration.GetSection(ImportOptions.SectionName));
        services.Configure<DeduplicationOptions>(configuration.GetSection(DeduplicationOptions.SectionName));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditActivityService, AuditActivityService>();
        services.AddScoped<IProviderCatalogService, ProviderCatalogService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<ITransactionService>(sp => sp.GetRequiredService<TransactionService>());
        services.AddScoped<ITransactionSplitService, TransactionSplitService>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IInternalTransferService, InternalTransferService>();

        // Retiros de efectivo: una especialización de las transferencias internas,
        // no un sistema aparte. Depende de ICashAccountProvisioner (registrado más
        // abajo con el registro rápido) para poder crear la cuenta Efectivo al vuelo.
        services.AddScoped<IWithdrawalService, WithdrawalService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICategorizationEngine, CategorizationEngine>();
        services.AddScoped<ICategorizationRuleService, CategorizationRuleService>();
        services.AddScoped<IDeduplicationService, DeduplicationService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IInsightEngine, InsightEngine>();
        services.AddScoped<IInsightService, InsightService>();
        services.AddScoped<IPulseEngine, PulseEngine>();
        services.AddScoped<IPulseService, PulseService>();
        services.AddScoped<IPulseNotificationDecisionService, PulseNotificationDecisionService>();
        services.AddScoped<IOnboardingService, OnboardingService>();

        // Presupuestos + Comprometido. BudgetLedgerFactory is the one evaluator both
        // services share, and CommittedMoneyService is the only producer of
        // Comprometido/Disponible anywhere in the product.
        services.AddScoped<BudgetLedgerFactory>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<ICommittedMoneyService, CommittedMoneyService>();

        // Tarjetas de crédito. CreditCardLedger is the only producer of card figures
        // (shared by the card screens and Comprometido); ICardMovementClassifier is the
        // one place every write path asks "what is this card movement?".
        services.AddScoped<CreditCardLedger>();
        services.AddScoped<ICardMovementClassifier, CardMovementClassifier>();
        services.AddScoped<CreditCardService>();
        services.AddScoped<ICreditCardService>(sp => sp.GetRequiredService<CreditCardService>());

        // Registro rápido de efectivo. CashAccountProvisioner y
        // QuickEntrySuggestionService son colaboradores de QuickTransactionService,
        // no puntos de entrada paralelos: el único camino que ESCRIBE un movimiento
        // manual sigue siendo IQuickTransactionService.CreateAsync.
        services.AddScoped<ICashAccountProvisioner, CashAccountProvisioner>();
        services.AddScoped<IQuickTransactionService, QuickTransactionService>();
        services.AddScoped<IQuickEntrySuggestionService, QuickEntrySuggestionService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<IPrivacyService, PrivacyService>();
        services.AddScoped<IEmailConnectionService, EmailConnectionService>();
        services.AddScoped<IEmailIngestionPipeline, EmailIngestionPipeline>();
        services.AddScoped<ISenderValidator, SenderValidator>();

        services.AddStatementParsers();
        services.AddBankEmailParsers();

        return services;
    }

    /// <summary>
    /// Statement parsers are plain DI registrations: adding a bank is one line here
    /// plus one class. Order does not matter — the resolver sorts by priority.
    /// </summary>
    public static IServiceCollection AddStatementParsers(this IServiceCollection services)
    {
        services.AddScoped<IStatementParser, PichinchaStatementParser>();
        services.AddScoped<IStatementParser, GuayaquilStatementParser>();
        services.AddScoped<IStatementParser, ProdubancoStatementParser>();
        services.AddScoped<IStatementParser, PacificoStatementParser>();
        services.AddScoped<IStatementParser, GenericStatementParser>();
        services.AddScoped<IStatementParserResolver, StatementParserResolver>();
        return services;
    }

    public static IServiceCollection AddBankEmailParsers(this IServiceCollection services)
    {
        services.AddScoped<IBankEmailParser, PichinchaEmailParser>();
        services.AddScoped<IBankEmailParser, GuayaquilEmailParser>();
        services.AddScoped<IBankEmailParser, ProdubancoEmailParser>();
        services.AddScoped<IBankEmailParser, PacificoEmailParser>();
        services.AddScoped<IBankEmailParser, DeunaEmailParser>();
        services.AddScoped<IBankEmailParser, PayPhoneEmailParser>();
        services.AddScoped<IBankEmailParserResolver, BankEmailParserResolver>();
        return services;
    }
}
