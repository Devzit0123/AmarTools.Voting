# ProgramOwnerController test matrix

The executable suite is `ProgramOwnerControllerTests.cs`, `ProgramOwnerBoundaryDefectTests.cs`, `ProgramOwnerSeleniumFlowTests.cs`, `TimeBehaviorTests.cs` and `VotePathTests.cs`, using xUnit, Moq, EF Core InMemory and Selenium.

Last updated 2026-10-13. Line numbers and paths refer to the current `Controllers/ProgramOwnerController.cs`, which includes the defect fixes from commit 4896cce and from 2026-10-13.

## Coverage modules and defects

| Module | Covered paths | Defects |
|---|---|---|
| MyPrograms | Missing identity; owner query; owner-only filtering | D12 (fixed: counts come from the database) |
| Create (GET/POST) | GET defaults; invalid model; service failure with and without message; success unpublished/published | D06, D13 (fixed) |
| Edit (GET/POST) | Missing program; owner/admin/other authorization; ID mismatch; invalid model; service failure with and without message; success published | D01 (fixed; plus a regression guard that renaming an active program still works) |
| ManageCandidates | Missing program; owner/admin authorization; successful view; header vote count | D30 (fixed: XSS in the delete confirmation), D34 (fixed: header always showed 0 votes) |
| AddCandidate | Required fields; length limits; missing program; ownership; admin; started program; exact and case-variant duplicate code; image URL classes; save failure | D02, D07, D08, D09, D10 (fixed) |
| DeleteCandidate | Missing program; missing or wrong-program candidate; ownership/admin; started program; recorded votes; save failure; owner delete | D02, D03, D11 (fixed) |
| Results | Missing program; ownership; admin; results and blockchain status | D18, D19, D23 (other classes, open) |
| ManageVoters | Missing program; ownership; owner and admin listing | D16, D17 (other classes, open) |
| RegisterVoter (GET/POST) | Missing program; owner/admin/other authorization; service failure; success | D11, D14, D29 (fixed) |
| RemoveVoter | Missing program; service result | D04, D11 (fixed) |
| Delete | Missing/unauthorized; admin; active published refused; upcoming published refused; unpublished; ended; votes recorded; save failure; end-time boundary | D05, D33 (fixed) |

## Boundary value analysis (BVA)

Seven boundary points per bounded input: below minimum, minimum, minimum+1, nominal, maximum-1, maximum, above maximum. All points listed are executed by an automated test.

