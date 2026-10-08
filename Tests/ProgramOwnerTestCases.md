# ProgramOwnerController test case design and execution

Scope: `Controllers/ProgramOwnerController.cs`.

## Execution summary

- **Command**: `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category!=Defect" && dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category=Defect"`
- **Test project**: `Tests/AmarTools.Voting.Tests.csproj`
- **Test files**: `ProgramOwnerControllerTests.cs`, `ProgramOwnerBoundaryDefectTests.cs`, `ProgramOwnerSeleniumFlowTests.cs`
- **Evidence**: `defect-output.txt`, `nondefect-output.txt`, `Tests/TestResults/defect.trx`, `Tests/TestResults/nondefect.trx`

## Results

| Category | Count | Status | Notes |
|---|---:|---|---|
| Existing controller tests | 53 | PASSED | ProgramOwnerControllerTests.cs |
| New BVA/ECT non-defect tests | 42 | PASSED | ProgramOwnerBoundaryDefectTests.cs |
| Time/vote-path tests | 9 | PASSED | TimeBehaviorTests.cs, VotePathTests.cs |
| Legacy Selenium smoke tests | 2 | SKIPPED | ProgramOwnerSeleniumTests.cs (no env vars) |
| **Total non-defect** | **106** | **98P, 8S** | 0 failed, 8 Selenium skipped |
| **Defect unit tests** | **21** | **FAILED** | D01–D11, D13, D14 confirmed |
| **Defect Selenium tests** | **3** | **SKIPPED** | S03, S07, S08 (no env vars) |
| **Total defect** | **24** | **21F, 3S** | All failures are assertion-based |
| **GRAND TOTAL** | **130** | **98P, 21F, 11S** | |

## Case matrix

| ID | Module | Class / equivalence | Expected result |
|---:|---|---|---|
| PO-001 | MyPrograms | Missing user ID | Unauthorized |
| PO-002 | MyPrograms | One owner program | Only owner program returned |
| PO-003 | MyPrograms | Multiple owners | Other owner excluded |
| PO-004 | Create GET | Default model | UTC model and one-hour range |
| PO-005 | Create POST | Invalid program name | Form returned; local input mode |
| PO-006 | Create POST | Service validation failure | Form returned with model error |
| PO-007 | Create POST | Valid unpublished model | Redirect to MyPrograms |
| PO-008 | Create POST | Valid published model | Redirect and success state |
| PO-009 | Edit GET | Missing ID | NotFound |
| PO-010 | Edit GET | Current owner | Form returned |
| PO-011 | Edit GET | Different owner | Forbid |
| PO-012 | Edit GET | Admin | Form returned |
| PO-013 | Edit POST | ID mismatch | NotFound |
| PO-014 | Edit POST | Invalid model | Form returned |
| PO-015 | Edit POST | Service failure | Form returned with error |
| PO-016 | Edit POST | Valid update | Redirect to MyPrograms |
| PO-017 | ManageCandidates | Missing program | NotFound |
| PO-018 | ManageCandidates | Current owner | Candidate view returned |
| PO-019 | ManageCandidates | Different owner | Forbid |
| PO-020 | ManageCandidates | Admin | Candidate view returned |
| PO-021 | AddCandidate | Null name | Redirect; no save |
| PO-022 | AddCandidate | Empty name | Redirect; no save |
| PO-023 | AddCandidate | Whitespace name | Redirect; no save |
| PO-024 | AddCandidate | Null code | Redirect; no save |
| PO-025 | AddCandidate | Empty code | Redirect; no save |
| PO-026 | AddCandidate | Whitespace code | Redirect; no save |
| PO-027 | AddCandidate | Missing program | NotFound |
| PO-028 | AddCandidate | Different owner | Forbid |
| PO-029 | AddCandidate | Duplicate code | Redirect; no duplicate |
| PO-030 | AddCandidate | HTTP image URL | Candidate saved |
| PO-031 | AddCandidate | HTTPS image URL | Candidate saved |
| PO-032 | AddCandidate | Root-relative image URL | Candidate saved |
| PO-033 | AddCandidate | Trimmed relative URL | Candidate saved and trimmed |
| PO-034 | AddCandidate | JavaScript URL | Redirect; no save |
| PO-035 | AddCandidate | FTP URL | Redirect; no save |
| PO-036 | AddCandidate | Bare host URL | Redirect; no save |
| PO-037 | AddCandidate | Data URL | Redirect; no save |
| PO-038 | AddCandidate | Optional description | Null/trimmed safely |
| PO-039 | DeleteCandidate | Missing candidate | NotFound |
| PO-040 | DeleteCandidate | Current owner | Candidate deleted |
| PO-041 | DeleteCandidate | Different owner | Forbid |
| PO-042 | DeleteCandidate | Admin | Candidate deleted |
| PO-043 | DeleteCandidate | Candidate belongs to another program | Forbid |
| PO-044 | Results | Missing program | NotFound |
| PO-045 | Results | Different owner | Forbid |
| PO-046 | Results | Current owner | Results view returned |
| PO-047 | Results | Blockchain valid | Valid status shown |
| PO-048 | ManageVoters | Missing program | NotFound |
| PO-049 | ManageVoters | Different owner | Forbid |
| PO-050 | ManageVoters | Current owner | Voter list returned |
| PO-051 | RegisterVoter GET | Missing program | NotFound |
| PO-052 | RegisterVoter GET | Current owner | Registration form returned |
| PO-053 | RegisterVoter GET | Different owner | Forbid |
| PO-054 | RegisterVoter POST | Service failure | Redirect with error |
| PO-055 | RegisterVoter POST | Service success | Redirect with success |
| PO-056 | RemoveVoter | Missing program | Forbid |
| PO-057 | RemoveVoter | Service success | Redirect with success |
| PO-058 | RemoveVoter | Service failure | Redirect with error |
| PO-059 | Delete | Missing program | NotFound |
| PO-060 | Delete | Different owner | Forbid |
| PO-061 | Delete | Active published program | Deletion rejected |
| PO-062 | Delete | Ended published program | Program deleted |

## BVA coverage

Boundary values include missing/empty/whitespace strings, missing IDs, ownership transitions, the active-versus-ended program boundary, and optional URL/description values.

## ECT coverage

Equivalence classes include owner/admin/other-user authorization, valid/invalid URL schemes, service success/failure, existing/missing records, and valid/invalid form models.

## Basis-path coverage

The cases cover the principal decision paths for ten modules: MyPrograms, Create, Edit, ManageCandidates, AddCandidate, DeleteCandidate, Results, ManageVoters, RegisterVoter, and RemoveVoter/Delete.
