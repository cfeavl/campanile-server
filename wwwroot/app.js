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

function salvaSessione(token, utente, campanili) {
  localStorage.setItem("campanile_token", token);
  localStorage.setItem("campanile_utente", utente);
  localStorage.setItem("campanile_campanili", JSON.stringify(campanili));
}

function leggiSessione() {
  const token = localStorage.getItem("campanile_token");
  if (!token) return null;
  return {
    token,
    utente: localStorage.getItem("campanile_utente"),
    campanili: JSON.parse(localStorage.getItem("campanile_campanili") || "[]"),
  };
}

function esci() {
  localStorage.removeItem("campanile_token");
  localStorage.removeItem("campanile_utente");
  localStorage.removeItem("campanile_campanili");
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
    salvaSessione(dati.token, utente, dati.campanili);
    mostraSchermataPrincipale(utente, dati.campanili);
  } catch {
    erroreLogin.textContent = "Impossibile contattare il server. Controlla la connessione.";
  } finally {
    bottoneLogin.disabled = false;
    bottoneLogin.textContent = "Accedi";
  }
}

function mostraSchermataPrincipale(utente, campanili) {
  testoUtente.textContent = `Ciao, ${utente}`;
  elencoCampanili.innerHTML = "";

  for (const campanile of campanili) {
    const card = document.createElement("div");
    card.className = "card-campanile";

    const titolo = document.createElement("h2");
    titolo.innerHTML = `🔔 ${campanile.nome}`;
    card.appendChild(titolo);

    const griglia = document.createElement("div");
    griglia.className = "griglia-dirette";

    for (let numero = 0; numero < 5; numero++) {
      const bottone = document.createElement("button");
      bottone.className = "btn-diretta";
      bottone.innerHTML = `🔔 Diretta ${numero + 1}`;
      bottone.addEventListener("click", () => inviaComando(campanile.id, numero, bottone));
      griglia.appendChild(bottone);
    }

    card.appendChild(griglia);
    elencoCampanili.appendChild(card);
  }

  mostraSchermata(schermataPrincipale);
}

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
      mostraSchermataPrincipale(dati.utente, dati.campanili);
    } else {
      esci();
    }
  } catch {
    // Server irraggiungibile al momento: mostra comunque la schermata principale con i dati
    // salvati, così l'app resta usabile a colpo d'occhio anche con connessione instabile.
    mostraSchermataPrincipale(sessione.utente, sessione.campanili);
  }
})();
