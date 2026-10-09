# ProgramOwnerController test matrix

The executable suite is `ProgramOwnerControllerTests.cs`, `ProgramOwnerBoundaryDefectTests.cs`, `ProgramOwnerSeleniumFlowTests.cs`, `TimeBehaviorTests.cs` and `VotePathTests.cs`, using xUnit, Moq, EF Core InMemory and Selenium.

## Coverage modules and defects

| Module | Covered paths | Defects |
|---|---|---|
| MyPrograms | Missing identity; owner query; owner-only filtering | D12 (loads all votes/voters) |
| Create (GET/POST) | GET defaults; invalid model; service failure with and without message; success unpublished/published | D06 (low), D13 |
| Edit (GET/POST) | Missing program; owner/admin authorization; ID mismatch; invalid model; service failure; success published | D01 |
| ManageCandidates | Missing program; owner/admin authorization; successful view | D30 (XSS in delete confirm) |
| AddCandidate | Required fields; missing program; ownership; admin; duplicate code; invalid/valid image URL; length boundaries | D02, D07 (fixed), D08 (partly fixed), D09 (fixed), D10 |
| DeleteCandidate | Missing candidate; ownership/admin; wrong program; successful deletion | D02, D03, D11 |
| Results | Missing program; ownership; admin; results and blockchain status | D18, D19, D23 (other classes) |
| ManageVoters | Missing program; ownership; successful listing | D16, D17 (other classes) |
| RegisterVoter (GET/POST) | Missing/authorized GET; admin; service failure; success | D14, D29 |
| RemoveVoter | Missing program authorization; service result | D04, D11 |
| Delete | Missing/unauthorized; active published rejected; unpublished; ended; end-time boundary | D05 |

## Boundary value analysis (BVA)

Seven boundary points per bounded input: below minimum, minimum, minimum+1, nominal, maximum-1, maximum, above maximum.

| Input | Function | Constraint (source) | Below min | Min | Min+1 | Nominal | Max-1 | Max | Above max | Expected | Cases |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Program duration (seconds) | Create/Edit (service) | End - Start >= 300 s (MinimumProgramDuration) | 299 | 300 | 301 | 3600 | — | — | — | <300 rejected; >=300 accepted | BVA-01..03 |
| ProgramName length | Create/Edit POST | 3..200 (model StringLength) | 2 | 3 | 4 | 20 | 199 | 200 | 201 | Outside range -> validation error | BVA-04..07 |
| Description length | Create/Edit POST | <= 2000 (model) | — | 0 | 1 | 200 | 1999 | 2000 | 2001 | 2001 -> validation error | BVA-08 |
| Candidate name length | AddCandidate | 2..150 (model; NOT enforced in action) | 1 | 2 | 3 | 12 | 149 | 150 | 151 | Outside range rejected (defects D07, D08) | BVA-09..12 |
| Candidate code length | AddCandidate | 1..50 (model; NOT enforced in action) | 0 (blank) | 1 | 2 | 6 | 49 | 50 | 51 | Outside range rejected (D08) | BVA-10,11,13 |
| Candidate description length | AddCandidate | <= 500 | — | 0 | 1 | 80 | 499 | 500 | 501 | 501 rejected (D08) | BVA-14 |
| Image URL length | AddCandidate | <= 500 | — | 1 | 2 | 40 | 499 | 500 | 501 | 501 rejected (D08) | BVA-15 |
| Program / candidate / voter id | Edit, ManageCandidates, Delete, … | Positive existing id | int.MinValue, -1, 0 | 1 (existing) | 2 | — | — | — | int.MaxValue | Non-existing ids -> 404 | BVA-16..19 |
| Election end time vs now (delete) | Delete | HasEnded = now >= EndTime | EndTime = now+60 s | — | — | — | — | — | EndTime = now-1 s | Active: refused; ended: deleted | BVA-20 |
| Voter name length | RegisterVoter (service) | 2..150 (model; NOT enforced in service) | 1 | 2 | 3 | 12 | 149 | 150 | 151 | Outside range rejected (D29) | D29 test |
| Voter email length | RegisterVoter (service) | <= 200 and valid format | 'a' (no @) | a@b.co | — | name@example.com | 199 | 200 | 201 | Invalid/too long rejected (D14) | ECT-18 |

## Equivalence class testing (ECT)