| Input | Function | Constraint (source) | Below min | Min | Min+1 | Nominal | Max-1 | Max | Above max | Expected | Cases |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Program duration (seconds) | Create/Edit (service) | End - Start >= 300 s (MinimumProgramDuration) | 299 | 300 | 301 | 3600 | — | — | — | <300 rejected; >=300 accepted | BVA-01..03 |
| ProgramName length | Create/Edit POST | 3..200 (model StringLength) | 2 | 3 | 4 | 20 | 199 | 200 | 201 | Outside range -> validation error | BVA-04..07 |
| Description length | Create/Edit POST | <= 2000 (model) | — | 0 | 1 | 200 | 1999 | 2000 | 2001 | 2001 -> validation error | BVA-08 |
| Candidate name length | AddCandidate | 2..150 (model; enforced in the action since 4896cce) | 1 | 2 | 3 | 12 | 149 | 150 | 151 | Outside range rejected (D07, D08 fixed) | BVA-09..12 |
| Candidate code length | AddCandidate | 1..50 (model; enforced in the action since 4896cce) | 0 (blank) | 1 | 2 | 6 | 49 | 50 | 51 | Outside range rejected (D08 fixed) | BVA-10, 11, 13 |
| Candidate description length | AddCandidate | <= 500 | — | 0 | 1 | 80 | 499 | 500 | 501 | 501 rejected (D08 fixed) | BVA-14 |
| Image URL length | AddCandidate | <= 500 | — | 1 | 2 | 40 | 499 | 500 | 501 | 501 rejected (D08 fixed) | BVA-15 |
| Program / candidate / voter id | Edit, ManageCandidates, Delete, … | Positive existing id | int.MinValue, -1, 0 | 1 (existing) | 2 | — | — | — | int.MaxValue | Non-existing ids -> 404 | BVA-16..19 |
| Election end time vs now (delete) | Delete | HasEnded = now >= EndTime | EndTime = now+60 s | — | — | — | — | — | EndTime = now-1 s | Active: refused; ended: deleted | BVA-20 |
| Voter name length | RegisterVoter (service) | 2..150 (model; enforced in the service since 4896cce) | 1 | 2 | 3 | 12 | 149 | 150 | 151 | Outside range rejected (D29 fixed) | D29_RegisterVoter_NameLengthBoundary_IsEnforced |
| Voter email | RegisterVoter (service) | valid format | 'not-an-email' | — | — | name@example.com | — | — | — | Invalid format rejected (D14 fixed) | ECT-18 |

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
| Program state | EC8 | Active (started, not ended) | Invalid for ballot changes | Start -1h, End +1h, published | Candidate add/delete, voter registration and rescheduling rejected | ECT-14, ECT-16, ECT-20, BP-19 |
| Program state | EC9 | Ended | Invalid for changes | End -1h | Add candidate / register voter rejected; delete keeps data that has votes | ECT-15, ECT-19, BVA-20 |
| Candidate name | EC10 | Valid length 2..150 | Valid | Alice Rahman | Saved | BVA-10, BVA-11 |
| Candidate name | EC11 | Blank / whitespace | Invalid | '', '  ', null | Rejected | BP-11 (PO-021..023) |
| Candidate name | EC12 | Too short or too long | Invalid | 'A', 151 chars | Rejected | BVA-09, BVA-12 |
| Candidate code | EC13 | New unique code | Valid | B2 | Saved | BP-12 |
| Candidate code | EC14 | Exact duplicate | Invalid | A | Rejected | ECT-12 |
| Candidate code | EC15 | Case-variant duplicate | Invalid | a | Rejected | ECT-13 |
| Image URL | EC16 | Empty / null | Valid | '' | Saved with ImageUrl = null | (PO-038) |
| Image URL | EC17 | Absolute http/https | Valid | https://x.example/a.png | Saved | ECT-06, ECT-07 |
| Image URL | EC18 | Root-relative | Valid | /images/a.png | Saved | ECT-08 |
| Image URL | EC19 | Protocol-relative or '/\' prefix | Invalid | //evil.example/a.png, /\evil.example/a.png | Rejected | ECT-09, AddCandidate_SlashBackslashOrDoubleSlashImageUrl_IsRejected |
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

Each function is modelled as a control-flow graph (`BasisPath_Diagram1.png`, `BasisPath_Diagram2.png`; node list in `ProgramOwner_BasisPath.xlsx`, sheet Node Legend). V(G) = decision nodes + 1, cross-checked with V(G) = E - N + 2. Every condition that branches is a decision node: each operand of `&&` / `||` inside an `if`, each `?:` ternary and each `catch`. The last operand of a `&&` / `||` chain that is only assigned to a variable (`uri.Scheme == https`, `!StartsWith("/\\")`) does not branch, so it is a process node. The fixes added branches (length limits, started-program and vote checks, the Delete message ternary), so V(G) is higher than in the 2026-10-09 analysis.

| Function | Source lines | Decision nodes (P) | Edges (E) | Nodes (N, incl. End) | V(G) = P + 1 | V(G) = E - N + 2 | Paths listed |
|---|---|---:|---:|---:|---:|---:|---:|
| MyPrograms | 33-48 | 1 | 7 | 7 | 2 | 2 | 2 |
| Create (POST) | 66-90 | 4 | 15 | 12 | 5 | 5 | 5 |
| Edit (POST) | 116-139 | 5 | 18 | 14 | 6 | 6 | 6 |
| ManageCandidates (GET) | 143-155 | 3 | 12 | 10 | 4 | 4 | 4 |
| AddCandidate (POST) | 160-250 | 21 | 63 | 43 | 22 | 22 | 22 |
| DeleteCandidate (POST) | 255-291 | 7 | 24 | 18 | 8 | 8 | 8 |
| Results (GET) | 295-311 | 3 | 12 | 10 | 4 | 4 | 4 |
| ManageVoters (GET) | 315-335 | 3 | 12 | 10 | 4 | 4 | 4 |
| RegisterVoter (POST) | 354-372 | 4 | 14 | 11 | 5 | 5 | 5 |
| Delete (POST) | 396-431 | 8 | 24 | 17 | 9 | 9 | 9 |
| **Total** |  | **59** |  |  | **69** |  | **69** |

