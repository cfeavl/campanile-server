using Campanile.Server;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddCors(opzioni =>
{
    // In questa fase iniziale accetta chiamate da qualunque origine (la web app remota).
    // Da restringere al dominio vero della web app una volta pubblicata.
    opzioni.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
});

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<CampanileHub>("/hub/campanile");

app.MapGet("/api/stato", () => "Campanile server attivo.");

app.MapPost("/api/login", (RichiestaLogin richiesta, AuthService auth, IConfiguration config) =>
{
    var token = auth.Login(richiesta.Utente, richiesta.Password);
    if (token is null)
        return Results.Json(new { errore = "Utente o password non corretti." }, statusCode: 401);

    var sessione = auth.Valida(token)!;
    return Results.Ok(new { token, campanili = CampaniliCon(sessione.CampaniliConsentiti, config) });
});

app.MapGet("/api/me", (HttpRequest richiesta, AuthService auth, IConfiguration config) =>
{
    var sessione = ValidaRichiesta(richiesta, auth);
    if (sessione is null) return Results.Json(new { errore = "Sessione scaduta, rifai il login." }, statusCode: 401);
    return Results.Ok(new { utente = sessione.Utente, campanili = CampaniliCon(sessione.CampaniliConsentiti, config) });
});

// Endpoint chiamato dalla web app remota per far partire una diretta su un campanile specifico.
// Protetto: serve un token valido e l'utente deve avere il permesso su quel campanile.
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

app.Run();

static InfoSessione? ValidaRichiesta(HttpRequest richiesta, AuthService auth)
{
    var intestazione = richiesta.Headers.Authorization.ToString();
    var token = intestazione.StartsWith("Bearer ") ? intestazione["Bearer ".Length..] : null;
    return auth.Valida(token);
}

static List<object> CampaniliCon(List<string> id, IConfiguration config)
{
    var elenco = config.GetSection("Campanili").Get<List<InfoCampanile>>() ?? [];
    return id.Select(i =>
    {
        var info = elenco.FirstOrDefault(c => c.Id == i);
        return (object)new { id = i, nome = info?.Nome ?? i };
    }).ToList();
}

public record RichiestaLogin(string Utente, string Password);
public record InfoCampanile { public string Id { get; init; } = ""; public string Nome { get; init; } = ""; }
