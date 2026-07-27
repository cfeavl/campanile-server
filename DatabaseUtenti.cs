using Npgsql;

namespace Campanile.Server;

public class Utente
{
    public string Nome { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public bool Admin { get; set; }
    public List<string> CampaniliConsentiti { get; set; } = [];
}

public class InfoCampanile
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
}

/// <summary>
/// Tutto l'accesso al database Postgres (Neon): crea le tabelle se non esistono ancora,
/// legge/scrive utenti e campanili. Sostituisce la vecchia lettura da appsettings.json,
/// così si possono aggiungere utenti senza toccare codice o GitHub.
/// </summary>
public class DatabaseUtenti(string stringaConnessione)
{
    public async Task AssicuraTabelleAsync()
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS campanili (
                id TEXT PRIMARY KEY,
                nome TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS utenti (
                nome TEXT PRIMARY KEY,
                password_hash TEXT NOT NULL,
                salt TEXT NOT NULL,
                admin BOOLEAN NOT NULL DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS utente_campanile (
                utente TEXT NOT NULL REFERENCES utenti(nome) ON DELETE CASCADE,
                campanile_id TEXT NOT NULL REFERENCES campanili(id) ON DELETE CASCADE,
                PRIMARY KEY (utente, campanile_id)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Se non esiste ancora nessun amministratore, ne crea uno con i dati indicati
    /// (usato al primo avvio, per non restare mai senza modo di entrare nel pannello admin).</summary>
    public async Task AssicuraAdminInizialeAsync(string nome, string passwordHash, string salt)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using (var controllo = conn.CreateCommand())
        {
            controllo.CommandText = "SELECT COUNT(*) FROM utenti WHERE admin = TRUE;";
            var conteggio = (long)(await controllo.ExecuteScalarAsync() ?? 0L);
            if (conteggio > 0) return;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO utenti (nome, password_hash, salt, admin)
            VALUES (@nome, @hash, @salt, TRUE)
            ON CONFLICT (nome) DO UPDATE SET admin = TRUE;
            """;
        cmd.Parameters.AddWithValue("@nome", nome);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        cmd.Parameters.AddWithValue("@salt", salt);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Utente?> TrovaUtenteAsync(string nome)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.nome, u.password_hash, u.salt, u.admin,
                   COALESCE(array_agg(uc.campanile_id) FILTER (WHERE uc.campanile_id IS NOT NULL), '{}')
            FROM utenti u
            LEFT JOIN utente_campanile uc ON uc.utente = u.nome
            WHERE u.nome = @nome
            GROUP BY u.nome, u.password_hash, u.salt, u.admin;
            """;
        cmd.Parameters.AddWithValue("@nome", nome);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Utente
        {
            Nome = reader.GetString(0),
            PasswordHash = reader.GetString(1),
            Salt = reader.GetString(2),
            Admin = reader.GetBoolean(3),
            CampaniliConsentiti = ((string[])reader.GetValue(4)).ToList(),
        };
    }

    public async Task<List<InfoCampanile>> LeggiCampaniliAsync(IEnumerable<string>? soloQuestiId = null)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        if (soloQuestiId is null)
        {
            cmd.CommandText = "SELECT id, nome FROM campanili ORDER BY nome;";
        }
        else
        {
            cmd.CommandText = "SELECT id, nome FROM campanili WHERE id = ANY(@ids) ORDER BY nome;";
            cmd.Parameters.AddWithValue("@ids", soloQuestiId.ToArray());
        }

        var risultato = new List<InfoCampanile>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            risultato.Add(new InfoCampanile { Id = reader.GetString(0), Nome = reader.GetString(1) });
        return risultato;
    }

    public async Task<List<Utente>> LeggiTuttiUtentiAsync()
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.nome, u.admin,
                   COALESCE(array_agg(uc.campanile_id) FILTER (WHERE uc.campanile_id IS NOT NULL), '{}')
            FROM utenti u
            LEFT JOIN utente_campanile uc ON uc.utente = u.nome
            GROUP BY u.nome, u.admin
            ORDER BY u.nome;
            """;

        var risultato = new List<Utente>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            risultato.Add(new Utente
            {
                Nome = reader.GetString(0),
                Admin = reader.GetBoolean(1),
                CampaniliConsentiti = ((string[])reader.GetValue(2)).ToList(),
            });
        }
        return risultato;
    }

    public async Task AggiungiCampanileAsync(string id, string nome)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO campanili (id, nome) VALUES (@id, @nome)
            ON CONFLICT (id) DO UPDATE SET nome = excluded.nome;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@nome", nome);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AggiungiUtenteAsync(string nome, string passwordHash, string salt, IEnumerable<string> campaniliConsentiti)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();
        await using var transazione = await conn.BeginTransactionAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transazione;
            cmd.CommandText = """
                INSERT INTO utenti (nome, password_hash, salt, admin)
                VALUES (@nome, @hash, @salt, FALSE)
                ON CONFLICT (nome) DO UPDATE SET password_hash = excluded.password_hash, salt = excluded.salt;
                """;
            cmd.Parameters.AddWithValue("@nome", nome);
            cmd.Parameters.AddWithValue("@hash", passwordHash);
            cmd.Parameters.AddWithValue("@salt", salt);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transazione;
            cmd.CommandText = "DELETE FROM utente_campanile WHERE utente = @nome;";
            cmd.Parameters.AddWithValue("@nome", nome);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var idCampanile in campaniliConsentiti)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transazione;
            cmd.CommandText = "INSERT INTO utente_campanile (utente, campanile_id) VALUES (@nome, @id);";
            cmd.Parameters.AddWithValue("@nome", nome);
            cmd.Parameters.AddWithValue("@id", idCampanile);
            await cmd.ExecuteNonQueryAsync();
        }

        await transazione.CommitAsync();
    }

    public async Task EliminaUtenteAsync(string nome)
    {
        await using var conn = new NpgsqlConnection(stringaConnessione);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM utenti WHERE nome = @nome AND admin = FALSE;"; // non si elimina mai un admin da qui
        cmd.Parameters.AddWithValue("@nome", nome);
        await cmd.ExecuteNonQueryAsync();
    }
}