Within each function the listed paths are linearly independent (the rank of their edge vectors equals V(G)), so they form a basis set.

### Independent paths

| Function | Path | Node sequence | Condition | Expected outcome | Test |
|---|---|---|---|---|---|
| MyPrograms | P1 | 1 → 2(T) → 3 → End | userId empty | Unauthorized | MyPrograms_WithoutUserId_ReturnsUnauthorized |
| MyPrograms | P2 | 1 → 2(F) → 4 → 5 → 6 → End | userId present | View with only this owner's programs | MyPrograms_ReturnsOnlyCurrentOwnersPrograms |
| Create (POST) | P1 | 1 → 2(T) → 3 → End | ModelState invalid | View(form), ValuesAreUtc = false | CreatePost_InvalidModel_ReturnsLocalInputMode |
| Create (POST) | P2 | 1 → 2(F) → 4 → 5(T) → 6(T) → 7 → 8 → End | service fails, message given | View(form) with model error | CreatePost_ServiceFailure_ReturnsValidationError |
| Create (POST) | P3 | 1 → 2(F) → 4 → 5(T) → 6(F) → 8 → End | service fails, no message | View(form), no model error | CreatePost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError |
| Create (POST) | P4 | 1 → 2(F) → 4 → 5(F) → 9(F) → 11 → End | success, IsPublished = false | Redirect to MyPrograms | CreatePost_PublishedProgram_RedirectsToMyPrograms |
| Create (POST) | P5 | 1 → 2(F) → 4 → 5(F) → 9(T) → 10 → 11 → End | success, IsPublished = true | PublishedLink stored + redirect | CreatePost_PublishedSuccess_StoresPublishedLinkAndRedirects |
| Edit (POST) | P1 | 1(T) → 2 → End | id differs from model.Id | NotFound | EditPost_IdMismatch_ReturnsNotFound |
| Edit (POST) | P2 | 1(F) → 3 → 4(T) → 5 → End | ModelState invalid | View(form) | EditPost_InvalidModel_ReturnsForm |
| Edit (POST) | P3 | 1(F) → 3 → 4(F) → 6 → 7(T) → 8(T) → 9 → 10 → End | service fails, message given | View(form) with model error | EditPost_ServiceFailureWithMessage_ReturnsFormWithModelError |
| Edit (POST) | P4 | 1(F) → 3 → 4(F) → 6 → 7(T) → 8(F) → 10 → End | service fails, no message | View(form), no model error | EditPost_ServiceFailureWithoutMessage_ReturnsFormWithoutModelError |
| Edit (POST) | P5 | 1(F) → 3 → 4(F) → 6 → 7(F) → 11(F) → 13 → End | success, IsPublished = false | Redirect to MyPrograms | EditPost_ServiceSuccess_Redirects |
| Edit (POST) | P6 | 1(F) → 3 → 4(F) → 6 → 7(F) → 11(T) → 12 → 13 → End | success, IsPublished = true | PublishedLink stored + redirect | EditPost_PublishedSuccess_StoresPublishedLinkAndRedirects |
| ManageCandidates (GET) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | ManageCandidates_MissingProgram_ReturnsNotFound |
| ManageCandidates (GET) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid | ManageCandidates_EnforcesOwnership(admin: False, allowed: False) |
| ManageCandidates (GET) | P3 | 1 → 2(F) → 4(T) → 5(F) → 7 → 8 → 9 → End | not owner, is admin | View with candidates | ManageCandidates_EnforcesOwnership(admin: True, allowed: True) |
| ManageCandidates (GET) | P4 | 1 → 2(F) → 4(F) → 7 → 8 → 9 → End | current user is owner | View with candidates | ManageCandidates_CurrentOwner_ReturnsCandidateView |
| AddCandidate (POST) | P1 | 1(T) → 3 → End | name blank | Redirect with error | AddCandidate_RequiredFieldsRedirect(name: null, code: "A")<br>AddCandidate_RequiredFieldsRedirect(name: "", code: "A")<br>AddCandidate_RequiredFieldsRedirect(name: " ", code: "A") |
| AddCandidate (POST) | P2 | 1(F) → 2(T) → 3 → End | name given, code blank | Redirect with error | AddCandidate_RequiredFieldsRedirect(name: "Candidate", code: null)<br>AddCandidate_RequiredFieldsRedirect(name: "Candidate", code: "")<br>AddCandidate_RequiredFieldsRedirect(name: "Candidate", code: " ") |
| AddCandidate (POST) | P3 | 1(F) → 2(F) → 4 → 5(T) → 7 → End | name shorter than 2 | Redirect with error | D07_AddCandidate_SingleCharacterName_IsRejected |
| AddCandidate (POST) | P4 | 1(F) → 2(F) → 4 → 5(F) → 6(T) → 7 → End | name longer than 150 | Redirect with error | D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage(field: "name") |
| AddCandidate (POST) | P5 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(T) → 14 → End | code longer than 50 | Redirect with error | D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage(field: "code") |
| AddCandidate (POST) | P6 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(F) → 10 → 11(F) → 12(T) → 14 → End | description longer than 500 | Redirect with error | D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage(field: "description") |
| AddCandidate (POST) | P7 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(T) → 14 → End | image URL longer than 500 | Redirect with error | D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage(field: "imageUrl") |
| AddCandidate (POST) | P8 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(T) → 17 → End | program not found | NotFound | AddCandidate_MissingProgram_ReturnsNotFound |
| AddCandidate (POST) | P9 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(T) → 19(T) → 20 → End | not owner, not admin | Forbid | AddCandidate_OtherOwner_ReturnsForbid |
| AddCandidate (POST) | P10 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(T) → 19(F) → 21(F) → 23 → 24(F) → 26 → 27(F) → 39 → 40(F) → 42 → End | not owner, is admin | Candidate saved | AddCandidate_AdminOnAnotherOwnersProgram_SavesCandidate |
| AddCandidate (POST) | P11 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(T) → 22 → End | program has started (active or ended) | Redirect with error | D02_AddCandidate_WhileProgramIsActive_IsRejected<br>D02_AddCandidate_AfterProgramEnded_IsRejected |
| AddCandidate (POST) | P12 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(T) → 25 → End | code already used in program (any case) | Redirect with error | AddCandidate_DuplicateCode_RedirectsWithoutAdding<br>D10_AddCandidate_CodeThatDiffersOnlyByCase_IsRejectedAsDuplicate |
| AddCandidate (POST) | P13 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(F) → 39 → 40(F) → 42 → End | owner; no image; description blank | Candidate saved with nulls, success message | AddCandidate_NameAndCodeAtUpperAndLowerValidBounds_AreSaved(nameLength: 2, codeLength: 1)<br>AddCandidate_NameAndCodeAtUpperAndLowerValidBounds_AreSaved(nameLength: 150, codeLength: 50) |
| AddCandidate (POST) | P14 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(F) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(F) → 39 → 40(F) → 42 → End | description given, no image | Candidate saved, description trimmed | AddCandidate_DescriptionWithoutImage_IsSavedTrimmed |
| AddCandidate (POST) | P15 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(T) → 30(T) → 32(F) → 35(F) → 38 → 39 → 40(F) → 42 → End | image URL is absolute http | Candidate saved | AddCandidate_ValidImageUrl_Saves(imageUrl: "http://example.com/image.png") |
| AddCandidate (POST) | P16 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(T) → 30(F) → 31 → 32(F) → 35(F) → 38 → 39 → 40(F) → 42 → End | image URL is absolute https | Candidate saved | AddCandidate_ValidImageUrl_Saves(imageUrl: "https://example.com/image.png")<br>AddCandidate_DescriptionAndImageUrlAtMaximumLength_AreSaved |
| AddCandidate (POST) | P17 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(F) → 32(T) → 33(T) → 34 → 35(T) → 36(F) → 38 → 39 → 40(F) → 42 → End | root-relative URL (/images/..; not absolute on Windows) | Candidate saved | AddCandidate_ValidImageUrl_Saves(imageUrl: "/images/candidate.png")<br>AddCandidate_ValidImageUrl_Saves(imageUrl: "  /images/candidate.png  ") |
| AddCandidate (POST) | P18 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(T) → 30(F) → 31 → 32(F) → 35(T) → 36(T) → 37 → End | absolute URL, other scheme (javascript:, ftp:, data:) | Redirect with error | AddCandidate_InvalidImageUrl_Redirects(imageUrl: "javascript:alert(1)")<br>AddCandidate_InvalidImageUrl_Redirects(imageUrl: "ftp://example.com/image.png")<br>AddCandidate_InvalidImageUrl_Redirects(imageUrl: "data:image/png;base64,abc") |
| AddCandidate (POST) | P19 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(F) → 32(F) → 35(T) → 36(T) → 37 → End | not absolute, no leading '/' (example.com/..) | Redirect with error | AddCandidate_InvalidImageUrl_Redirects(imageUrl: "example.com/image.png") |
| AddCandidate (POST) | P20 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(T) → 30(F) → 31 → 32(T) → 33(F) → 35(T) → 36(T) → 37 → End | protocol-relative '//host/..' (parsed as file: URI) | Redirect with error | D09_AddCandidate_ProtocolRelativeImageUrl_IsRejected<br>AddCandidate_SlashBackslashOrDoubleSlashImageUrl_IsRejected(imageUrl: "//evil.example/a.png") |
| AddCandidate (POST) | P21 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(T) → 28 → 29(T) → 30(F) → 31 → 32(T) → 33(T) → 34 → 35(T) → 36(T) → 37 → End | '/\host/..' (parsed as file: URI) | Redirect with error | AddCandidate_SlashBackslashOrDoubleSlashImageUrl_IsRejected(imageUrl: "/\\evil.example/a.png") |
| AddCandidate (POST) | P22 | 1(F) → 2(F) → 4 → 5(F) → 6(F) → 8 → 9(T) → 10 → 11(F) → 12(F) → 13(F) → 15 → 16(F) → 18(F) → 21(F) → 23 → 24(F) → 26 → 27(F) → 39 → 40(T) → 41 → End | SaveChanges throws DbUpdateException | Redirect with error | AddCandidate_SaveFails_RedirectsWithError |
| DeleteCandidate (POST) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | DeleteCandidate_MissingProgram_ReturnsNotFound<br>DeleteCandidate_MissingCandidate_ReturnsNotFound |
| DeleteCandidate (POST) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid | DeleteCandidate_EnforcesOwnership(admin: False, allowed: False) |
| DeleteCandidate (POST) | P3 | 1 → 2(F) → 4(T) → 5(F) → 7 → 8(F) → 10(F) → 12(F) → 14 → 15(F) → 17 → End | not owner, is admin | Candidate deleted | DeleteCandidate_EnforcesOwnership(admin: True, allowed: True) |
| DeleteCandidate (POST) | P4 | 1 → 2(F) → 4(F) → 7 → 8(T) → 9 → End | candidate missing or in another program | NotFound | DeleteCandidate_WrongProgram_ReturnsNotFound<br>D11_DeleteCandidate_ExistingAndMissingCandidateOfOtherProgram_BehaveTheSame |
| DeleteCandidate (POST) | P5 | 1 → 2(F) → 4(F) → 7 → 8(F) → 10(T) → 11 → End | program has started | Redirect with error, candidate kept | D02_DeleteCandidate_WhileProgramIsActive_IsRejected |
| DeleteCandidate (POST) | P6 | 1 → 2(F) → 4(F) → 7 → 8(F) → 10(F) → 12(T) → 13 → End | candidate has recorded votes | Redirect with error, votes kept | D03_DeleteCandidate_WithRecordedVotes_IsRejected |
| DeleteCandidate (POST) | P7 | 1 → 2(F) → 4(F) → 7 → 8(F) → 10(F) → 12(F) → 14 → 15(T) → 16 → End | SaveChanges throws DbUpdateException | Redirect with error | DeleteCandidate_SaveFails_RedirectsWithErrorAndKeepsCandidate |
| DeleteCandidate (POST) | P8 | 1 → 2(F) → 4(F) → 7 → 8(F) → 10(F) → 12(F) → 14 → 15(F) → 17 → End | owner deletes own candidate | Candidate deleted, success message | DeleteCandidate_Owner_DeletesCandidate |
| Results (GET) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | Results_MissingProgram_ReturnsNotFound |
| Results (GET) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid | Results_OtherOwner_ReturnsForbid |
| Results (GET) | P3 | 1 → 2(F) → 4(F) → 7 → 8 → 9 → End | current user is owner | Results view + blockchain status | Results_OwnerLoadsResultsAndBlockchainStatus |
| Results (GET) | P4 | 1 → 2(F) → 4(T) → 5(F) → 7 → 8 → 9 → End | not owner, is admin | Results view | Results_AdminOnAnotherOwnersProgram_ReturnsView |
| ManageVoters (GET) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | ManageVoters_MissingProgram_ReturnsNotFound |
| ManageVoters (GET) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid | ManageVoters_OtherOwner_ReturnsForbid |
| ManageVoters (GET) | P3 | 1 → 2(F) → 4(F) → 7 → 8 → 9 → End | current user is owner | View with voter list | ManageVoters_OwnerLoadsVoters |
| ManageVoters (GET) | P4 | 1 → 2(F) → 4(T) → 5(F) → 7 → 8 → 9 → End | not owner, is admin | View with voter list | ManageVoters_AdminOnAnotherOwnersProgram_ReturnsView |
| RegisterVoter (POST) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | D11_RegisterVoterPost_MissingProgram_ReturnsNotFoundLikeTheOtherActions |
| RegisterVoter (POST) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid; service not called | RegisterVoterPost_OtherOwner_ReturnsForbidWithoutCallingService |
| RegisterVoter (POST) | P3 | 1 → 2(F) → 4(T) → 5(F) → 7 → 8(F) → 10 → End | not owner, is admin; service succeeds | Redirect with success | RegisterVoterPost_AdminOnAnotherOwnersProgram_CallsService |
| RegisterVoter (POST) | P4 | 1 → 2(F) → 4(F) → 7 → 8(T) → 9 → End | owner; service fails | Redirect with error | RegisterVoterPost_ServiceFailure_RedirectsWithError |
| RegisterVoter (POST) | P5 | 1 → 2(F) → 4(F) → 7 → 8(F) → 10 → End | owner; service succeeds | Redirect with success | RegisterVoterPost_ServiceSuccess_Redirects |
| Delete (POST) | P1 | 1 → 2(T) → 3 → End | program not found | NotFound | Delete_MissingProgram_ReturnsNotFound |
| Delete (POST) | P2 | 1 → 2(F) → 4(T) → 5(T) → 6 → End | not owner, not admin | Forbid | Delete_OtherOwner_ReturnsForbid |
| Delete (POST) | P3 | 1 → 2(F) → 4(T) → 5(F) → 7(T) → 8(F) → 11(F) → 13 → 14(F) → 16 → End | not owner, is admin; published and ended | Program deleted | Delete_AdminOnAnotherOwnersEndedProgram_RemovesProgram |
| Delete (POST) | P4 | 1 → 2(F) → 4(F) → 7(F) → 11(F) → 13 → 14(F) → 16 → End | owner; not published | Program deleted | Delete_UnpublishedProgram_RemovesProgram |
| Delete (POST) | P5 | 1 → 2(F) → 4(F) → 7(T) → 8(F) → 11(F) → 13 → 14(F) → 16 → End | owner; published and ended | Program deleted | Delete_EndedProgram_RemovesProgram<br>Delete_PublishedProgramAroundEndTime_RespectsBoundary(secondsFromNow: -1, deleted: True) |
| Delete (POST) | P6 | 1 → 2(F) → 4(F) → 7(T) → 8(T) → 9(T) → 10 → End | published, started, not ended | Refused: 'voting is in progress' | Delete_ActivePublishedProgram_IsRejected<br>D33_Delete_ActiveProgram_DoesNotSuggestAnImpossibleUnpublish |
| Delete (POST) | P7 | 1 → 2(F) → 4(F) → 7(T) → 8(T) → 9(F) → 10 → End | published, not yet started | Refused: 'Unpublish it first' | Delete_PublishedUpcomingProgram_IsRefusedWithUnpublishHint |
| Delete (POST) | P8 | 1 → 2(F) → 4(F) → 7(F) → 11(T) → 12 → End | votes recorded | Refused, program and votes kept | D05_Delete_EndedProgramWithVotes_IsNotHardDeleted |
| Delete (POST) | P9 | 1 → 2(F) → 4(F) → 7(F) → 11(F) → 13 → 14(T) → 15 → End | SaveChanges throws DbUpdateException | Redirect with error | Delete_SaveFails_RedirectsWithError |