| Input | Class | Description | Valid / invalid | Representative value | Expected | Cases |
|---|---|---|---|---|---|---|
| Caller identity | EC1 | Program owner | Valid | owner-1 on own program | Action allowed | ECT-01 |
| Caller identity | EC2 | Admin on another owner's program | Valid | admin-1 with Admin role | Action allowed | ECT-02, BP-12, BP-16, BP-18 |
| Caller identity | EC3 | Authenticated non-owner, non-admin | Invalid | owner-1 on owner-2's program | Forbid | ECT-03 |
| Caller identity | EC4 | No user id claim | Invalid | — | Unauthorized | ECT-04 |
| Resource id | EC5 | Program/candidate exists | Valid | 1 | Proceeds to ownership check | ECT-01 |
| Resource id | EC6 | Program/candidate missing | Invalid | 999 | NotFound | ECT-05 |
| Program state | EC7 | Draft / upcoming | Valid for edits | IsPublished=false, start in future | Edits and deletes allowed | BP-20 |
| Program state | EC8 | Active (started, not ended) | Invalid for ballot changes | Start -1h, End +1h, published | Candidate add/delete and rescheduling must be rejected | ECT-14, ECT-16, ECT-20, BP-19 |
| Program state | EC9 | Ended | Invalid for changes | End -1h | Add candidate / register voter rejected; delete should keep data | ECT-15, ECT-19, BVA-20 |
| Candidate name | EC10 | Valid length 2..150 | Valid | Alice Rahman | Saved | BVA-10, BVA-11 |
| Candidate name | EC11 | Blank / whitespace | Invalid | '', '  ', null | Rejected | BP-11 (PO-021..023) |
| Candidate name | EC12 | Too short or too long | Invalid | 'A', 151 chars | Rejected | BVA-09, BVA-12 |
| Candidate code | EC13 | New unique code | Valid | B2 | Saved | BP-12 |
| Candidate code | EC14 | Exact duplicate | Invalid | A | Rejected | ECT-12 |
| Candidate code | EC15 | Case-variant duplicate | Invalid | a | Rejected | ECT-13 |
| Image URL | EC16 | Empty / null | Valid | '' | Saved with ImageUrl = null | (PO-038) |
| Image URL | EC17 | Absolute http/https | Valid | https://x.example/a.png | Saved | ECT-06, ECT-07 |
| Image URL | EC18 | Root-relative | Valid | /images/a.png | Saved | ECT-08 |
| Image URL | EC19 | Protocol-relative | Invalid | //evil.example/a.png | Rejected | ECT-09 |
| Image URL | EC20 | Other scheme / malformed | Invalid | javascript:, ftp:, data:, example.com | Rejected | ECT-10, ECT-11, BP-13 |
| Voter email | EC21 | Valid unique email | Valid | nadia@example.com | Registered | BP-18 |
| Voter email | EC22 | Invalid format | Invalid | not-an-email | Rejected | ECT-18 |
| Voter email | EC23 | Duplicate email in program | Invalid | same email twice | Rejected | (service) / Selenium S10 |
| Service result | EC24 | Success | Valid | (true, null) | Redirect + success message | BP-06, BP-10 |
| Service result | EC25 | Failure with message | Invalid | (false, 'text') | Form/redirect with error | BP-04, BP-17 |
| Service result | EC26 | Failure without message | Invalid | (false, null) | Form without model error | BP-05, BP-09 |
| Model state | EC27 | Valid | Valid | complete form | Service is called | BP-06 |
| Model state | EC28 | Invalid | Invalid | empty ProgramName | Form returned, service not called | BP-03 |
| Browser time zone offset | EC29 | Offset present | Valid | +360 (UTC+6) / -300 (UTC-5) | Correct UTC stored | TimeBehaviorTests |
| Browser time zone offset | EC30 | Offset missing | Invalid | null | Rejected | TimeBehaviorTests |

## Basis path testing: 10 functions

V(G) = decision points + 1. A decision point is each `if`, each `&&` / `||` operand, ternary and `catch`. The number of independent paths listed equals V(G) for every function.

