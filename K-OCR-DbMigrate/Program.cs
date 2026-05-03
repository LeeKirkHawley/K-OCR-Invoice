using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

// ── Usage ────────────────────────────────────────────────────────────────────
//   K-OCR-DbMigrate --sqlite "Data Source=C:\path\to\kocr.db"
//                   --sqlserver "Server=.;Database=KOCRIdentity;Trusted_Connection=True;TrustServerCertificate=True;"
//                   [--force]
//
//   Prerequisites: run `dotnet ef database update` against KOCRAsp first so the
//   SQL Server schema (tables, indexes) already exists before running this tool.
//
//   --force  Clears all destination tables (children first) before inserting.
//            Without this flag, tables that already contain rows are skipped.
// ─────────────────────────────────────────────────────────────────────────────

Banner("K-OCR Identity Database Migration: SQLite → SQL Server");

var sqliteCs    = GetArg(args, "--sqlite")
    ?? Prompt("SQLite connection string (e.g. \"Data Source=C:\\KOCRBase\\kocr.db\"): ");
var sqlServerCs = GetArg(args, "--sqlserver")
    ?? Prompt("SQL Server connection string: ");
var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"  Source:      {sqliteCs}");
Console.WriteLine($"  Destination: {sqlServerCs}");
Console.WriteLine($"  Force:       {(force ? "yes — destination tables will be cleared first" : "no")}");
Console.WriteLine();

// ── Open connections ─────────────────────────────────────────────────────────
await using var sqlite    = new SqliteConnection(sqliteCs);
await using var sqlServer = new SqlConnection(sqlServerCs);

try   { await sqlite.OpenAsync(); }
catch (Exception ex) { return Fatal("Cannot open SQLite source: " + ex.Message); }
Console.WriteLine("✓ Connected to SQLite source");

try   { await sqlServer.OpenAsync(); }
catch (Exception ex) { return Fatal("Cannot open SQL Server destination: " + ex.Message); }
Console.WriteLine("✓ Connected to SQL Server destination");
Console.WriteLine();

// ── Verify schema exists ─────────────────────────────────────────────────────
if (!await TableExistsAsync("AspNetUsers"))
    return Fatal("SQL Server schema not found.\n" +
                 "Run:  dotnet ef database update --project KOCRAsp\\KOCRAsp.csproj\n" +
                 "Then re-run this tool.");

// ── Optional force-clear (children before parents to satisfy FK constraints) ─
if (force)
{
    Console.WriteLine("Clearing destination tables...");
    string[] clearOrder =
    [
        "AspNetRoleClaims",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserRoles",
        "AspNetUserTokens",
        "AspNetUsers",
        "AspNetRoles",
        "Organizations",
    ];
    foreach (var t in clearOrder)
    {
        await ExecSqlServerAsync($"DELETE FROM [{t}]");
        Console.WriteLine($"  Cleared {t}");
    }
    Console.WriteLine();
}

// ── Run migrations in dependency order (parents before children) ─────────────
int totalRows = 0;
totalRows += await RunAsync("AspNetRoles",      MigrateAspNetRolesAsync);
totalRows += await RunAsync("Organizations",    MigrateOrganizationsAsync);
totalRows += await RunAsync("AspNetUsers",      MigrateAspNetUsersAsync);
totalRows += await RunAsync("AspNetRoleClaims", MigrateAspNetRoleClaimsAsync);
totalRows += await RunAsync("AspNetUserClaims", MigrateAspNetUserClaimsAsync);
totalRows += await RunAsync("AspNetUserLogins", MigrateAspNetUserLoginsAsync);
totalRows += await RunAsync("AspNetUserRoles",  MigrateAspNetUserRolesAsync);
totalRows += await RunAsync("AspNetUserTokens", MigrateAspNetUserTokensAsync);

Console.WriteLine();
Console.WriteLine($"Migration complete — {totalRows} total rows transferred.");
return 0;

// ── Table migrators ──────────────────────────────────────────────────────────