All 69 paths are feasible and each one is executed by a passing xUnit test (run 2026-10-13). The `DbUpdateException` paths use `FailingSaveInterceptor` from `ProgramOwnerBoundaryDefectTests.cs`, which makes the next `SaveChangesAsync` throw after the test data is seeded. Image-URL paths reflect `Uri.TryCreate` on .NET 8 / Windows: `//host` and `/\host` parse as absolute `file:` URIs, while `/images/..` and `example.com/..` do not parse.

## Test counts

| Category | Count | Result (2026-10-13) |
|---|---:|---|
| Controller tests (ProgramOwnerControllerTests.cs) | 53 | 53 passed |
| Time and vote-path tests (TimeBehaviorTests.cs, VotePathTests.cs) | 9 | 9 passed |
| BVA / ECT / basis-path unit tests (ProgramOwnerBoundaryTests) | 61 | 61 passed |
| Defect unit tests (ProgramOwnerDefectTests) | 34 | 34 passed |
| Selenium flows (ProgramOwnerSeleniumFlowTests) | 12 | 12 passed (selenium-final.trx). Re-run at 03:06 after the D12/D34 fix and an app restart: 12 passed again. |
| **Total** | **169** | **169 passed, 0 failed** |

## Defect reporting

`ProgramOwnerDefectReports.md` records 34 defects: 24 fixed and 10 open. No open defect is in ProgramOwnerController; the 10 open ones are in services, blockchain, Identity and the data model. Severity: 1 critical, 9 high, 7 medium, 17 low.

