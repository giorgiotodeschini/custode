# Custode

> **Secure, Self-Hosted Multi-Tenant Secrets Manager**

**Custode** è una piattaforma open-source e self-hosted per la gestione sicura dei segreti, progettata per sviluppatori, team e organizzazioni[cite: 1]. Offre un backend cifrato robusto per la conservazione di credenziali, API key, token di accesso (PAT) e file-based secrets (certificati e chiavi)[cite: 1].

---

## 🏗 Architettura del Progetto

Il progetto è suddiviso nei seguenti componenti principali:

* **Server Core (.NET 10)**: Backend ad alte prestazioni costruito secondo l'architettura a **Vertical Slices** e i principi **SOLID**[cite: 1]. Gestisce la sicurezza a riposo (cifratura KEK/DEK), l'isolamento dei tenant (`TenantId`), le API REST e l'audit logging immutabile.
* **Web UI (Angular)**: Console web per l'amministrazione di tenant, utenti, gruppi, permessi sulle cartelle e gestione visuale dei segreti.
* **Desktop Client (.NET 10)**: Applicazione desktop cross-platform focalizzata sull'autofill rapido e sicuro delle credenziali nei form di autenticazione[cite: 1].

---

## 🔑 Caratteristiche Principali

* **Multi-Tenancy Sicura**: Isolamento rigido dei dati a livello di database tramite `TenantId` e protezione delle chiavi di cifratura tramite master key/KEK esterna.
* **Tassonomia dei Segreti Flessibile**:
  * Credenziali Username e Password
  * API Keys & Token di accesso (PAT)
  * File-based Secrets (certificati `.crt`, `.pem`, `.pfx`, chiavi SSH)
  * Coppie chiave-valore generiche (`.env`)
* **Organizzazione a Cartelle & ACL Granulare**: Struttura ad albero con ereditarietà dei permessi per gruppi di utenti e supporto al *break inheritance* (override) per cartelle riservate.
* **Audit Logging Immutabile**: Tracciamento cronologico e non modificabile di tutti gli accessi, le decifrazioni e le modifiche ai permessi.

---

## 🛠 Tech Stack

* **Server**: .NET 10, ASP.NET Core Identity, Entity Framework Core, PostgreSQL (Primary), SQLite (Integration tests)
* **Web UI**: Angular, TypeScript, RxJS
* **Desktop Client**: .NET 10 (Cross-platform UI)
* **DevOps / Testing**: Docker, Testcontainers, xUnit, OpenSpec

---

## 🚀 Specification-Driven Development (SDD)

Questo progetto segue rigorosamente la metodologia **Specification-Driven Development (SDD)** tramite **OpenSpec**. Ogni modifica all'architettura o alle funzionalità viene preventivamente definita in specifiche tracciabili prima dell'implementazione nel codice, vietando pratiche di *vibe coding* casuale.

### Struttura delle Specifiche (`openspec/`)
* `openspec/specs/`: Specifiche attive e architetture consolidate.
* `openspec/changes/`: Modifiche e feature in fase di proposta o sviluppo attivo.
* `openspec/changes/archive/`: Change completati e archiviati.

---

## 📜 Licenza

Questo progetto è rilasciato sotto licenza [MIT](LICENSE).