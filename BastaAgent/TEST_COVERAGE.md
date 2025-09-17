# BastaAgent Test Coverage Report

## Overview
- Total tests: 230
- Status: All passing (100%)
- Runtime: ~2–5 seconds locally (net9.0)
 - Coverage (line): 75.52%
 - Coverage (branch): 63.20%

Note: Coverage values reflect the latest run (see TestResults/*/coverage.cobertura.xml) with the repository’s coverlet.runsettings applied.

## Coverage Summary (by area)
- Agent orchestration
  - Tool-call parsing (streaming), argument-Fragmentaggregation (index→id), tool_result Linking (tool_call_id)
  - Follow-up Loop: mehrstufige Tool-Flows, konfigurierbares MaxFollowUps, System‑Nudge aus Config
  - Auto‑Save und State‑Wiederherstellung (inkl. robustem, atomarem Write)
- Memory
  - Kompaktierung (LLM- und Fallback‑Summary), Erhalt jüngster User/Assistant‑Nachrichten, Token‑Schätzung
  - Datei‑Persistenz (Save/Load) mit Fehlerpfaden
- LLM Client
  - Non‑JSON, Content‑Type Mismatches, Streaming (SSE)
  - Retry‑Logik: 5xx, 429, 408; nach Retry Erfolg (Complete + Stream)
  - Modell‑Selektion per Purpose
- Tools
  - FileSystem: Read/Write/Append, Pretty‑Print JSON bei Overwrite, Verzeichnis‑Listing (Pattern, Recursive)
  - Web: Request Parameter‑Validierung, Protokollbeschränkung; WebSearch: Parameter/Limitierung (Mock)
  - ToolRegistry: Reflection‑Discovery, FunctionDefinition (sanitized), RequiresConfirmation
- UI / Utilities
  - InteractiveConsole: Thread‑Safety, Progress, Output‑Formate, Cancellation
  - ErrorMessages und ProgressIndicator (Spinner/ProgressBar)

## Notable Scenarios Covered
- Tool-Aufrufe aus Streaming-Deltas (mehrere Fragmente) werden korrekt konsolidiert und ausgeführt.
- Folge‑ToolCalls werden in Sequenz (z. B. Read→Write) orchestriert; Abbruch nach konfiguriertem Limit.
- LLM‑Fehlerfälle (HTTP 500/429/408, Non‑JSON, falscher Content‑Type) werden erkannt; Retries/Fehlerpfade getestet.
- Speicherkompaktierung erstellt sinnvolle Summaries und respektiert die jüngste Konversation.
- Dateioperationen inkl. Encoding‑Fehler, Verzeichnis‑Kantenfälle und JSON‑Schreibqualität.

## Quality Notes
- Tests sind isoliert, schnell und deterministisch (keine echten Netzwerkabhängigkeiten).
- Breite Abdeckung von Happy‑Path, Fehlerpfaden, Edge‑Cases und nebenläufigen Szenarien.
- Log‑Nachrichten nutzen Message Templates (konsistente Strukturierung für Telemetrie).

## Maintenance Guidelines
- Ziel: ≥80% Abdeckung in Kernkomponenten, mit Fokus auf Fehlerpfade und Nebenläufigkeit.
- Bei neuen Tools/Flows: jeweils Erfolg und Fehlerszenarien (Parameter/Timeout/Denied) abdecken.
- Halte Tests schnell (unter 5s Gesamt) und unabhängig von externer Infrastruktur.

## Commands
```bash
# Alle Tests ausführen
dotnet test BastaAgent/BastaAgent.sln

# Einzelne Bereiche filtern (Beispiel)
dotnet test BastaAgent/BastaAgent.sln --filter "FullyQualifiedName~Agent"

# Coverage (Beispiel, erfordert Tooling)
dotnet test BastaAgent/BastaAgent.sln --collect:"XPlat Code Coverage"
```
