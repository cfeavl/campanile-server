// URL del server: la web app è servita dallo stesso server delle API, quindi basta usare
// un percorso relativo — funziona automaticamente sia in locale che una volta pubblicata.
const BASE_URL = "";

const schermataLogin = document.getElementById("schermataLogin");
const schermataPrincipale = document.getElementById("schermataPrincipale");
const campoUtente = document.getElementById("campoUtente");
const campoPassword = document.getElementById("campoPassword");
const bottoneLogin = document.getElementById("bottoneLogin");
const erroreLogin = document.getElementById("erroreLogin");
const testoUtente = document.getElementById("testoUtente");
const bottoneEsci = document.getElementById("bottoneEsci");
const elencoCampanili = document.getElementById("elencoCampanili");

function mostraSchermata(schermata) {
  document.querySelectorAll(".schermata").forEach(s => s.classList.remove("attiva"));
  schermata.classList.add("attiva");
}

function salvaSessione(token, utente, campanili, admin) {
  localStorage.setItem("campanile_token", token);
  localStorage.setItem("campanile_utente", utente);
  localStorage.setItem("campanile_campanili", JSON.stringify(campanili));
  localStorage.setItem("campanile_admin", admin ? "1" : "0");
}

function leggiSessione() {
  const token = localStorage.getItem("campanile_token");
  if (!token) return null;
  return {
    token,
    utente: localStorage.getItem("campanile_utente"),
    campanili: JSON.parse(localStorage.getItem("campanile_campanili") || "[]"),
    admin: localStorage.getItem("campanile_admin") === "1",
  };
}

function esci() {
  localStorage.removeItem("campanile_token");
  localStorage.removeItem("campanile_utente");
  localStorage.removeItem("campanile_campanili");
  localStorage.removeItem("campanile_admin");
  mostraSchermata(schermataLogin);
}