async Task<int> MigrateAspNetRolesAsync()
{
    const string sel = "SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM AspNetRoles";
    const string ins = """
        INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
        VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@Id",               Req(rdr, "Id"));
        cmd.Parameters.AddWithValue("@Name",             Nullable(rdr, "Name"));
        cmd.Parameters.AddWithValue("@NormalizedName",   Nullable(rdr, "NormalizedName"));
        cmd.Parameters.AddWithValue("@ConcurrencyStamp", Nullable(rdr, "ConcurrencyStamp"));
    });
}

async Task<int> MigrateOrganizationsAsync()
{
    const string sel = """
        SELECT Id, Name, Description, IsActive, IsGuestOrganization,
               MarkedForDeletionAtUtc, CreatedAtUtc
        FROM Organizations
        """;
    const string ins = """
        INSERT INTO Organizations
            (Id, Name, Description, IsActive, IsGuestOrganization,
             MarkedForDeletionAtUtc, CreatedAtUtc)
        VALUES
            (@Id, @Name, @Description, @IsActive, @IsGuestOrganization,
             @MarkedForDeletionAtUtc, @CreatedAtUtc)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@Id",                    Req(rdr, "Id"));
        cmd.Parameters.AddWithValue("@Name",                  Req(rdr, "Name"));
        cmd.Parameters.AddWithValue("@Description",           Nullable(rdr, "Description"));
        cmd.Parameters.AddWithValue("@IsActive",              Bool(rdr, "IsActive"));
        cmd.Parameters.AddWithValue("@IsGuestOrganization",   Bool(rdr, "IsGuestOrganization"));
        cmd.Parameters.AddWithValue("@MarkedForDeletionAtUtc",NullableDateTime(rdr, "MarkedForDeletionAtUtc"));
        cmd.Parameters.AddWithValue("@CreatedAtUtc",          DateTime(rdr, "CreatedAtUtc"));
    });
}

async Task<int> MigrateAspNetUsersAsync()
{
    const string sel = """
        SELECT Id, FullName, OrganizationId, IsOrganizationAdmin, IsGlobalAdmin,
               UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
               PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber,
               PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled,
               AccessFailedCount
        FROM AspNetUsers
        """;
    const string ins = """
        INSERT INTO AspNetUsers
            (Id, FullName, OrganizationId, IsOrganizationAdmin, IsGlobalAdmin,
             UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
             PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber,
             PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled,
             AccessFailedCount)
        VALUES
            (@Id, @FullName, @OrganizationId, @IsOrganizationAdmin, @IsGlobalAdmin,
             @UserName, @NormalizedUserName, @Email, @NormalizedEmail, @EmailConfirmed,
             @PasswordHash, @SecurityStamp, @ConcurrencyStamp, @PhoneNumber,
             @PhoneNumberConfirmed, @TwoFactorEnabled, @LockoutEnd, @LockoutEnabled,
             @AccessFailedCount)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@Id",                  Req(rdr, "Id"));
        cmd.Parameters.AddWithValue("@FullName",            Nullable(rdr, "FullName"));
        cmd.Parameters.AddWithValue("@OrganizationId",      Nullable(rdr, "OrganizationId"));
        cmd.Parameters.AddWithValue("@IsOrganizationAdmin", Bool(rdr, "IsOrganizationAdmin"));
        cmd.Parameters.AddWithValue("@IsGlobalAdmin",       Bool(rdr, "IsGlobalAdmin"));
        cmd.Parameters.AddWithValue("@UserName",            Nullable(rdr, "UserName"));
        cmd.Parameters.AddWithValue("@NormalizedUserName",  Nullable(rdr, "NormalizedUserName"));
        cmd.Parameters.AddWithValue("@Email",               Nullable(rdr, "Email"));
        cmd.Parameters.AddWithValue("@NormalizedEmail",     Nullable(rdr, "NormalizedEmail"));
        cmd.Parameters.AddWithValue("@EmailConfirmed",      Bool(rdr, "EmailConfirmed"));
        cmd.Parameters.AddWithValue("@PasswordHash",        Nullable(rdr, "PasswordHash"));
        cmd.Parameters.AddWithValue("@SecurityStamp",       Nullable(rdr, "SecurityStamp"));
        cmd.Parameters.AddWithValue("@ConcurrencyStamp",    Nullable(rdr, "ConcurrencyStamp"));
        cmd.Parameters.AddWithValue("@PhoneNumber",         Nullable(rdr, "PhoneNumber"));
        cmd.Parameters.AddWithValue("@PhoneNumberConfirmed",Bool(rdr, "PhoneNumberConfirmed"));
        cmd.Parameters.AddWithValue("@TwoFactorEnabled",    Bool(rdr, "TwoFactorEnabled"));
        cmd.Parameters.AddWithValue("@LockoutEnd",          NullableDateTimeOffset(rdr, "LockoutEnd"));
        cmd.Parameters.AddWithValue("@LockoutEnabled",      Bool(rdr, "LockoutEnabled"));
        cmd.Parameters.AddWithValue("@AccessFailedCount",   Int(rdr, "AccessFailedCount"));
    });
}

// Id is IDENTITY in SQL Server — omit it; SQL Server auto-generates a new value.
// The integer Id is never referenced by foreign keys, so values need not be preserved.
async Task<int> MigrateAspNetRoleClaimsAsync()
{
    const string sel = "SELECT RoleId, ClaimType, ClaimValue FROM AspNetRoleClaims";
    const string ins = """
        INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue)
        VALUES (@RoleId, @ClaimType, @ClaimValue)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@RoleId",     Req(rdr, "RoleId"));
        cmd.Parameters.AddWithValue("@ClaimType",  Nullable(rdr, "ClaimType"));
        cmd.Parameters.AddWithValue("@ClaimValue", Nullable(rdr, "ClaimValue"));
    });
}

