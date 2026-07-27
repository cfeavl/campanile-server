using Campanile.Server;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Nel container di Render, tenere sotto controllo il file di configurazione per eventuali
// modifiche (comportamento di default di ASP.NET Core) va in conflitto con un limite di sistema
// (inotify) e manda in crash il programma all'avvio. Non ci serve comunque, quindi lo disattivo.
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.AddSignalR();
builder.Services.AddCors(opzioni =>
{
    opzioni.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
});

var stringaConnessioneGrezza = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Manca la stringa di connessione 'ConnectionStrings:Postgres'.");
var stringaConnessione = ConvertiStringaConnessione(stringaConnessioneGrezza);

var database = new DatabaseUtenti(stringaConnessione);
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<AuthService>();

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// All'avvio: crea le tabelle se non esistono, e assicura che esista almeno un amministratore
// (preso dalla configurazione "AdminIniziale" — serve solo la primissima volta).
Console.WriteLine($"[Campanile] Stringa di connessione letta, lunghezza={stringaConnessione.Length}, inizia con 'postgresql://'={stringaConnessione.StartsWith("postgresql://")}");

try
{
    Console.WriteLine("[Campanile] Avvio AssicuraTabelleAsync...");
    await database.AssicuraTabelleAsync();
    Console.WriteLine("[Campanile] Tabelle assicurate con successo.");
}
catch (Exception ex)
{
    Console.WriteLine($"[Campanile] ERRORE in AssicuraTabelleAsync: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

var nomeAdmin = app.Configuration["AdminIniziale:Nome"];
var passwordAdmin = app.Configuration["AdminIniziale:Password"];
Console.WriteLine($"[Campanile] AdminIniziale:Nome presente={!string.IsNullOrWhiteSpace(nomeAdmin)}, AdminIniziale:Password presente={!string.IsNullOrWhiteSpace(passwordAdmin)}");

if (!string.IsNullOrWhiteSpace(nomeAdmin) && !string.IsNullOrWhiteSpace(passwordAdmin))
{
    try
    {
        var (hash, salt) = AuthService.GeneraHashPassword(passwordAdmin);
        await database.AssicuraAdminInizialeAsync(nomeAdmin, hash, salt);
        Console.WriteLine($"[Campanile] Admin iniziale assicurato per l'utente '{nomeAdmin}'.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Campanile] ERRORE in AssicuraAdminInizialeAsync: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

app.MapHub<CampanileHub>("/hub/campanile");

app.MapGet("/api/stato", () => "Campanile server attivo.");

app.MapPost("/api/login", async (RichiestaLogin richiesta, AuthService auth, DatabaseUtenti db) =>
{
    var token = await auth.LoginAsync(richiesta.Utente, richiesta.Password);
    if (token is null)
        return Results.Json(new { errore = "Utente o password non corretti." }, statusCode: 401);

    var sessione = auth.Valida(token)!;
    var campanili = await db.LeggiCampaniliAsync(sessione.CampaniliConsentiti);
    return Results.Ok(new { token, admin = sessione.Admin, campanili });
});

app.MapGet("/api/me", async (HttpRequest richiesta, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiesta(richiesta, auth);
    if (sessione is null) return Results.Json(new { errore = "Sessione scaduta, rifai il login." }, statusCode: 401);
    var campanili = await db.LeggiCampaniliAsync(sessione.CampaniliConsentiti);
    return Results.Ok(new { utente = sessione.Utente, admin = sessione.Admin, campanili });
});

// Endpoint chiamato dalla web app remota per far partire una diretta su un campanile specifico.
app.MapPost("/api/campanili/{idCampanile}/diretta/{numeroDiretta:int}",
    async (string idCampanile, int numeroDiretta, HttpRequest richiesta, AuthService auth, IHubContext<CampanileHub> hub) =>
    {
        var sessione = ValidaRichiesta(richiesta, auth);
        if (sessione is null)
            return Results.Json(new { errore = "Sessione scaduta, rifai il login." }, statusCode: 401);

        if (!sessione.CampaniliConsentiti.Contains(idCampanile))
            return Results.Json(new { errore = "Non hai accesso a questo campanile." }, statusCode: 403);

        await hub.Clients.Group(CampanileHub.GruppoPer(idCampanile)).SendAsync("SuonaDiretta", numeroDiretta);
        return Results.Ok(new { inviato = true, idCampanile, numeroDiretta });
    });

// ===================== PANNELLO DI AMMINISTRAZIONE (solo utenti admin) =====================

app.MapGet("/api/admin/utenti", async (HttpRequest richiesta, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiestaAdmin(richiesta, auth);
    if (sessione is null) return Results.Json(new { errore = "Non autorizzato." }, statusCode: 403);
    return Results.Ok(await db.LeggiTuttiUtentiAsync());
});

app.MapGet("/api/admin/campanili", async (HttpRequest richiesta, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiestaAdmin(richiesta, auth);
    if (sessione is null) return Results.Json(new { errore = "Non autorizzato." }, statusCode: 403);
    return Results.Ok(await db.LeggiCampaniliAsync());
});

app.MapPost("/api/admin/campanili", async (RichiestaCampanile richiesta, HttpRequest http, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiestaAdmin(http, auth);
    if (sessione is null) return Results.Json(new { errore = "Non autorizzato." }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(richiesta.Id) || string.IsNullOrWhiteSpace(richiesta.Nome))
        return Results.Json(new { errore = "Id e nome sono obbligatori." }, statusCode: 400);

    await db.AggiungiCampanileAsync(richiesta.Id.Trim(), richiesta.Nome.Trim());
    return Results.Ok(new { fatto = true });
});

app.MapPost("/api/admin/utenti", async (RichiestaNuovoUtente richiesta, HttpRequest http, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiestaAdmin(http, auth);
    if (sessione is null) return Results.Json(new { errore = "Non autorizzato." }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(richiesta.Nome) || string.IsNullOrWhiteSpace(richiesta.Password))
        return Results.Json(new { errore = "Nome utente e password sono obbligatori." }, statusCode: 400);

    var (hash, salt) = AuthService.GeneraHashPassword(richiesta.Password);
    await db.AggiungiUtenteAsync(richiesta.Nome.Trim(), hash, salt, richiesta.CampaniliConsentiti ?? []);
    return Results.Ok(new { fatto = true });
});

app.MapDelete("/api/admin/utenti/{nome}", async (string nome, HttpRequest http, AuthService auth, DatabaseUtenti db) =>
{
    var sessione = ValidaRichiestaAdmin(http, auth);
    if (sessione is null) return Results.Json(new { errore = "Non autorizzato." }, statusCode: 403);

    await db.EliminaUtenteAsync(nome);
    return Results.Ok(new { fatto = true });
});

app.Run();

static InfoSessione? ValidaRichiesta(HttpRequest richiesta, AuthService auth)
{
    var intestazione = richiesta.Headers.Authorization.ToString();
    var token = intestazione.StartsWith("Bearer ") ? intestazione["Bearer ".Length..] : null;
    return auth.Valida(token);
}

/// <summary>
/// Neon (e molti altri servizi Postgres) forniscono l'indirizzo nel formato "URL"
/// (postgresql://utente:password@host/database) — ma Npgsql vuole invece il formato classico
/// "chiave=valore;". Questa funzione converte automaticamente dal primo formato al secondo,
/// così basta incollare la stringa di Neon così com'è, senza doverla riscrivere a mano.
/// </summary>
static string ConvertiStringaConnessione(string valore)
{
    if (!valore.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !valore.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return valore; // è già nel formato "chiave=valore;" che Npgsql si aspetta
    }

    var uri = new Uri(valore);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var nomeDatabase = uri.AbsolutePath.TrimStart('/');

    var costruttore = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = username,
        Password = password,
        Database = nomeDatabase,
        SslMode = SslMode.Require,
    };

    return costruttore.ConnectionString;
}

static InfoSessione? ValidaRichiestaAdmin(HttpRequest richiesta, AuthService auth)
{
    var sessione = ValidaRichiesta(richiesta, auth);
    return sessione is { Admin: true } ? sessione : null;
}

public record RichiestaLogin(string Utente, string Password);
public record RichiestaCampanile(string Id, string Nome);
public record RichiestaNuovoUtente(string Nome, string Password, List<string>? CampaniliConsentiti);
