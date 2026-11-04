# Agent Guidelines & Project Instructions for Custode

Benvenuto nel repository di **Custode**. Questo documento definisce le regole operative, le convenzioni architetturali e la metodologia che tutti gli agenti AI e gli sviluppatori devono seguire per contribuire al progetto.

---

## 🎯 Principi Guida del Progetto

1. **Zero Vibe Coding**: Nessun codice viene scritto senza una specifica OpenSpec approvata e validata (`openspec validate --strict`).
2. **Specification-Driven Development (SDD)**: Tutte le modifiche seguono il ciclo di vita `propose` -> `apply` -> `verify` -> `archive`.
3. **SOLID & Vertical Slices Architecture**: 
   * La parte server in .NET 10 è organizzata per **Vertical Slices** (Feature Slices).
   * Alta coesione e basso accoppiamento tra i casi d'uso.
4. **Security-First & Multi-Tenancy**:
   * Ogni query o operazione sul database deve garantire l'isolamento del `TenantId`.
   * I segreti non vengono mai salvati in chiaro.
   * Tutte le operazioni critiche e di gestione ACL devono produrre una voce nell'Audit Log.

---

## 🛠 Stack Tecnologico & Strumenti

* **Backend**: .NET 10 (C#)
* **Database**: PostgreSQL (Produzione/Sviluppo locale) / SQLite (Test veloci in-memory/file)
* **Test Automation**: xUnit + Testcontainers (PostgreSQL 18)
* **Web Frontend**: Angular (TypeScript)
* **Desktop App**: .NET 10 Cross-platform
* **Spec Tool**: OpenSpec CLI (`openspec`)

---

## 📋 Regole di Workflow per gli Agenti

Quando ricevi un prompt o un task per questo repository:

1. **Verifica le Specifiche**: Assicurati che l'operazione richiesta sia tracciata in un change OpenSpec attivo nella cartella `openspec/changes/`.
2. **Rispetta la Vertical Slice**: Quando aggiungi o modifichi una funzionalità backend, mantieni la logica racchiusa nella slice relativa (Request, Handler, Response, Validator, Data Access).
3. **Esegui la Validation Suite**:
   * Prima di considerare un task completato, esegui i test sia su SQLite che su PostgreSQL tramite Testcontainers.
   * Assicurati che `openspec validate --strict` restituisca 0 errori o warning.
4. **Nessuna Scorciatoia**: Non saltare i passaggi di validazione e non effettuare modifiche arbitrarie al DB senza regolare migrazione EF Core.
5. **Intestazione di Copyright**: Ogni file sorgente principale (es. `.cs`, `.ts`) sotto `src/` e `tests/` creato o modificato DEVE iniziare con queste due righe, prima di qualsiasi altro contenuto (inclusi `using` e `import`):

   ```
   // Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
   // Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.
   ```

   * Non applicare l'intestazione a file generati (migrazioni EF Core, `*.Designer.cs`, `obj/`, `bin/`, `node_modules/`), né a file di configurazione, JSON, Markdown o OpenSpec.