// Id is IDENTITY in SQL Server — same rationale as AspNetRoleClaims.
async Task<int> MigrateAspNetUserClaimsAsync()
{
    const string sel = "SELECT UserId, ClaimType, ClaimValue FROM AspNetUserClaims";
    const string ins = """
        INSERT INTO AspNetUserClaims (UserId, ClaimType, ClaimValue)
        VALUES (@UserId, @ClaimType, @ClaimValue)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@UserId",     Req(rdr, "UserId"));
        cmd.Parameters.AddWithValue("@ClaimType",  Nullable(rdr, "ClaimType"));
        cmd.Parameters.AddWithValue("@ClaimValue", Nullable(rdr, "ClaimValue"));
    });
}

async Task<int> MigrateAspNetUserLoginsAsync()
{
    const string sel = "SELECT LoginProvider, ProviderKey, ProviderDisplayName, UserId FROM AspNetUserLogins";
    const string ins = """
        INSERT INTO AspNetUserLogins (LoginProvider, ProviderKey, ProviderDisplayName, UserId)
        VALUES (@LoginProvider, @ProviderKey, @ProviderDisplayName, @UserId)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@LoginProvider",      Req(rdr, "LoginProvider"));
        cmd.Parameters.AddWithValue("@ProviderKey",        Req(rdr, "ProviderKey"));
        cmd.Parameters.AddWithValue("@ProviderDisplayName",Nullable(rdr, "ProviderDisplayName"));
        cmd.Parameters.AddWithValue("@UserId",             Req(rdr, "UserId"));
    });
}

