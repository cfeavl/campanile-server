# Campanile.Server — guida rapida

## Cosa fa

Il server centrale: riceve i comandi dalla web app (o da un telefono) e li inoltra al
campanile giusto tramite una connessione permanente con l'app desktop. Include anche la
pagina web stessa (dentro `wwwroot/`), quindi basta pubblicare **questo solo servizio** —
niente hosting separato per la parte web.

## Come aggiungere un utente vero

Apri `appsettings.json` e modifica la sezione `Utenti`. Per generare la password in modo
sicuro (non va scritta in chiaro), chiedimi di calcolarti l'hash — ti basta dirmi la
password che vuoi usare, e ti preparo la riga pronta da incollare. Esempio di voce:

```json
{
  "Nome": "donmario",
  "PasswordHash": "....",
  "Salt": "....",
  "CampaniliConsentiti": [ "id-del-tuo-campanile" ]
}
```

Un utente può avere accesso a più campanili elencandoli tutti in `CampaniliConsentiti`.

## Come aggiungere/rinominare un campanile

Sezione `Campanili` dello stesso file:

```json
{ "Id": "id-del-tuo-campanile", "Nome": "Nome che vedi nella app" }
```

L'`Id` deve combaciare con quello che vedi nella finestra "Accesso remoto" (⚙) dell'app
desktop del campanile — è lì che viene generato automaticamente.

## Pubblicazione su Render (piano gratuito)

Render richiede Docker per le app .NET (non c'è un supporto "diretto"): ho già preparato
il file `Dockerfile` in questa cartella, quindi non devi scrivere nulla — segui solo
questi passaggi.

1. **Crea un account GitHub** (gratuito) su [github.com](https://github.com), se non ce l'hai già
2. Crea un **nuovo repository** (pulsante verde "New"), dagli un nome tipo `campanile-server`, lascialo pubblico o privato come preferisci
3. Nella pagina del repository appena creato, clicca **"uploading an existing file"** (o trascina i file) e carica **tutto il contenuto di questa cartella `Campanile.Server`** (incluso il `Dockerfile` e la cartella `wwwroot`) — non serve installare git, si può fare dal sito
4. Crea un account su [render.com](https://render.com) (puoi accedere direttamente con GitHub)
5. Su Render: **New → Web Service**, scegli **"Build and deploy from a Git repository"**, collega il repository appena creato
6. Render dovrebbe rilevare da solo il `Dockerfile` e proporre l'ambiente **Docker** — lascialo così
7. Scegli un nome per il servizio (diventerà parte dell'indirizzo, es. `campanile-tuaparrocchia`), piano **Free**, clicca **Create Web Service**
8. Aspetta che la build finisca (qualche minuto, vedi i log scorrere) — quando è pronto, in alto vedrai l'indirizzo tipo `https://campanile-tuaparrocchia.onrender.com`
9. Copia quell'indirizzo e incollalo nella finestra "Accesso remoto" (⚙) dell'app desktop del campanile

Nota sul piano gratuito: il server si "addormenta" dopo 15 minuti di inattività e la
prima richiesta dopo una pausa lunga impiega 30-50 secondi a rispondere. Per un servizio
sempre pronto si passa al piano Starter (7$/mese) senza cambiare nulla nel codice.

## Sicurezza

Gli utenti e le password non sono mai salvati "in chiaro": la password viene trasformata
con un procedimento (PBKDF2, 100.000 iterazioni) che non si può invertire. Anche vedendo
il file di configurazione, nessuno può risalire alla password originale.
