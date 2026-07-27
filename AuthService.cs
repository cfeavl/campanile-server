using System.Security.Cryptography;
using System.Text;

namespace Campanile.Server;

public class Utente
{
    public string Nome { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public List<string> CampaniliConsentiti { get; set; } = [];
}

public class InfoSessione
{
    public required string Utente { get; init; }
    public required List<string> CampaniliConsentiti { get; init; }
    public required DateTime Scadenza { get; init; }
}

/// <summary>
/// Gestisce login e verifica dei token. Gli utenti sono definiti in appsettings.json
/// (o nelle variabili d'ambiente su Render) — niente database da mantenere per una manciata
/// di persone, e niente rischio di perdere gli utenti se il server gratuito si riavvia.
/// I token di sessione invece vivono solo in memoria: se il server riavvia, tutti devono
/// rifare login (accettabile per questo utilizzo, non serve altro).
/// </summary>
public class AuthService(IConfiguration configurazione)
{
    private readonly Dictionary<string, InfoSessione> _sessioni = new();
    private readonly object _lucchetto = new();

    public string? Login(string nomeUtente, string password)
    {
        var utenti = configurazione.GetSection("Utenti").Get<List<Utente>>() ?? [];
        var utente = utenti.FirstOrDefault(u => string.Equals(u.Nome, nomeUtente, StringComparison.OrdinalIgnoreCase));
        if (utente is null) return null;

        if (!VerificaPassword(password, utente.Salt, utente.PasswordHash))
            return null;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sessione = new InfoSessione
        {
            Utente = utente.Nome,
            CampaniliConsentiti = utente.CampaniliConsentiti,
            Scadenza = DateTime.UtcNow.AddDays(30), // lungo apposta: non vogliamo che il parroco debba rifare login spesso
        };

        lock (_lucchetto) { _sessioni[token] = sessione; }
        return token;
    }

    public InfoSessione? Valida(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_lucchetto)
        {
            if (_sessioni.TryGetValue(token, out var sessione) && sessione.Scadenza > DateTime.UtcNow)
                return sessione;
        }
        return null;
    }

    public static (string hash, string salt) GeneraHashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    private static bool VerificaPassword(string password, string saltBase64, string hashAtteso)
    {
        var saltBytes = Convert.FromBase64String(saltBase64);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hashBytes, Convert.FromBase64String(hashAtteso));
    }
}
