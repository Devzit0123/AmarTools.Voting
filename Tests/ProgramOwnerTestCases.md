# ProgramOwnerController test case design and execution

Scope: `Controllers/ProgramOwnerController.cs` and the services it calls.

## Execution summary

| Run | Result | Evidence |
|---|---|---|
| Non-defect (`Category!=Defect`) | 104 tests: 104 passed | `nondefect-output.txt`, `Tests/TestResults/nondefect.trx` |
| Defect (`Category=Defect`) | 24 tests: 6 passed, 18 failed | `defect-output.txt`, `Tests/TestResults/defect.trx` |
| Selenium flows | 11 tests: 11 passed | `selenium-output.txt`, `Tests/TestResults/selenium.trx` |
| **Combined** | **128 tests: 110 passed, 18 failed** |  |

Test project: `Tests/AmarTools.Voting.Tests.csproj` (xUnit, Moq, EF Core InMemory, Selenium WebDriver). Selenium tests need `AMARTOOLS_BASE_URL`, `AMARTOOLS_TEST_EMAIL` and `AMARTOOLS_TEST_PASSWORD`; without them they are reported as skipped.

## Test counts

| Group | Tests | File(s) | Result |
|---|---:|---|---|
| Existing controller tests | 53 | ProgramOwnerControllerTests.cs | 53 passed |
| Time and vote-path tests | 9 | TimeBehaviorTests.cs, VotePathTests.cs | 9 passed |
| New BVA / ECT / basis-path unit tests | 34 | ProgramOwnerBoundaryDefectTests.cs (ProgramOwnerBoundaryTests) | 34 passed |
| New Selenium flows (non-defect) | 8 | ProgramOwnerSeleniumFlowTests.cs | 8 passed |
| **Non-defect total** | **104** |  | **104 passed** |
| Defect unit tests | 21 | ProgramOwnerBoundaryDefectTests.cs (ProgramOwnerDefectTests) | 3 passed (fixed defects), 18 failed (open defects) |
| Defect Selenium tests | 3 | ProgramOwnerSeleniumFlowTests.cs | 3 passed |
| **Defect total** | **24** |  | **6 passed, 18 failed** |
| **Grand total** | **128** |  | **110 passed, 18 failed** |

## Designed test cases (60): BVA 20, ECT 20, basis path 20

Result is from the runs above. A case marked Fail is a confirmed defect; a case marked Pass whose defect is Fixed now acts as a regression test. Test names starting with `PO-` refer to the case matrix further down.

