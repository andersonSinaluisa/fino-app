using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SupportedModesRaw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatementParserCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BrandColor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    LogoKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trusted_senders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsDomain = table.Column<bool>(type: "boolean", nullable: false),
                    RequireAuthenticated = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trusted_senders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreferredCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletionRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Icon = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsIncome = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_corrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_corrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_category_corrections_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpoPushToken = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAmountsInPreview = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnNewTransaction = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnImportFinished = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnWeeklySummary = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_devices_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EmailAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecretReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantedScopes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SyncCursor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MessagesProcessed = table.Column<int>(type: "integer", nullable: false),
                    TransactionsDetected = table.Column<int>(type: "integer", nullable: false),
                    InboundAddress = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_connections_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "financial_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Alias = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Mask = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ConnectionMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    LastVerifiedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EstimatedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LastTransactionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_financial_accounts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParserCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    NewRows = table.Column<int>(type: "integer", nullable: false),
                    DuplicateRows = table.Column<int>(type: "integer", nullable: false),
                    ProbableDuplicateRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    IncomeTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeclaredClosingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ImportedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_imports_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "insights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Body = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ComparisonValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PercentChange = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: true),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_insights_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Payload = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PushSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceLabel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedFromIpHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categorization_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Pattern = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MatchKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TimesApplied = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorization_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categorization_rules_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_categorization_rules_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TransactionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    NormalizedDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Merchant = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryManuallySet = table.Column<bool>(type: "boolean", nullable: false),
                    AccountMask = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceConfidence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PossibleDuplicateOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmailConnectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transactions_financial_accounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "financial_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transactions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MatchType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    MatchedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchScore = table.Column<double>(type: "double precision", nullable: true),
                    TransactionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SuggestedCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Error = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    RawPayload = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_rows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_rows_imports_ImportId",
                        column: x => x.ImportId,
                        principalTable: "imports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_Action",
                table: "audit_log",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_UserId_CreatedAt",
                table: "audit_log",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_UserId_Code",
                table: "categories",
                columns: new[] { "UserId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categorization_rules_CategoryId",
                table: "categorization_rules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_categorization_rules_UserId_IsActive_Priority",
                table: "categorization_rules",
                columns: new[] { "UserId", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_category_corrections_UserId_CreatedAt",
                table: "category_corrections",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_devices_UserId_ExpoPushToken",
                table: "devices",
                columns: new[] { "UserId", "ExpoPushToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_connections_UserId_Status",
                table: "email_connections",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_ProviderCode",
                table: "financial_accounts",
                column: "ProviderCode");

            migrationBuilder.CreateIndex(
                name: "IX_financial_accounts_UserId_IsArchived",
                table: "financial_accounts",
                columns: new[] { "UserId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_import_rows_ImportId_RowNumber",
                table: "import_rows",
                columns: new[] { "ImportId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_imports_FinancialAccountId_ContentHash",
                table: "imports",
                columns: new[] { "FinancialAccountId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_imports_UserId_CreatedAt",
                table: "imports",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_insights_UserId_DisplayOrder",
                table: "insights",
                columns: new[] { "UserId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_providers_Code",
                table: "providers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_RevokedAt",
                table: "refresh_tokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_FinancialAccountId_ExternalReference",
                table: "transactions",
                columns: new[] { "FinancialAccountId", "ExternalReference" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_FinancialAccountId_Fingerprint",
                table: "transactions",
                columns: new[] { "FinancialAccountId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_FinancialAccountId_TransactionDate",
                table: "transactions",
                columns: new[] { "FinancialAccountId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ImportId",
                table: "transactions",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ProviderCode",
                table: "transactions",
                column: "ProviderCode");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId_CategoryId",
                table: "transactions",
                columns: new[] { "UserId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserId_TransactionDate",
                table: "transactions",
                columns: new[] { "UserId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_trusted_senders_ProviderCode_Value",
                table: "trusted_senders",
                columns: new[] { "ProviderCode", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedEmail",
                table: "users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "categorization_rules");

            migrationBuilder.DropTable(
                name: "category_corrections");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "email_connections");

            migrationBuilder.DropTable(
                name: "import_rows");

            migrationBuilder.DropTable(
                name: "insights");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "providers");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "trusted_senders");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "imports");

            migrationBuilder.DropTable(
                name: "financial_accounts");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