async function accedi() {
  const utente = campoUtente.value.trim();
  const password = campoPassword.value;
  erroreLogin.textContent = "";

  if (!utente || !password) {
    erroreLogin.textContent = "Inserisci utente e password.";
    return;
  }

  bottoneLogin.disabled = true;
  bottoneLogin.textContent = "Accesso in corso...";

  try {
    const risposta = await fetch(`${BASE_URL}/api/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ utente, password }),
    });

    if (!risposta.ok) {
      const dati = await risposta.json().catch(() => ({}));
      erroreLogin.textContent = dati.errore || "Accesso non riuscito.";
      return;
    }

    const dati = await risposta.json();
    salvaSessione(dati.token, utente, dati.campanili, dati.admin);
    await mostraSchermataPrincipale(utente, dati.campanili, dati.admin);
  } catch {
    erroreLogin.textContent = "Impossibile contattare il server. Controlla la connessione.";
  } finally {
    bottoneLogin.disabled = false;
    bottoneLogin.textContent = "Accedi";
  }
}

const timerBarra = {}; // idCampanile -> { intervalId, secondiTotali, secondiTrascorsi }

async function mostraSchermataPrincipale(utente, campanili, admin) {
  testoUtente.textContent = `Ciao, ${utente}`;
  elencoCampanili.innerHTML = "";

  const sessione = leggiSessione();

  for (const campanile of campanili) {
    const card = document.createElement("div");
    card.className = "card-campanile";

    const titolo = document.createElement("h2");
    titolo.innerHTML = `🔔 ${campanile.nome}`;
    card.appendChild(titolo);

    // Barra "sta suonando", nascosta finché non arriva davvero qualcosa.
    const statoRiproduzione = document.createElement("div");
    statoRiproduzione.className = "stato-riproduzione";
    statoRiproduzione.id = `stato-${campanile.id}`;
    statoRiproduzione.innerHTML = `
      <div class="intestazione">
        <span class="nome-suonata"></span>
        <span class="tempo">0:00 / 0:00</span>
      </div>
      <div class="barra-progresso"><div class="riempimento"></div></div>
      <div class="barra-azioni">
        <button class="btn-ascolta">🔇 Ascolta dal vivo</button>
        <button class="btn-ferma">⏹ Ferma suono</button>
      </div>
      <audio class="audio-diretta" style="display:none;"></audio>
    `;
    statoRiproduzione.querySelector(".btn-ferma").addEventListener("click", () => fermaSuono(campanile.id));
    statoRiproduzione.querySelector(".btn-ascolta").addEventListener("click", () => alternaAscolto(campanile.id));
    card.appendChild(statoRiproduzione);

    // Nomi veri delle dirette (es. "Angelus"), con un'etichetta generica finché l'app
    // desktop non li ha ancora comunicati almeno una volta.
    let nomiDirette = ["Diretta 1", "Diretta 2", "Diretta 3", "Diretta 4", "Diretta 5"];
    if (sessione) {
      try {
        const rispostaNomi = await fetch(`${BASE_URL}/api/campanili/${campanile.id}/dirette`, {
          headers: { Authorization: `Bearer ${sessione.token}` },
        });
        if (rispostaNomi.ok) nomiDirette = await rispostaNomi.json();
      } catch { /* resta l'etichetta generica */ }
    }

    const griglia = document.createElement("div");
    griglia.className = "griglia-dirette";

    for (let numero = 0; numero < 5; numero++) {
      const bottone = document.createElement("button");
      bottone.className = "btn-diretta";
      bottone.innerHTML = `🔔 ${nomiDirette[numero] || `Diretta ${numero + 1}`}`;
      bottone.addEventListener("click", () => inviaComando(campanile.id, numero, bottone));
      griglia.appendChild(bottone);
    }

    card.appendChild(griglia);
    elencoCampanili.appendChild(card);
  }

  const sezioneAdmin = document.getElementById("sezioneAdmin");
  if (admin) {
    sezioneAdmin.style.display = "block";
    caricaPannelloAdmin();
  } else {
    sezioneAdmin.style.display = "none";
  }

  mostraSchermata(schermataPrincipale);
  await avviaAscoltoStatoLive(campanili);
}

const statoMostrato = {}; // idCampanile -> nome della suonata attualmente mostrata sulla barra
let intervalloControllo = null;

/// Controlla periodicamente (invece di affidarsi a una connessione "sempre aperta", che sui
/// cellulari può addormentarsi o interrompersi) se sta suonando qualcosa su uno dei campanili
/// a cui l'utente ha accesso, e aggiorna la barra di avanzamento di conseguenza.
async function avviaAscoltoStatoLive(campanili) {
  const sessione = leggiSessione();
  if (!sessione || campanili.length === 0) return;

  if (intervalloControllo) clearInterval(intervalloControllo);

  const controlla = async () => {
    for (const campanile of campanili) {
      try {
        const risposta = await fetch(`${BASE_URL}/api/campanili/${campanile.id}/stato`, {
          headers: { Authorization: `Bearer ${sessione.token}` },
        });
        if (!risposta.ok) continue;
        const dati = await risposta.json();

        if (dati.inCorso) {
          if (statoMostrato[campanile.id] !== dati.nome) {
            statoMostrato[campanile.id] = dati.nome;
            avviaBarraProgresso(campanile.id, dati.nome, dati.durataSecondi, dati.secondiTrascorsi);
          }
        } else if (statoMostrato[campanile.id]) {
          delete statoMostrato[campanile.id];
          fermaBarraProgresso(campanile.id);
        }
      } catch { /* va bene, si riprova al giro successivo tra poco */ }
    }
  };

  await controlla(); // subito, senza aspettare il primo intervallo
  intervalloControllo = setInterval(controlla, 1500);
}

function formattaTempo(secondiTotali) {
  const s = Math.max(0, Math.round(secondiTotali));
  const minuti = Math.floor(s / 60);
  const secondi = s % 60;
  return `${minuti}:${secondi.toString().padStart(2, "0")}`;
}

const ascoltoAttivo = {}; // idCampanile -> true/false
const urlAudioAttivi = {}; // idCampanile -> URL temporaneo dell'audio in corso, da liberare dopo

function alternaAscolto(idCampanile) {
  ascoltoAttivo[idCampanile] = !ascoltoAttivo[idCampanile];
  const contenitore = document.getElementById(`stato-${idCampanile}`);
  if (!contenitore) return;

  const bottone = contenitore.querySelector(".btn-ascolta");
  if (ascoltoAttivo[idCampanile]) {
    bottone.textContent = "🔊 In ascolto";
    bottone.classList.add("attivo");

    // Se una suonata è già in corso in questo momento, aggancia subito l'ascolto,
    // partendo dal punto in cui si trova adesso — non serve aspettare la prossima.
    if (timerBarra[idCampanile]) {
      avviaAscoltoAudio(idCampanile);
    }
  } else {
    bottone.textContent = "🔇 Ascolta dal vivo";
    bottone.classList.remove("attivo");
    contenitore.querySelector(".audio-diretta").pause();
  }
}

async function avviaAscoltoAudio(idCampanile) {
  const sessione = leggiSessione();
  const contenitore = document.getElementById(`stato-${idCampanile}`);
  if (!sessione || !contenitore) return;

  try {
    // Il file audio viene caricato dall'app desktop in parallelo alla notifica "è partita una
    // suonata", quindi può volerci un attimo prima che sia pronto: si riprova qualche volta
    // prima di rinunciare, invece di dare subito errore.
    let risposta = null;
    for (let tentativo = 0; tentativo < 6; tentativo++) {
      risposta = await fetch(`${BASE_URL}/api/campanili/${idCampanile}/audio-in-corso?t=${Date.now()}`, {
        headers: { Authorization: `Bearer ${sessione.token}` },
      });
      if (risposta.ok) break;
      await new Promise(r => setTimeout(r, 300));
    }
    if (!risposta || !risposta.ok) return;

    const blob = await risposta.blob();
    const url = URL.createObjectURL(blob);
    urlAudioAttivi[idCampanile] = url;

    const elementoAudio = contenitore.querySelector(".audio-diretta");
    elementoAudio.src = url;

    // Se la suonata era già a metà quando è partito l'ascolto, salta al punto giusto
    // invece di ripartire dall'inizio.
    const info = timerBarra[idCampanile];
    if (info) {
      const trascorsi = (Date.now() - info.inizio) / 1000;
      elementoAudio.addEventListener("loadedmetadata", () => {
        elementoAudio.currentTime = Math.min(trascorsi, elementoAudio.duration || trascorsi);
      }, { once: true });
    }

    elementoAudio.play().catch(() => { /* il browser potrebbe bloccarlo se manca un'interazione recente: non grave */ });
  } catch { /* connessione instabile: niente audio questa volta, il resto continua a funzionare */ }
}

function avviaBarraProgresso(idCampanile, nomeSuonata, durataSecondi, secondiGiaTrascorsi = 0) {
  const contenitore = document.getElementById(`stato-${idCampanile}`);
  if (!contenitore) return;

  fermaBarraProgresso(idCampanile); // nel caso ce ne fosse già una in corso

  contenitore.classList.add("attivo");
  contenitore.querySelector(".nome-suonata").textContent = nomeSuonata;
  const riempimento = contenitore.querySelector(".riempimento");
  const tempo = contenitore.querySelector(".tempo");
  riempimento.style.width = `${Math.min(100, (secondiGiaTrascorsi / durataSecondi) * 100)}%`;
  tempo.textContent = `${formattaTempo(secondiGiaTrascorsi)} / ${formattaTempo(durataSecondi)}`;

  if (ascoltoAttivo[idCampanile]) {
    avviaAscoltoAudio(idCampanile);
  }

  // Se l'abbiamo scoperta già a metà (grazie al controllo periodico, non a una notifica
  // istantanea), il "punto di partenza" del conteggio va spostato indietro di conseguenza,
  // così il tempo mostrato resta corretto fin da subito.
  const inizio = Date.now() - secondiGiaTrascorsi * 1000;
  const intervalId = setInterval(() => {
    const trascorsi = (Date.now() - inizio) / 1000;
    const percentuale = Math.min(100, (trascorsi / durataSecondi) * 100);
    riempimento.style.width = `${percentuale}%`;
    tempo.textContent = `${formattaTempo(trascorsi)} / ${formattaTempo(durataSecondi)}`;

    if (trascorsi >= durataSecondi) {
      fermaBarraProgresso(idCampanile);
    }
  }, 250);

  timerBarra[idCampanile] = { intervalId, inizio };
}

function fermaBarraProgresso(idCampanile) {
  const precedente = timerBarra[idCampanile];
  if (precedente) {
    clearInterval(precedente.intervalId);
    delete timerBarra[idCampanile];
  }

  const contenitore = document.getElementById(`stato-${idCampanile}`);
  if (contenitore) {
    contenitore.classList.remove("attivo");
    const elementoAudio = contenitore.querySelector(".audio-diretta");
    elementoAudio.pause();
    elementoAudio.removeAttribute("src");
  }

  if (urlAudioAttivi[idCampanile]) {
    URL.revokeObjectURL(urlAudioAttivi[idCampanile]);
    delete urlAudioAttivi[idCampanile];
  }
}

async function fermaSuono(idCampanile) {
  const sessione = leggiSessione();
  if (!sessione) { esci(); return; }

  // Feedback immediato: non aspettare il prossimo controllo periodico (fino a 1,5 secondi)
  // per far sparire la barra, visto che è stato l'utente stesso a chiedere di fermarla.
  delete statoMostrato[idCampanile];
  fermaBarraProgresso(idCampanile);

  try {
    await fetch(`${BASE_URL}/api/campanili/${idCampanile}/ferma`, {
      method: "POST",
      headers: { Authorization: `Bearer ${sessione.token}` },
    });
  } catch { /* la barra resta comunque nascosta lato telefono; il campanile riceverà il comando appena si ricollega */ }
}

async function chiamataAutenticata(percorso, opzioni = {}) {
  const sessione = leggiSessione();
  if (!sessione) { esci(); return null; }

  const risposta = await fetch(`${BASE_URL}${percorso}`, {
    ...opzioni,
    headers: { ...(opzioni.headers || {}), Authorization: `Bearer ${sessione.token}` },
  });

  if (risposta.status === 401 || risposta.status === 403) {
    if (risposta.status === 401) esci();
    return null;
  }
  return risposta;
}

async function caricaPannelloAdmin() {
  const rispostaCampanili = await chiamataAutenticata("/api/admin/campanili");
  const campanili = rispostaCampanili ? await rispostaCampanili.json() : [];

  const listaCampaniliDiv = document.getElementById("listaCampaniliDisponibili");
  listaCampaniliDiv.innerHTML = "";
  for (const c of campanili) {
    const riga = document.createElement("label");
    riga.style.fontWeight = "normal";
    riga.style.display = "flex";
    riga.style.alignItems = "center";
    riga.style.gap = "8px";
    riga.innerHTML = `<input type="checkbox" value="${c.id}" style="width:auto;" /> ${c.nome} (${c.id})`;
    listaCampaniliDiv.appendChild(riga);
  }

  const rispostaUtenti = await chiamataAutenticata("/api/admin/utenti");
  const utenti = rispostaUtenti ? await rispostaUtenti.json() : [];

  const listaUtentiDiv = document.getElementById("listaUtenti");
  listaUtentiDiv.innerHTML = "";
  for (const u of utenti) {
    const riga = document.createElement("div");
    riga.style.display = "flex";
    riga.style.justifyContent = "space-between";
    riga.style.alignItems = "center";
    riga.style.padding = "8px 0";
    riga.style.borderBottom = "1px solid var(--bordo)";
    const campaniliUtente = u.campaniliConsentiti?.join(", ") || "nessuno";
    riga.innerHTML = `<span>${u.nome}${u.admin ? " (admin)" : ""} — ${campaniliUtente}</span>`;

    if (!u.admin) {
      const bottoneElimina = document.createElement("button");
      bottoneElimina.textContent = "Elimina";
      bottoneElimina.className = "btn-secondario";
      bottoneElimina.style.width = "auto";
      bottoneElimina.style.padding = "6px 12px";
      bottoneElimina.addEventListener("click", async () => {
        await chiamataAutenticata(`/api/admin/utenti/${encodeURIComponent(u.nome)}`, { method: "DELETE" });
        caricaPannelloAdmin();
      });
      riga.appendChild(bottoneElimina);
    }
    listaUtentiDiv.appendChild(riga);
  }
}

document.getElementById("bottoneAggiungiCampanile").addEventListener("click", async () => {
  const id = document.getElementById("nuovoCampanileId").value.trim();
  const nome = document.getElementById("nuovoCampanileNome").value.trim();
  if (!id || !nome) { alert("Compila sia l'identificativo che il nome."); return; }

  const risposta = await chiamataAutenticata("/api/admin/campanili", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ id, nome }),
  });
  if (risposta && risposta.ok) {
    document.getElementById("nuovoCampanileId").value = "";
    document.getElementById("nuovoCampanileNome").value = "";
    caricaPannelloAdmin();
  }
});

document.getElementById("bottoneAggiungiUtente").addEventListener("click", async () => {
  const nome = document.getElementById("nuovoUtenteNome").value.trim();
  const password = document.getElementById("nuovoUtentePassword").value;
  const campaniliConsentiti = Array.from(
    document.querySelectorAll("#listaCampaniliDisponibili input:checked")
  ).map(el => el.value);

  if (!nome || !password) { alert("Compila nome utente e password."); return; }
  if (campaniliConsentiti.length === 0) { alert("Seleziona almeno un campanile per questo utente."); return; }

  const risposta = await chiamataAutenticata("/api/admin/utenti", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ nome, password, campaniliConsentiti }),
  });
  if (risposta && risposta.ok) {
    document.getElementById("nuovoUtenteNome").value = "";
    document.getElementById("nuovoUtentePassword").value = "";
    caricaPannelloAdmin();
  }
});

async function inviaComando(idCampanile, numeroDiretta, bottone) {
  const sessione = leggiSessione();
  if (!sessione) { esci(); return; }

  const testoOriginale = bottone.innerHTML;
  bottone.disabled = true;
  bottone.innerHTML = "Invio...";

  try {
    const risposta = await fetch(`${BASE_URL}/api/campanili/${idCampanile}/diretta/${numeroDiretta}`, {
      method: "POST",
      headers: { Authorization: `Bearer ${sessione.token}` },
    });

    if (risposta.status === 401) { esci(); return; }

    if (risposta.ok) {
      bottone.classList.add("inviato");
      bottone.innerHTML = "✔ Inviato";
      setTimeout(() => { bottone.classList.remove("inviato"); bottone.innerHTML = testoOriginale; }, 1800);
    } else {
      bottone.innerHTML = "Errore";
      setTimeout(() => { bottone.innerHTML = testoOriginale; }, 1800);
    }
  } catch {
    bottone.innerHTML = "Errore di rete";
    setTimeout(() => { bottone.innerHTML = testoOriginale; }, 1800);
  } finally {
    bottone.disabled = false;
  }
}

bottoneLogin.addEventListener("click", accedi);
campoPassword.addEventListener("keydown", e => { if (e.key === "Enter") accedi(); });
bottoneEsci.addEventListener("click", esci);

// All'avvio: se c'è già una sessione salvata, prova a usarla direttamente senza richiedere login.
(async function avvio() {
  const sessione = leggiSessione();
  if (!sessione) return;

  try {
    const risposta = await fetch(`${BASE_URL}/api/me`, {
      headers: { Authorization: `Bearer ${sessione.token}` },
    });
    if (risposta.ok) {
      const dati = await risposta.json();
      await mostraSchermataPrincipale(dati.utente, dati.campanili, dati.admin);
    } else {
      esci();
    }
  } catch {
    // Server irraggiungibile al momento: mostra comunque la schermata principale con i dati
    // salvati, così l'app resta usabile a colpo d'occhio anche con connessione instabile.
    await mostraSchermataPrincipale(sessione.utente, sessione.campanili, sessione.admin);
  }
})();