| Function | Source lines | Decision predicates | Decision points | V(G) | Paths listed | Check |
|---|---|---|---|---|---|---|
| MyPrograms | 33-52 | userId empty | 1 | 2 | 2 | OK |
| Create POST | 69-94 | !ModelState.IsValid; !success; errorMessage != null; model.IsPublished | 4 | 5 | 5 | OK |
| Edit POST | 119-143 | id != model.Id; !ModelState.IsValid; !success; errorMessage != null; model.IsPublished | 5 | 6 | 6 | OK |
| ManageCandidates GET | 146-157 | program is null; owner != current; !Admin | 3 | 4 | 4 | OK |
| AddCandidate | 162-226 | name blank; code blank; program null; owner != current; !Admin; codeExists; imageUrl present; TryCreate; http; https; !validUrl; !relativeOk; description blank; catch DbUpdateException | 14 | 15 | 15 | OK |
| DeleteCandidate | 232-257 | candidate null; program null; owner != current; !Admin; candidate.ProgramId != programId; catch DbUpdateException | 6 | 7 | 7 | OK |
| Results | 260-276 | program null; owner != current; !Admin | 3 | 4 | 4 | OK |
| ManageVoters | 280-300 | program null; owner != current; !Admin | 3 | 4 | 4 | OK |
| RegisterVoter POST | 319-337 | program null; owner != current; !Admin; !success | 4 | 5 | 5 | OK |
| Delete | 362-389 | program null; owner != current; !Admin; IsPublished; !HasEnded; catch DbUpdateException | 6 | 7 | 7 | OK |
| **Total** |  |  | 49 | 59 | 59 |  |

### Independent paths

| Function | Path | Condition path | Expected outcome | Test case(s) |
|---|---|---|---|---|
| MyPrograms | P1 | userId empty | Unauthorized | BP-01 |
| MyPrograms | P2 | userId present | View(programs of owner) | BP-02 |
| Create POST | P1 | ModelState invalid | Form, ValuesAreUtc=false | BP-03 |
| Create POST | P2 | valid; service fails; message present | Form + model error | BP-04 |
| Create POST | P3 | valid; service fails; message null | Form, no model error | BP-05 |
| Create POST | P4 | valid; success; not published | Redirect MyPrograms | BP-06 |
| Create POST | P5 | valid; success; published | PublishedLink + redirect | BP-07 |
| Edit POST | P1 | id != model.Id | NotFound | BP-08 |
| Edit POST | P2 | ModelState invalid | Form | PO-014 |
| Edit POST | P3 | service fails; message present | Form + model error | PO-015 |
| Edit POST | P4 | service fails; message null | Form, no model error | BP-09 |
| Edit POST | P5 | success; not published | Redirect MyPrograms | PO-016 |
| Edit POST | P6 | success; published | PublishedLink + redirect | BP-10 |
| ManageCandidates GET | P1 | program null | NotFound | PO-017 |
| ManageCandidates GET | P2 | owner != current and not admin | Forbid | PO-019 |
| ManageCandidates GET | P3 | owner | View(candidates) | ECT-01 |
| ManageCandidates GET | P4 | admin on another owner's program | View(candidates) | ECT-02 |
| AddCandidate | P1 | name blank | Error + redirect | BP-11 |
| AddCandidate | P2 | name ok; code blank | Error + redirect | PO-024..026 |
| AddCandidate | P3 | program null | NotFound | PO-027 |
| AddCandidate | P4 | not owner, not admin | Forbid | PO-028 |
| AddCandidate | P5 | admin on another owner's program | Saved | BP-12 |
| AddCandidate | P6 | code already exists | Error + redirect | ECT-12 |
| AddCandidate | P7 | no image URL; description blank | Saved with nulls | PO-038 |
| AddCandidate | P8 | http URL | Saved | ECT-06 |
| AddCandidate | P9 | https URL | Saved | ECT-07 |
| AddCandidate | P10 | root-relative URL | Saved | ECT-08 |
| AddCandidate | P11 | absolute URL with other scheme | Error + redirect | ECT-10, ECT-11 |
| AddCandidate | P12 | unparsable / bare host URL | Error + redirect | BP-13 |
| AddCandidate | P13 | description present | Saved, trimmed | PO-038 |
| AddCandidate | P14 | DbUpdateException on save | Error + redirect | needs fault-injection test (add) |
| AddCandidate | P15 | owner; all inputs valid | Saved + success message + redirect | PO-030, BVA-10 |
| DeleteCandidate | P1 | candidate null | NotFound | PO-039 |
| DeleteCandidate | P2 | program null | Forbid | (add) — PO-056 pattern |
| DeleteCandidate | P3 | not owner, not admin | Forbid | PO-041 |
| DeleteCandidate | P4 | admin deletes | Deleted | PO-042 |
| DeleteCandidate | P5 | candidate.ProgramId != programId | Forbid | BP-14 |
| DeleteCandidate | P6 | DbUpdateException | Error + redirect | needs fault-injection test (add) |
| DeleteCandidate | P7 | owner deletes own candidate | Deleted | BP-15 |
| Results | P1 | program null | NotFound | PO-044 |
| Results | P2 | not owner, not admin | Forbid | PO-045 |
| Results | P3 | owner | View + results + chain status | PO-046, PO-047 |
| Results | P4 | admin on another owner's program | View | BP-16 |
| ManageVoters | P1 | program null | NotFound | PO-048 |
| ManageVoters | P2 | not owner, not admin | Forbid | PO-049 |
| ManageVoters | P3 | owner | View(voters) | PO-050 |
| ManageVoters | P4 | admin on another owner's program | View(voters) | (add) |
| RegisterVoter POST | P1 | program null | Forbid | PO-056 pattern |
| RegisterVoter POST | P2 | not owner, not admin | Forbid | (add) |
| RegisterVoter POST | P3 | admin; service success | Redirect + success | BP-18 |
| RegisterVoter POST | P4 | service fails | Redirect + error | BP-17 |
| RegisterVoter POST | P5 | owner; service success | Redirect + success | PO-055 |
| Delete | P1 | program null | NotFound | PO-059 |
| Delete | P2 | not owner, not admin | Forbid | PO-060 |
| Delete | P3 | admin deletes ended program | Deleted | (add) |
| Delete | P4 | published and not ended | Refused with error | BP-19 |
| Delete | P5 | unpublished | Deleted | BP-20 |
| Delete | P6 | published and ended | Deleted | PO-062 |
| Delete | P7 | DbUpdateException | Error + redirect | needs fault-injection test (add) |