## Selenium coverage

There are 12 browser tests in `ProgramOwnerSeleniumFlowTests.cs`. All 12 passed on 2026-10-13 (`Tests/TestResults/Muskan_Binoy/selenium-final.trx` (2026-10-13 03:06, app at https://localhost:7135)); S01–S11 had also passed on 2026-10-09 (`selenium.trx`). The same run skipped 3 of the teammate's VotingController browser tests (SEL03, SEL04, SEL08) because their program-id environment variables were not set; they are outside this scope.

| Test | Scenario | Last result |
|---|---|---|
| S01 Dashboard_IsShownAfterLogin | Login and dashboard | Pass |
| S02 CreateProgram_ValidInput_AppearsOnDashboard | Create a program through the form | Pass |
| S03 CreateProgram_DurationUnderFiveMinutes_KeepsTheTimesTheUserTyped | Client-side 5-minute alert; typed times stay in the form | Pass |
| S04 AddCandidate_ValidInput_ShowsSuccess | Add candidate | Pass |
| S05 AddCandidate_DuplicateCode_ShowsError | Duplicate candidate code | Pass |
| S06 AddCandidate_JavascriptImageUrl_IsRejected | javascript: image URL rejected | Pass |
| S07 AddCandidate_ProtocolRelativeImageUrl_IsRejected | //host image URL rejected | Pass |
| S08 AddCandidate_SingleCharacterName_IsRejected | 1-character name rejected | Pass |
| S09 RegisterVoter_ValidInput_ShowsSuccess | Register voter | Pass |
| S10 RegisterVoter_DuplicateEmail_ShowsError | Duplicate voter email | Pass |
| S11 DeleteProgram_Unpublished_RemovesItFromTheDashboard | Delete unpublished program (confirm dialog) | Pass |
| S12 DeleteCandidate_NameWithQuotesAndMarkup_IsShownLiterallyInConfirmation | D30: a name with quotes and markup is shown literally in the confirm dialog | Pass |

The tests run against a local copy of the app with a test account. They create programs, candidates and voters, so use a test database. Edit and publish flows have no browser tests.
