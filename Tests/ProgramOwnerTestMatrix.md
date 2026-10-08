# ProgramOwnerController test matrix

The executable suite is `ProgramOwnerControllerTests.cs`, `ProgramOwnerBoundaryDefectTests.cs`, and `ProgramOwnerSeleniumFlowTests.cs` using xUnit, Moq, Selenium, and EF Core InMemory.

## Coverage modules and basis paths

| Module | Covered paths | Defect tests |
|---|---|---|
| MyPrograms | Missing identity; owner query; owner-only filtering | — |
| Create (GET/POST) | GET defaults; POST invalid model; service failure; successful redirect | D13: inconsistent trimming |
| Edit (GET/POST) | Missing program; owner/admin authorization; ID mismatch; invalid model; service success | D01: lifecycle unchecked (can reopen ended elections) |
| ManageCandidates | Missing program; owner/admin authorization; successful view | — |
| AddCandidate | Required fields; missing program; ownership; duplicate code; invalid/valid image URL; normalized persistence | D02, D07, D08, D09, D10: lifecycle, lengths, URL validation, case-sensitivity |
| DeleteCandidate | Missing candidate; ownership/admin; wrong program; successful deletion | D02, D03, D11: lifecycle unchecked, cascade-delete votes, authorization inconsistency |
| Results | Missing program; ownership; successful results and blockchain status | — |
| ManageVoters | Missing program; ownership; successful voter listing | — |
| RegisterVoter (GET/POST) | Missing/authorized GET; POST service failure; successful registration | D14: no email format/length checks, no end-time restriction |
| RemoveVoter | Missing program authorization; service result bridging | D04, D11: cascade-delete votes, authorization inconsistency |
| Delete | Missing/unauthorized resources; service success/failure; active-program deletion boundary; ended-program deletion | D01, D05: lifecycle unchecked, cascade-delete data |

## BVA and ECT classes

- Program identifiers: missing, existing, and wrong-owner values.
- Ownership: current owner, another owner, and Admin role.
- Candidate name/code: null, empty, whitespace, 1 char (D07), max length (D08), and valid values.
- Image URL: HTTP, HTTPS, root-relative, protocol-relative (D09), javascript: scheme (D09), data: URL, malformed.
- Program deletion timing: active published, ended published (D01, D05), and missing program.
- Candidate code case-sensitivity: "A" vs "a" duplicates (D10).
- Voter registration email: valid, invalid format (D14), too long, missing.
- Program lifecycle: not started, active, ended; election state transitions (D01, D02, D04).
- Service outcomes: success and failure result tuples.

## Test counts

| Category | Count | Detail |
|---|---|---|
| Existing controller tests | 53 | ProgramOwnerControllerTests.cs |
| Time/vote-path tests | 9 | TimeBehaviorTests.cs, VotePathTests.cs |
| Legacy Selenium smoke tests | 2 | ProgramOwnerSeleniumTests.cs (skipped without env vars) |
| **Total existing** | **64** | |
| **New BVA/ECT tests** | **42** | ProgramOwnerBoundaryDefectTests.cs (non-defect) |
| **New defect tests** | **24** | ProgramOwnerBoundaryDefectTests.cs + ProgramOwnerSeleniumFlowTests.cs |
| **Total new** | **66** | |
| **Grand total** | **130** | 98 passed, 21 defect failures (confirmed), 11 skipped (Selenium) |

## Defect reporting

**13 confirmed application defects** were identified via 21 failing unit tests:
- D01–D11, D13–D14: confirmed unit-test failures (assertion-based)
- D12: deferred (requires profiling/code-review outside test scope; log loading inefficiency documented but not automated)
- D15–D28: deferred (involve other controllers, services, or require manual Selenium/security testing)

See `ProgramOwnerDefectReports.md` for full details.

## Selenium coverage

11 browser-automation tests in `ProgramOwnerSeleniumFlowTests.cs`:
- Dashboard login and program listing
- Program create, edit, publish workflows
- Candidate add/delete with validation (D07, D09)
- Voter registration with validation (D14)
- Duration boundary rendering (D06)

**Status**: All 11 tests skipped (require `AMARTOOLS_BASE_URL`, `AMARTOOLS_TEST_EMAIL`, `AMARTOOLS_TEST_PASSWORD` environment variables).
