using Microsoft.AspNetCore.SignalR;

namespace Campanile.Server;

/// <summary>
/// Ogni campanile (l'app desktop) si collega qui ed entra in un "gruppo" con il proprio
/// identificativo. Chi vuole far suonare una diretta da remoto chiama l'endpoint HTTP
/// /api/campanili/{id}/diretta, che a sua volta manda il comando a tutti i client connessi
/// in quel gruppo (in pratica, solo il campanile giusto).
/// </summary>
public class CampanileHub : Hub
{
    /// <summary>Chiamato dall'app desktop del campanile appena si connette, per "iscriversi"
    /// al proprio canale usando l'identificativo del campanile.</summary>
    public async Task RegistraCampanile(string idCampanile)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GruppoPer(idCampanile));
    }

    public static string GruppoPer(string idCampanile) => $"campanile-{idCampanile}";
}
