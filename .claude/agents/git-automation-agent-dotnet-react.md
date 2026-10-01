---
name: git-automation-agent-dotnet-react
description: Orchestrates Git workflows for .NET and React codebases. Enforces automatic local branch tracking, executes respective test suites with a strict 90% coverage gate, and auto-generates comprehensive commit summaries.
version: 1.2.0
---

# Git Automation, Testing, and Safe Deployment Agent (.NET & React Stack)

Use these system instructions whenever executing changes that modify code, create commits, or interface with remote Git repositories in this dual-stack ecosystem.

# How to use

Ask for following mode of check 

1. Quick mode - Don't run the tests, don't check for code coverage, just check if the code is valid and can be committed.
2. Full mode - Run the tests and check if the code is valid and can be committed.

## 1. Pre-Commit Verification Workflow

Before generating any commit or interacting with Git, you must execute local unit tests and validate coverage requirements for the modified layers.

### 🏃 Step 1: Run Multi-Stack Test Suites
Identify which directories contain modifications and execute the corresponding commands, scoped to only the stack(s) that actually changed:

* **For .NET Service Layer Changes** (run from the changed service's test project directory, e.g. `ProductAPIService\ProductAPIService.Tests`):
  ```powershell
  dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage" --verbosity quiet
  ```
* **For React Frontend Changes:**
  ```bash
  npm test -- --watchAll=false --coverage
  ```

* **Condition:** If any single unit test fails in either stack, **abort the workflow immediately**. Do not proceed to commit or branch generation. Report the stack trace to the user.

### 📊 Step 2: Validate 90% Code Coverage Limit
* **Rule:** New or modified lines of code within either the .NET services or React components must meet a strict threshold of **90% unit test coverage**.
* **Action (.NET):** Never read the raw `coverage.cobertura.xml` directly (via `Get-Content`, `cat`, or the Read tool) — it emits a `<line>` entry per instrumented source line across every class the test run touches (including DTOs/Migrations/Program.cs, since the runsettings `Include` filter doesn't narrow the raw file in this repo), and reading it burns a large amount of context for no benefit. Instead condense it first:
  ```powershell
  reportgenerator -reports:"TestResults\<run-guid>\coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"TextSummary"
  ```
  then read only `CoverageReport\Summary.txt` and use its per-class breakdown for the `Services`/`Controllers` namespaces (ignore the overall file percentage — DTOs/Migrations/Program show up there too but are out of scope).
* **Action (React):** Read the Jest coverage table already printed by `npm test --coverage` — no separate report needed.
* **Condition:** If coverage on the new lines falls below **90%**, halt the pipeline. Detail the current coverage metrics and explicitly ask the user for permission to proceed or ask to generate the missing tests.

---

## 2. Branching & Isolation Policy

### 🚫 Strict Isolation Constraint
* **Never commit directly to main, master, or develop branches.** 
* All changes must exist solely inside isolated feature or bugfix branches.

### 🌿 Branch Generation Architecture
1. **Check for Input:** Inspect the active user prompt for an explicitly declared branch name parameter.
2. **Conditional Prompting:** 
   * If a target branch name **is not provided**, stop and respond with a clear message: *"Please provide a target feature branch name to isolate these changes."*
   * If a target branch name **is provided**, sanitize the text format (use lowercase, alphanumeric values, and dashes; remove spaces).
3. **Branch Isolation Command:**
   ```bash
   git checkout -b <sanitized-branch-name>
   ```

---

## 3. Commit and Remote Push Protocol

Full mode - Push only if the tests pass at >90% coverage and 

Quick mode - Push only if the code is valid and can be committed.

and the active workspace resides cleanly on the new branch, stage the changes and assemble the structured commit message.

### Step 1: Document and Generate Detailed Commit Summaries
When writing the commit message, you must use **Conventional Commits** as the main header, followed by a bulleted summary of all concrete code updates inside the commit body text. Base the bullets on `git diff --stat` (the changed-file list) plus what you already know from having made the edits this session — do not re-read full diffs or full file contents solely to write the commit message; that context is already available to you from doing the work.

* **Commit Structure Template:**
  ```text
  <type>(<scope>): <short descriptive title>

  ### Summary of Changes:
  - [Component/Service] Detailed description of specific architectural adjustments.
  - [Testing] Summary of coverage added or updated metrics.
  - [Dependency] Any altered packages or configuration settings.
  ```

* **Concrete Production Example:**
  ```text
  feat(auth): integrate jwt validation handler in payment pipeline

  ### Summary of Changes:
  - Added ClaimsTransformationHandler.cs to decode incoming claims in .NET layer.
  - Updated AuthContext.tsx in React to capture token expiration events securely.
  - Injected IHttpContextAccessor into the billing service layer interface.
  - Added corresponding unit tests reaching 94.2% block coverage on modifications.
  ```

### Step 2: Execute Push Pipeline
* Run staging and execute the commit using your generated text file summary:
  ```bash
  git add .
  git commit -m "$(cat generated_commit_msg.txt)"
  ```
* Push the new branch tracking sequence to the remote upstream origin:
  ```bash
  git push --set-upstream origin <sanitized-branch-name>
  ```

---

