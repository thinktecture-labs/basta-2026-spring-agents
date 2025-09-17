# Repository Guidelines

## Project Structure & Module Organization
- `BastaAgent/BastaAgent` — main .NET console app. Key folders: `Core/` (config), `LLM/` (API client + models), `Tools/` (tooling, reflection discovery), `Memory/`, `UI/`, `Utilities/`, `state/` (runtime data). Config: `appsettings.json`.
- `BastaAgent/BastaAgent.Tests` — xUnit test suite; see `TEST_COVERAGE.md` for scope and targets.
- `requests/` — HTTP demo files for OpenAI‑compatible endpoints.
- `.env`, `.env.template` — local HTTP demo variables; do not commit secrets.

## Build, Test, and Development Commands
- `dotnet build BastaAgent/BastaAgent.sln` — compile all projects.
- `dotnet test BastaAgent/BastaAgent.sln` — run tests (fast, isolated).
- `dotnet run --project BastaAgent/BastaAgent` — start the interactive agent.
- Configuration: edit `BastaAgent/BastaAgent/appsettings.json` or use environment variables. Example:
  - macOS/Linux: `export Agent__API__BaseUrl=https://openrouter.ai/api/v1; export Agent__API__ApiKey=...`
  - Windows PowerShell: `$env:Agent__API__ApiKey="..."`

## Coding Style & Naming Conventions
- C#/.NET conventions; 4‑space indentation; one type per file.
- Naming: types/methods `PascalCase`, locals/params `camelCase`, private fields `_camelCase`.
- Async methods end with `Async`; prefer DI over statics; use `ILogger<T>` for logging.
- Keep tools small and single‑purpose; validate inputs and return structured errors.

## Testing Guidelines
- Framework: xUnit. Target ≥80% coverage (see `TEST_COVERAGE.md`).
- Test names: `MethodOrBehavior_Condition_ExpectedResult` (e.g., `GenerateToolDefinitions_InvalidSchema_IsHandled`).
- Run subsets: `dotnet test --filter "FullyQualifiedName~InteractiveConsole"`.
- Add tests for new tools, memory behaviors, and error paths; keep tests fast (<5s suite).

## Commit & Pull Request Guidelines
- Use Conventional Commits: `feat:`, `fix:`, `docs:`, `test:`, `chore:` (repo history uses `chore:`).
- PRs must: describe changes, link issues, include screenshots/log excerpts for CLI behavior, and pass `dotnet test`.
- Include tests for new functionality; update `appsettings.json` only when necessary and never commit secrets.

## Agent & Tool Instructions
- Adding a tool: place in `BastaAgent/BastaAgent/Tools/BuiltIn`, inherit `BaseTool`, decorate with `[Tool(Category="...", RequiresApproval=true)]`, implement `Name`, `Description`, `ParametersSchema` (JSON schema), and `ExecuteAsync(...)`. The `ToolRegistry` discovers tools via reflection.
- Tool approval: configure via `Agent:Tools:RequireApproval` (env `Agent__Tools__RequireApproval`).
