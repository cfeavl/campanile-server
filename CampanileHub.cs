using Microsoft.AspNetCore.SignalR;

namespace Campanile.Server;

/// <summary>
/// Ogni campanile (l'app desktop) si collega qui ed entra in un "gruppo" con il proprio
/// identificativo. Chi vuole far suonare una diretta da remoto chiama l'endpoint HTTP
/// /api/campanili/{id}/diretta, che a sua volta manda il comando a tutti i client connessi
/// in quel gruppo. Le pagine web che vogliono "seguire" lo stato di un campanile (per
/// mostrare il nome della suonata in corso e la barra di avanzamento) entrano nello stesso
/// gruppo tramite AscoltaCampanile, in sola lettura.
/// </summary>
public class CampanileHub(StatoCampanili statoCampanili) : Hub
{
    /// <summary>Chiamato dall'app desktop del campanile appena si connette, per "iscriversi"
    /// al proprio canale usando l'identificativo del campanile.</summary>
    public async Task RegistraCampanile(string idCampanile)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GruppoPer(idCampanile));
    }

    /// <summary>Chiamato dalla web app per ricevere in tempo reale lo stato di un campanile
    /// (suonata in corso, ecc.) senza poter inviare comandi — la sicurezza vera sui comandi
    /// resta sugli endpoint HTTP protetti da token.</summary>
    public async Task AscoltaCampanile(string idCampanile)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GruppoPer(idCampanile));
    }

    /// <summary>Chiamato dall'app desktop per comunicare i nomi attuali delle 5 dirette,
    /// così la web app può mostrare "Angelus" invece di "Diretta 1".</summary>
    public void AggiornaDirette(string idCampanile, string[] nomi)
    {
        statoCampanili.ImpostaNomiDirette(idCampanile, nomi);
    }

    /// <summary>Chiamato dall'app desktop quando inizia a suonare qualcosa (sia per un
    /// comando remoto sia per un pulsante premuto lì fisicamente), per far comparire
    /// nella web app il nome e la barra di avanzamento.</summary>
    public async Task NotificaSuonataAvviata(string idCampanile, string nomeSuonata, double durataSecondi)
    {
        await Clients.Group(GruppoPer(idCampanile)).SendAsync("StatoSuonataAvviata", idCampanile, nomeSuonata, durataSecondi);
    }

    /// <summary>Chiamato dall'app desktop quando la suonata finisce o viene fermata.</summary>
    public async Task NotificaSuonataFerma(string idCampanile)
    {
        await Clients.Group(GruppoPer(idCampanile)).SendAsync("StatoSuonataFerma", idCampanile);
    }

    public static string GruppoPer(string idCampanile) => $"campanile-{idCampanile}";
}

/// <summary>Tiene in memoria, per ogni campanile connesso, i nomi delle 5 dirette e l'ultimo
/// file audio suonato (per la funzione "ascolta in diretta"). Non serve salvarlo su disco:
/// se il server riavvia, l'app desktop rimanda tutto appena si ricollega o suona di nuovo.</summary>
public class StatoCampanili
{
    private readonly Dictionary<string, string[]> _nomiDirette = new();
    private readonly Dictionary<string, (byte[] Dati, string TipoContenuto)> _audioInCorso = new();
    private readonly object _lucchetto = new();

    public void ImpostaNomiDirette(string idCampanile, string[] nomi)
    {
        lock (_lucchetto) { _nomiDirette[idCampanile] = nomi; }
    }

    public string[] LeggiNomiDirette(string idCampanile)
    {
        lock (_lucchetto)
        {
            return _nomiDirette.TryGetValue(idCampanile, out var nomi)
                ? nomi
                : ["Diretta 1", "Diretta 2", "Diretta 3", "Diretta 4", "Diretta 5"];
        }
    }

    public void ImpostaAudioInCorso(string idCampanile, byte[] dati, string tipoContenuto)
    {
        lock (_lucchetto) { _audioInCorso[idCampanile] = (dati, tipoContenuto); }
    }

    public (byte[] Dati, string TipoContenuto)? LeggiAudioInCorso(string idCampanile)
    {
        lock (_lucchetto)
        {
            return _audioInCorso.TryGetValue(idCampanile, out var audio) ? audio : null;
        }
    }
}