| ID | Function | Technique | Priority | Preconditions | Test data | Steps | Expected result | Automated test | Result | Actual result | Defect |
|---|---|---|---|---|---|---|---|---|---|---|---|
| BVA-01 | Create/Edit (service) | BVA | High | Logged in as owner | Start +1h, End = Start + 299 s | Submit create form | Rejected: end must be at least 5 minutes after start | ProgramDuration_AtBoundary_IsEnforced(299,false) | Pass | As expected; test passed. |  |
| BVA-02 | Create/Edit (service) | BVA | High | Logged in as owner | End = Start + 300 s | Submit create form | Accepted | ProgramDuration_AtBoundary_IsEnforced(300,true) | Pass | As expected; test passed. |  |
| BVA-03 | Create/Edit (service) | BVA | Medium | Logged in as owner | End = Start + 301 s | Submit create form | Accepted | ProgramDuration_AtBoundary_IsEnforced(301,true) | Pass | As expected; test passed. |  |
| BVA-04 | Create POST | BVA | Medium | Model validation | ProgramName length 2 | Validate model | Validation error (min 3) | ProgramName_LengthBoundary_MatchesModelRule(2,false) | Pass | As expected; test passed. |  |
| BVA-05 | Create POST | BVA | Medium | Model validation | ProgramName length 3 | Validate model | Valid | ProgramName_LengthBoundary_MatchesModelRule(3,true) | Pass | As expected; test passed. |  |
| BVA-06 | Create POST | BVA | Medium | Model validation | ProgramName length 200 | Validate model | Valid | ProgramName_LengthBoundary_MatchesModelRule(200,true) | Pass | As expected; test passed. |  |
| BVA-07 | Create POST | BVA | Medium | Model validation | ProgramName length 201 | Validate model | Validation error (max 200) | ProgramName_LengthBoundary_MatchesModelRule(201,false) | Pass | As expected; test passed. |  |
| BVA-08 | Create POST | BVA | Low | Model validation | Description 2000 / 2001 chars | Validate model | 2000 valid, 2001 error | ProgramDescription_LengthBoundary_MatchesModelRule | Pass | As expected; test passed. |  |
| BVA-09 | AddCandidate | BVA | Low | Upcoming program, owner | name = 'A' (1 char), code C1 | POST AddCandidate | Rejected (min 2) | D07_AddCandidate_SingleCharacterName_IsRejected | Pass | Rejected after the fix; defect D07 is fixed and the regression test passes. | D07 |
| BVA-10 | AddCandidate | BVA | Medium | Upcoming program, owner | name 2 chars, code 1 char | POST AddCandidate | Saved | AddCandidate_NameAndCodeAtUpperAndLowerValidBounds_AreSaved(2,1) | Pass | As expected; test passed. |  |
| BVA-11 | AddCandidate | BVA | Medium | Upcoming program, owner | name 150 chars, code 50 chars | POST AddCandidate | Saved | AddCandidate_NameAndCodeAtUpperAndLowerValidBounds_AreSaved(150,50) | Pass | As expected; test passed. |  |
| BVA-12 | AddCandidate | BVA | Low | Upcoming program, owner | name 151 chars | POST AddCandidate | Rejected with message | D08_..._IsRejectedWithValidationMessage("name") | Pass | Rejected after the fix; defect D08 (name) is fixed and the regression test passes. | D08 |
| BVA-13 | AddCandidate | BVA | Low | Upcoming program, owner | code 51 chars | POST AddCandidate | Rejected with message | D08_..._IsRejectedWithValidationMessage("code") | Fail | Not rejected / incorrect behaviour; defect D08 confirmed (assertion failure). | D08 |
| BVA-14 | AddCandidate | BVA | Low | Upcoming program, owner | description 501 chars | POST AddCandidate | Rejected with message | D08_..._IsRejectedWithValidationMessage("description") | Fail | Not rejected / incorrect behaviour; defect D08 confirmed (assertion failure). | D08 |
| BVA-15 | AddCandidate | BVA | Low | Upcoming program, owner | imageUrl 501 chars | POST AddCandidate | Rejected with message | D08_..._IsRejectedWithValidationMessage("imageUrl") | Fail | Not rejected / incorrect behaviour; defect D08 confirmed (assertion failure). | D08 |
| BVA-16 | Edit GET | BVA | Medium | No programs | id = 0 | GET /ProgramOwner/Edit/0 | 404 NotFound | EditGet_NonExistingIdBoundaries_ReturnNotFound(0) | Pass | As expected; test passed. |  |
| BVA-17 | Edit GET | BVA | Medium | No programs | id = -1 and int.MinValue | GET Edit | 404 NotFound | EditGet_NonExistingIdBoundaries_ReturnNotFound(-1, int.MinValue) | Pass | As expected; test passed. |  |
| BVA-18 | Edit GET | BVA | Low | No programs | id = int.MaxValue | GET Edit | 404 NotFound | EditGet_NonExistingIdBoundaries_ReturnNotFound(int.MaxValue) | Pass | As expected; test passed. |  |
| BVA-19 | ManageCandidates | BVA | Low | Service returns null | programId = 0, -1, int.MaxValue | GET ManageCandidates | 404 NotFound | ManageCandidates_NonExistingIdBoundaries_ReturnNotFound | Pass | As expected; test passed. |  |
| BVA-20 | Delete | BVA | High | Published program, owner | EndTime = now-1 s (ended) / now+60 s (active) | POST Delete | Ended: deleted. Active: refused | Delete_PublishedProgramAroundEndTime_RespectsBoundary | Pass | As expected; test passed. |  |
| ECT-01 | ManageCandidates | ECT | High | Program owned by user | Owner | GET ManageCandidates | View returned | PO-018 (existing) | Pass | As expected; test passed. |  |
| ECT-02 | ManageCandidates | ECT | High | Program owned by someone else | Admin role | GET ManageCandidates | View returned | PO-020 (existing) | Pass | As expected; test passed. |  |
| ECT-03 | ManageVoters | ECT | High | Program owned by someone else | Non-owner, non-admin | GET ManageVoters | Forbid | PO-049 (existing) | Pass | As expected; test passed. |  |
| ECT-04 | MyPrograms | ECT | High | No NameIdentifier claim | Anonymous identity | GET MyPrograms | Unauthorized | PO-001 (existing) | Pass | As expected; test passed. |  |
| ECT-05 | Results | ECT | Medium | Program does not exist | programId = 999 | GET Results | NotFound | PO-044 (existing) | Pass | As expected; test passed. |  |
| ECT-06 | AddCandidate | ECT | Medium | Owner | imageUrl http://x.example/a.png | POST AddCandidate | Saved | PO-030 (existing) | Pass | As expected; test passed. |  |
| ECT-07 | AddCandidate | ECT | Medium | Owner | imageUrl https://x.example/a.png | POST AddCandidate | Saved | PO-031 (existing) | Pass | As expected; test passed. |  |
| ECT-08 | AddCandidate | ECT | Medium | Owner | imageUrl /images/a.png | POST AddCandidate | Saved | PO-032 (existing) | Pass | As expected; test passed. |  |
| ECT-09 | AddCandidate | ECT | Medium | Owner | imageUrl //evil.example/a.png | POST AddCandidate | Rejected | D09_AddCandidate_ProtocolRelativeImageUrl_IsRejected | Pass | Rejected after the fix; defect D09 is fixed and the regression test passes. | D09 |
| ECT-10 | AddCandidate | ECT | High | Owner | imageUrl javascript:alert(1) | POST AddCandidate | Rejected | PO-034 (existing) | Pass | As expected; test passed. |  |
| ECT-11 | AddCandidate | ECT | Medium | Owner | imageUrl ftp://x.example/a.png | POST AddCandidate | Rejected | PO-035 (existing) | Pass | As expected; test passed. |  |
| ECT-12 | AddCandidate | ECT | Medium | Candidate code 'A' exists | code 'A' again | POST AddCandidate | Rejected as duplicate | PO-029 (existing) | Pass | As expected; test passed. |  |
| ECT-13 | AddCandidate | ECT | Low | Candidate code 'A' exists | code 'a' | POST AddCandidate | Rejected as duplicate | D10_AddCandidate_CodeThatDiffersOnlyByCase_IsRejectedAsDuplicate | Fail | Not rejected / incorrect behaviour; defect D10 confirmed (assertion failure). | D10 |
| ECT-14 | AddCandidate | ECT | High | Active published program | Valid candidate | POST AddCandidate | Rejected: ballot frozen | D02_AddCandidate_WhileProgramIsActive_IsRejected | Fail | Not rejected / incorrect behaviour; defect D02 confirmed (assertion failure). | D02 |
| ECT-15 | AddCandidate | ECT | High | Ended program | Valid candidate | POST AddCandidate | Rejected | D02_AddCandidate_AfterProgramEnded_IsRejected | Fail | Not rejected / incorrect behaviour; defect D02 confirmed (assertion failure). | D02 |
| ECT-16 | DeleteCandidate | ECT | High | Active published program | Existing candidate | POST DeleteCandidate | Rejected | D02_DeleteCandidate_WhileProgramIsActive_IsRejected | Fail | Not rejected / incorrect behaviour; defect D02 confirmed (assertion failure). | D02 |
| ECT-17 | DeleteCandidate | ECT | High | Candidate has a vote | Existing candidate | POST DeleteCandidate | Rejected; votes preserved | D03_DeleteCandidate_WithRecordedVotes_IsRejected | Fail | Not rejected / incorrect behaviour; defect D03 confirmed (assertion failure). | D03 |
| ECT-18 | RegisterVoter | ECT | Medium | Owner program | email 'not-an-email' | RegisterVoterByEmailAsync | Rejected | D14_RegisterVoter_InvalidEmailFormat_IsRejected | Fail | Not rejected / incorrect behaviour; defect D14 confirmed (assertion failure). | D14 |
| ECT-19 | RegisterVoter | ECT | Medium | Ended program | Valid voter | RegisterVoterByEmailAsync | Rejected | D14_RegisterVoter_AfterElectionEnded_IsRejected | Fail | Not rejected / incorrect behaviour; defect D14 confirmed (assertion failure). | D14 |
| ECT-20 | Edit POST | ECT | High | Active program | New Start/End in the future | ValidateAndUpdateProgramAsync | Rejected | D01_Edit_StartedProgram_CannotBeRescheduledIntoTheFuture | Fail | Not rejected / incorrect behaviour; defect D01 confirmed (assertion failure). | D01 |
| BP-01 | MyPrograms | Basis path | High | No user id | — | GET MyPrograms | Unauthorized (P1) | PO-001 (existing) | Pass | As expected; test passed. |  |
| BP-02 | MyPrograms | Basis path | High | Owner has 1 program | owner-1 | GET MyPrograms | View with owner's programs (P2) | PO-002 (existing) | Pass | As expected; test passed. |  |
| BP-03 | Create POST | Basis path | High | Invalid ModelState | Empty name | POST Create | Form returned, ValuesAreUtc=false (P1) | PO-005 (existing) | Pass | As expected; test passed. |  |
| BP-04 | Create POST | Basis path | High | Service fails with a message | — | POST Create | Form + model error (P2) | PO-006 (existing) | Pass | As expected; test passed. |  |
| BP-05 | Create POST | Basis path | Medium | Service fails with no message | — | POST Create | Form returned, no model error (P3) | CreatePost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError | Pass | As expected; test passed. |  |
| BP-06 | Create POST | Basis path | High | Service succeeds, unpublished | — | POST Create | Redirect to MyPrograms (P4) | PO-007 (existing) | Pass | As expected; test passed. |  |
| BP-07 | Create POST | Basis path | High | Service succeeds, published | IsPublished=true | POST Create | Published link stored, redirect (P5) | PO-008 (existing) | Pass | As expected; test passed. |  |
| BP-08 | Edit POST | Basis path | High | id != model.Id | id=1, model.Id=2 | POST Edit | NotFound (P1) | PO-013 (existing) | Pass | As expected; test passed. |  |
| BP-09 | Edit POST | Basis path | Medium | Service fails with no message | — | POST Edit | Form returned, no model error (P4) | EditPost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError | Pass | As expected; test passed. |  |
| BP-10 | Edit POST | Basis path | High | Service succeeds, published | IsPublished=true | POST Edit | Link stored, redirect (P6) | EditPost_PublishedSuccess_StoresPublishedLinkAndRedirects | Pass | As expected; test passed. |  |
| BP-11 | AddCandidate | Basis path | High | Owner | name = '' | POST AddCandidate | Error + redirect (P1) | PO-022 (existing) | Pass | As expected; test passed. |  |
| BP-12 | AddCandidate | Basis path | High | Admin, program of another owner | Valid data | POST AddCandidate | Saved (P5) | AddCandidate_AdminOnAnotherOwnersProgram_SavesCandidate | Pass | As expected; test passed. |  |
| BP-13 | AddCandidate | Basis path | Medium | Owner | imageUrl 'example.com/a.png' | POST AddCandidate | Rejected (P12) | PO-036 (existing) | Pass | As expected; test passed. |  |
| BP-14 | DeleteCandidate | Basis path | High | Candidate belongs to another program | candidateId of program 2, programId 1 | POST DeleteCandidate | Forbid (P5) | PO-043 (existing) | Pass | As expected; test passed. |  |
| BP-15 | DeleteCandidate | Basis path | High | Owner, upcoming program | Own candidate | POST DeleteCandidate | Deleted (P7) | PO-040 (existing) | Pass | As expected; test passed. |  |
| BP-16 | Results | Basis path | Medium | Admin, program of another owner | — | GET Results | View returned (P4) | Results_AdminOnAnotherOwnersProgram_ReturnsView | Pass | As expected; test passed. |  |
| BP-17 | RegisterVoter POST | Basis path | High | Service fails | — | POST RegisterVoter | Redirect with error (P4) | PO-054 (existing) | Pass | As expected; test passed. |  |
| BP-18 | RegisterVoter POST | Basis path | Medium | Admin, program of another owner | Valid voter | POST RegisterVoter | Service called (P3) | RegisterVoterPost_AdminOnAnotherOwnersProgram_CallsService | Pass | As expected; test passed. |  |
| BP-19 | Delete | Basis path | High | Published, active program | — | POST Delete | Refused with error (P4) | PO-061 (existing) | Pass | As expected; test passed. |  |
| BP-20 | Delete | Basis path | Medium | Unpublished program | — | POST Delete | Deleted (P5) | Delete_UnpublishedProgram_RemovesProgram | Pass | As expected; test passed. |  |

Result totals for the 60 cases: 49 Pass, 11 Fail (executed 2026-10-09 by Binoy Dev).

## Existing controller case matrix (PO-001 to PO-062)

Every test behind these cases is in the non-defect run, which passed (104 of 104).

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

## BVA, ECT and basis-path documents

The boundary-value table, the equivalence classes, and the cyclomatic complexity and independent paths of the 10 functions are in `ProgramOwnerTestMatrix.md` and in the workbook `ProgramOwner_TestDesign.xlsx` (sheets `BVA`, `ECT`, `BasisPath`).