### Known gaps

These paths have no automated test yet:

- AddCandidate P14: DbUpdateException on save (needs fault-injection test (add))
- DeleteCandidate P2: program null ((add) — PO-056 pattern)
- DeleteCandidate P6: DbUpdateException (needs fault-injection test (add))
- ManageVoters P4: admin on another owner's program ((add))
- RegisterVoter POST P2: not owner, not admin ((add))
- Delete P3: admin deletes ended program ((add))
- Delete P7: DbUpdateException (needs fault-injection test (add))

## Test counts

| Category | Count | Detail |
|---|---:|---|
| Existing controller tests | 53 | ProgramOwnerControllerTests.cs |
| Time and vote-path tests | 9 | TimeBehaviorTests.cs, VotePathTests.cs |
| New BVA/ECT/basis-path unit tests | 34 | ProgramOwnerBoundaryDefectTests.cs |
| New Selenium flows (non-defect) | 8 | ProgramOwnerSeleniumFlowTests.cs |
| **Non-defect total** | **104** | 104 passed |
| Defect unit tests | 21 | 3 passed (D07, D08 name, D09 fixed), 18 failed (open defects) |
| Defect Selenium tests | 3 | 3 passed |
| **Defect total** | **24** | 6 passed, 18 failed |
| **Grand total** | **128** | 110 passed, 18 failed |

## Defect reporting

32 defects are recorded in `ProgramOwnerDefectReports.md`: 10 confirmed (open), 1 partly fixed, 4 fixed and 17 open from code review. Severity: 1 critical, 9 high, 7 medium, 15 low.

## Selenium coverage

11 browser tests in `ProgramOwnerSeleniumFlowTests.cs`, all passing (`selenium-output.txt`):

| Test | Scenario | Result |
|---|---|---|
| Dashboard_IsShownAfterLogin | Login and dashboard | Pass |
| CreateProgram_ValidInput_AppearsOnDashboard | Create a program through the form | Pass |
| CreateProgram_DurationUnderFiveMinutes_KeepsTheTimesTheUserTyped | Client-side 5-minute alert; typed times stay in the form | Pass |
| AddCandidate_ValidInput_ShowsSuccess | Add candidate | Pass |
| AddCandidate_DuplicateCode_ShowsError | Duplicate candidate code | Pass |
| AddCandidate_JavascriptImageUrl_IsRejected | javascript: image URL rejected | Pass |
| AddCandidate_ProtocolRelativeImageUrl_IsRejected | //host image URL rejected | Pass |
| AddCandidate_SingleCharacterName_IsRejected | 1-character name rejected | Pass |
| RegisterVoter_ValidInput_ShowsSuccess | Register voter | Pass |
| RegisterVoter_DuplicateEmail_ShowsError | Duplicate voter email | Pass |
| DeleteProgram_Unpublished_RemovesItFromTheDashboard | Delete unpublished program (confirm dialog) | Pass |

The tests run against a local copy of the app with a test account; they create programs, candidates and voters, so use a test database. Edit, publish and candidate-delete flows are not covered by browser tests.