async Task<int> MigrateAspNetUserRolesAsync()
{
    const string sel = "SELECT UserId, RoleId FROM AspNetUserRoles";
    const string ins = """
        INSERT INTO AspNetUserRoles (UserId, RoleId)
        VALUES (@UserId, @RoleId)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@UserId", Req(rdr, "UserId"));
        cmd.Parameters.AddWithValue("@RoleId", Req(rdr, "RoleId"));
    });
}

async Task<int> MigrateAspNetUserTokensAsync()
{
    const string sel = "SELECT UserId, LoginProvider, Name, Value FROM AspNetUserTokens";
    const string ins = """
        INSERT INTO AspNetUserTokens (UserId, LoginProvider, Name, Value)
        VALUES (@UserId, @LoginProvider, @Name, @Value)
        """;
    return await CopyRowsAsync(sel, ins, (rdr, cmd) =>
    {
        cmd.Parameters.AddWithValue("@UserId",        Req(rdr, "UserId"));
        cmd.Parameters.AddWithValue("@LoginProvider", Req(rdr, "LoginProvider"));
        cmd.Parameters.AddWithValue("@Name",          Req(rdr, "Name"));
        cmd.Parameters.AddWithValue("@Value",         Nullable(rdr, "Value"));
    });
}

// ── Infrastructure helpers ────────────────────────────────────────────────────

/// <summary>Runs a single table migrator, skipping if destination already has data.</summary>
async Task<int> RunAsync(string tableName, Func<Task<int>> migrate)
{
    if (!force)
    {
        var destCount = await CountAsync(tableName);
        if (destCount > 0)
        {
            Console.WriteLine($"  ⚠  {tableName}: {destCount} row(s) already exist — skipped (use --force to overwrite)");
            return 0;
        }
    }
    try
    {
        var n = await migrate();
        Console.WriteLine($"  ✓  {tableName}: {n} row(s) migrated");
        return n;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗  {tableName}: ERROR — {ex.Message}");
        Console.ResetColor();
        throw;
    }
}

/// <summary>Core copy loop: reads every row from SQLite and inserts into SQL Server.</summary>
async Task<int> CopyRowsAsync(string selectSql, string insertSql,
    Action<SqliteDataReader, SqlCommand> bindParams)
{
    int count = 0;
    await using var selCmd = new SqliteCommand(selectSql, sqlite);
    await using var rdr    = await selCmd.ExecuteReaderAsync();
    while (await rdr.ReadAsync())
    {
        await using var insCmd = new SqlCommand(insertSql, sqlServer);
        bindParams(rdr, insCmd);
        await insCmd.ExecuteNonQueryAsync();
        count++;
    }
    return count;
}

async Task<bool> TableExistsAsync(string tableName)
{
    const string sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @t";
    await using var cmd = new SqlCommand(sql, sqlServer);
    cmd.Parameters.AddWithValue("@t", tableName);
    return (int)(await cmd.ExecuteScalarAsync())! > 0;
}

async Task<int> CountAsync(string tableName)
{
    await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM [{tableName}]", sqlServer);
    return (int)(await cmd.ExecuteScalarAsync())!;
}

async Task ExecSqlServerAsync(string sql)
{
    await using var cmd = new SqlCommand(sql, sqlServer);
    await cmd.ExecuteNonQueryAsync();
}

// ── SQLite value readers ──────────────────────────────────────────────────────

/// <summary>Required string column — throws if null.</summary>
static string Req(SqliteDataReader r, string col)
{
    var ord = r.GetOrdinal(col);
    if (r.IsDBNull(ord)) throw new InvalidDataException($"Unexpected NULL in required column '{col}'.");
    return r.GetString(ord);
}

/// <summary>Nullable string → DBNull.Value when null.</summary>
static object Nullable(SqliteDataReader r, string col)
{
    var ord = r.GetOrdinal(col);
    return r.IsDBNull(ord) ? DBNull.Value : (object)r.GetString(ord);
}

/// <summary>SQLite INTEGER (0/1) → bool.</summary>
static bool Bool(SqliteDataReader r, string col)
    => r.GetInt64(r.GetOrdinal(col)) != 0;

/// <summary>SQLite INTEGER → int.</summary>
static int Int(SqliteDataReader r, string col)
    => (int)r.GetInt64(r.GetOrdinal(col));

/// <summary>SQLite TEXT (ISO-8601 datetime) → DateTime (UTC).</summary>
static System.DateTime DateTime(SqliteDataReader r, string col)
{
    var s = r.GetString(r.GetOrdinal(col));
    return System.DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>Nullable SQLite TEXT (ISO-8601 datetime) → DBNull.Value when null.</summary>
static object NullableDateTime(SqliteDataReader r, string col)
{
    var ord = r.GetOrdinal(col);
    if (r.IsDBNull(ord)) return DBNull.Value;
    var s = r.GetString(ord);
    return (object)System.DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>Nullable SQLite TEXT (ISO-8601 datetimeoffset) → DBNull.Value when null.</summary>
static object NullableDateTimeOffset(SqliteDataReader r, string col)
{
    var ord = r.GetOrdinal(col);
    if (r.IsDBNull(ord)) return DBNull.Value;
    var s = r.GetString(ord);
    return (object)DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

// ── CLI helpers ───────────────────────────────────────────────────────────────

static string? GetArg(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static string Prompt(string message)
{
    Console.Write(message);
    return Console.ReadLine() ?? string.Empty;
}

static void Banner(string text)
{
    var bar = new string('─', text.Length);
    Console.WriteLine(bar);
    Console.WriteLine(text);
    Console.WriteLine(bar);
    Console.WriteLine();
}

static int Fatal(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: " + message);
    Console.ResetColor();
    return 1;
}

