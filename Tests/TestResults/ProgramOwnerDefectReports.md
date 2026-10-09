# ProgramOwnerController and codebase defect report

## Execution evidence

| Run | Command | Result | Evidence |
|---|---|---|---|
| Non-defect tests | `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category!=Defect"` | **104 passed, 0 failed** | `nondefect-output.txt`, `Tests/TestResults/nondefect.trx` |
| Defect tests | `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category=Defect"` | **18 failed, 6 passed** (24 tests) | `defect-output.txt`, `Tests/TestResults/defect.trx` |
| Selenium flows | `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "FullyQualifiedName~ProgramOwnerSeleniumFlowTests"` | **11 passed** (app at `http://localhost:5256`) | `selenium-output.txt`, `Tests/TestResults/selenium.trx` |

Total: 128 tests, 110 passed, 18 failed. All 18 failures are assertion failures (`Assert.True/False/Single/Equal/IsType` or the explicit D06 message); none is an exception.

## Summary

32 defects are recorded.

| Status | Count | Meaning |
|---|---|---|
| Confirmed | 10 | A defect test fails; the defect is open. |
| Partly fixed | 1 | Some cases fixed, some still fail. |
| Fixed | 4 | Fixed in the code; the regression test passes. |
| Open | 17 | Found by code review; not yet reproduced by a test run. |

| Severity | Count |
|---|---|
| Critical | 1 |
| High | 9 |
| Medium | 7 |
| Low | 15 |

## Defect list

| ID | Severity | Status | Title |
|---|---|---|---|
| D01 | High | Confirmed | Edit allows rescheduling/reopening a program after voting started or ended |
| D02 | High | Confirmed | Candidates can be added or deleted while the election is active or after it ended |
| D03 | High | Confirmed | Deleting a candidate that has votes silently deletes those votes (FK cascade) |
| D04 | High | Confirmed | Removing a voter who already voted deletes their vote (FK cascade) |
| D05 | Medium | Confirmed | A program with recorded votes can be hard-deleted as soon as it ends |
| D06 | Low | Confirmed | After a duration-validation failure the form re-renders with shifted times |
| D07 | Low | Fixed | Candidate name of 1 character is accepted (model requires 2..150) |
| D08 | Low | Partly fixed | No server-side maximum-length checks for candidate fields |
| D09 | Low | Fixed | Protocol-relative image URL (//host/img.png) is accepted |
| D10 | Low | Confirmed | Candidate-code uniqueness is case-sensitive |
| D11 | Low | Confirmed | Inconsistent NotFound/Forbid responses reveal which IDs exist |
| D12 | Medium | Open | MyPrograms and the dashboard load every vote, voter and candidate |
| D13 | Low | Confirmed | Create does not trim Description (Edit does); invalid-slug message is unreachable |
| D14 | Medium | Confirmed | Voter registration has no email-format check and ignores the election window |
| D15 | Critical | Open | Anyone can take over a pre-registered voter's slot by registering with that email |
| D16 | High | Open | Any signed-in user can self-register as a voter in any published program |
| D17 | High | Open | Ballot secrecy is not preserved |
| D18 | High | Open | Hash chain does not cover the vote's content |
| D19 | Medium | Open | Chain validation cannot detect missing or truncated blocks |
| D20 | Medium | Open | Block queue is in memory, drops writes when full, and is lost on restart |
| D21 | Low | Open | Retry delays block the single queue reader |
| D22 | High | Open | Search orders by an unmapped property and may return HTTP 500 |
| D23 | Medium | Open | Live tallies are visible to voters while voting is still open |
| D24 | Low | Open | Unpublished (draft) programs can be opened by id |
| D25 | Low | Open | Search has no maximum query length |
| D26 | Low | Fixed | Selenium tests report a pass when nothing ran |
| D27 | Low | Open | Nullable-reference warnings in four views |
| D28 | Low | Fixed | A real Data Protection key file is present in the project folder |
| D29 | Low | Open | Voter name length is not validated on registration (model rule 2..150) |
| D30 | High | Open | Stored XSS / attribute injection through the candidate name in the delete confirmation |
| D31 | Medium | Open | Custom URL slug is collected but never used, and its uniqueness is not enforced by the database |
| D32 | Low | Open | Verification fields are never set or read (Vote.IsVerified, Vote.VerifiedAt, Vote.BlockchainReference, Voter.IsVerified, Voter.VerifiedAt) |

## Details

### D01: Edit allows rescheduling/reopening a program after voting started or ended

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** High / P1
- **Location:** VotingService.ValidateAndUpdateProgramAsync (called from ProgramOwnerController.Edit POST, line ~119)
- **Preconditions:** Published program already started (or ended); logged in as owner
- **Steps to reproduce:** 1) Open Edit for a started program. 2) Move Start/End into the future (or extend End of an ended program). 3) Save.
- **Expected result:** Update rejected once voting has started; ended elections cannot be reopened.
- **Actual result:** Update succeeds. No lifecycle check exists in the service.
- **Evidence / test:** D01_Edit_StartedProgram_CannotBeRescheduledIntoTheFuture, D01_Edit_EndedProgram_CannotBeReopened
- **Suggested fix:** Reject changes to StartTime/EndTime/IsPublished when program HasStarted (allow only name/description).

### D02: Candidates can be added or deleted while the election is active or after it ended

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** High / P1
- **Location:** ProgramOwnerController.AddCandidate (line ~162), DeleteCandidate (line ~232)
- **Preconditions:** Published program that is active (or ended)
- **Steps to reproduce:** 1) Open ManageCandidates for an active program. 2) Add a candidate / delete one.
- **Expected result:** Rejected with an error: the ballot is frozen once voting starts.
- **Actual result:** Candidate is added/deleted. No state check in either action.
- **Evidence / test:** D02_AddCandidate_WhileProgramIsActive_IsRejected, D02_AddCandidate_AfterProgramEnded_IsRejected, D02_DeleteCandidate_WhileProgramIsActive_IsRejected
- **Suggested fix:** Add `if (program.HasStarted) { TempData["Error"]=...; redirect }` in both actions.

### D03: Deleting a candidate that has votes silently deletes those votes (FK cascade)

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** High / P1
- **Location:** ProgramOwnerController.DeleteCandidate; Migrations/20260418054953_Initialcreate.cs (FK_Votes_Candidates_CandidateId = Cascade)
- **Preconditions:** Candidate has at least one vote
- **Steps to reproduce:** 1) Delete a candidate who received votes.
- **Expected result:** Delete refused (or votes preserved); tallies and hash chain unchanged.
- **Actual result:** Candidate and its votes are removed; vote totals drop and the blockchain chain no longer validates.
- **Evidence / test:** D03_DeleteCandidate_WithRecordedVotes_IsRejected
- **Suggested fix:** Check for votes before delete; change FK to Restrict via migration.

### D04: Removing a voter who already voted deletes their vote (FK cascade)

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** High / P1
- **Location:** VotingService.RemoveVoterAsync; FK_Votes_Voters_VoterId = Cascade
- **Preconditions:** Voter has HasVoted = true
- **Steps to reproduce:** 1) Open ManageVoters. 2) Remove a voter who has voted.
- **Expected result:** Removal refused for voters who have voted.
- **Actual result:** Voter and the vote row are removed; chain breaks. No HasVoted check.
- **Evidence / test:** D04_RemoveVoter_VoterWhoAlreadyVoted_IsRejected
- **Suggested fix:** Return error when voter.HasVoted; change FK to Restrict.

### D05: A program with recorded votes can be hard-deleted as soon as it ends

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Medium / P2
- **Location:** ProgramOwnerController.Delete (line ~362); cascade FK on Votes/BlockchainVotes; VotingDbContext Owner OnDelete Cascade
- **Preconditions:** Ended published program with votes
- **Steps to reproduce:** 1) Wait for the end time. 2) Click Delete on MyPrograms.
- **Expected result:** Results and audit trail preserved (soft delete / archive).
- **Actual result:** Program, votes and blocks are permanently removed. Deleting an Identity user also cascades to their programs.
- **Evidence / test:** D05_Delete_EndedProgramWithVotes_IsNotHardDeleted
- **Suggested fix:** Soft-delete (IsDeleted flag) or block deletion when votes exist.

### D06: After a duration-validation failure the form re-renders with shifted times

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Low / P3
- **Location:** VotingService.ValidateProgramAsync (sets ValuesAreUtc = true before assigning StartTime/EndTime)
- **Preconditions:** Browser time zone is not UTC
- **Steps to reproduce:** 1) Create program with End < Start + 5 min. 2) Submit. 3) Look at Start/End on the re-rendered form.
- **Expected result:** Times shown exactly as typed.
- **Actual result:** ValuesAreUtc is true while the model still holds local values, so the JS converts them again and the times shift by the UTC offset; every retry shifts again. Reachable only when the page's client-side 5-minute check (a JavaScript alert) is bypassed, so the browser test cannot reach it; the unit test confirms it.
- **Evidence / test:** D06_DurationFailure_DoesNotLeaveLocalTimesFlaggedAsUtc; Selenium S03
- **Suggested fix:** Set ValuesAreUtc only after StartTime/EndTime have been assigned UTC values.

### D07: Candidate name of 1 character is accepted (model requires 2..150)

- **Status:** Fixed: AddCandidate now enforces name length 2..150; regression test passes.
- **Severity / priority:** Low / P3
- **Location:** ProgramOwnerController.AddCandidate (parameters bypass model validation)
- **Preconditions:** Any program
- **Steps to reproduce:** 1) POST name = 'A' with a valid code.
- **Expected result:** Rejected with 'Name must be between 2 and 150 characters'.
- **Actual result:** Before the fix a 1-character name was saved. Now rejected.
- **Evidence / test:** D07_AddCandidate_SingleCharacterName_IsRejected; Selenium S08
- **Suggested fix:** Bind to Candidate model with ModelState validation, or check lengths explicitly.

### D08: No server-side maximum-length checks for candidate fields

- **Status:** Partly fixed: candidate name is enforced; code (51), description (501) and image URL (501) are still accepted, so 3 of the 4 test cases still fail.
- **Severity / priority:** Low / P3
- **Location:** ProgramOwnerController.AddCandidate
- **Preconditions:** Crafted POST (browser maxlength can be bypassed)
- **Steps to reproduce:** 1) POST name 151 / code 51 / description 501 / imageUrl 501 characters.
- **Expected result:** Specific validation message; nothing saved.
- **Actual result:** Database throws DbUpdateException; user sees only 'Candidate could not be saved'.
- **Evidence / test:** D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage (4 cases)
- **Suggested fix:** Validate lengths before SaveChanges.

### D09: Protocol-relative image URL (//host/img.png) is accepted

- **Status:** Fixed: '//' and '/\' prefixes are rejected; unit regression test and Selenium test pass.
- **Severity / priority:** Low / P3
- **Location:** ProgramOwnerController.AddCandidate (`imageUrl.StartsWith('/')`)
- **Preconditions:** Any program
- **Steps to reproduce:** 1) Add candidate with imageUrl = //evil.example/photo.png.
- **Expected result:** Rejected as not a valid image URL.
- **Actual result:** Before the fix a protocol-relative URL was saved and browsers loaded the image from the external host. Now rejected.
- **Evidence / test:** D09_AddCandidate_ProtocolRelativeImageUrl_IsRejected; Selenium S07
- **Suggested fix:** Require StartsWith('/') && !StartsWith("//") && !StartsWith("/\\").

### D10: Candidate-code uniqueness is case-sensitive

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Low / P3
- **Location:** ProgramOwnerController.AddCandidate; unique index on (ProgramId, CandidateCode)
- **Preconditions:** Candidate with code 'A' exists
- **Steps to reproduce:** 1) Add another candidate with code 'a'.
- **Expected result:** Rejected as duplicate.
- **Actual result:** Saved: 'A' and 'a' coexist.
- **Evidence / test:** D10_AddCandidate_CodeThatDiffersOnlyByCase_IsRejectedAsDuplicate
- **Suggested fix:** Compare with ToLower()/ordinal-ignore-case and normalise on save.

### D11: Inconsistent NotFound/Forbid responses reveal which IDs exist

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Low / P3
- **Location:** ProgramOwnerController.DeleteCandidate, RemoveVoter
- **Preconditions:** Logged in as a different owner
- **Steps to reproduce:** 1) DeleteCandidate for a candidate of someone else's program. 2) DeleteCandidate for a non-existent id. 3) RemoveVoter for a missing program.
- **Expected result:** Same response for 'missing' and 'not yours'.
- **Actual result:** 403 vs 404 differ, so IDs can be enumerated; RemoveVoter returns 403 for a missing program while other actions return 404.
- **Evidence / test:** D11_DeleteCandidate_ExistingAndMissingCandidateOfOtherProgram_BehaveTheSame; D11_RemoveVoter_MissingProgram_ReturnsNotFoundLikeTheOtherActions
- **Suggested fix:** Return NotFound for both cases. (Note: existing test PO-056 asserts the current Forbid behaviour.)

### D12: MyPrograms and the dashboard load every vote, voter and candidate

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Medium / P2
- **Location:** ProgramOwnerController.MyPrograms (Include Candidates/Votes/Voters); VotingService.GetAllProgramsForDashboardAsync
- **Preconditions:** Owner with large programs
- **Steps to reproduce:** 1) Open MyPrograms for a program with thousands of votes.
- **Expected result:** Page loads counts only.
- **Actual result:** All rows are materialised; memory and latency grow with vote volume.
- **Evidence / test:** (performance) measure query size / response time
- **Suggested fix:** Project to counts with Select(p => new { ..., Votes = p.Votes.Count }).

### D13: Create does not trim Description (Edit does); invalid-slug message is unreachable

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Low / P3
- **Location:** VotingService.ValidateAndCreateProgramAsync; TryNormalizeSlug (always returns true)
- **Preconditions:** Any owner
- **Steps to reproduce:** 1) Create a program with Description '  hello  '. 2) Create with slug 'My Slug!'.
- **Expected result:** Description trimmed; invalid slug reported.
- **Actual result:** Description saved with spaces; slug is silently rewritten and the 'Slug can only contain…' error can never occur.
- **Evidence / test:** D13_Create_TrimsDescriptionLikeEditDoes
- **Suggested fix:** Trim in create; return false from TryNormalizeSlug when characters were removed.

### D14: Voter registration has no email-format check and ignores the election window

- **Status:** Confirmed: the defect test fails with an assertion failure (see defect-output.txt).
- **Severity / priority:** Medium / P2
- **Location:** VotingService.RegisterVoterByEmailAsync
- **Preconditions:** Owner of a program
- **Steps to reproduce:** 1) Register voter with email 'not-an-email'. 2) Register a voter after the program ended.
- **Expected result:** Both rejected.
- **Actual result:** Both accepted. Only null/whitespace is checked; name/email lengths rely on DB errors.
- **Evidence / test:** D14_RegisterVoter_InvalidEmailFormat_IsRejected, D14_RegisterVoter_AfterElectionEnded_IsRejected
- **Suggested fix:** Validate with MailAddress/EmailAddressAttribute, lengths, and program state.

### D15: Anyone can take over a pre-registered voter's slot by registering with that email

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Critical / P1
- **Location:** Areas/Identity/Pages/Account/Register.cshtml.cs (EmailConfirmed = true; LinkExistingVoterRegistrationsAsync); Program.cs (RequireConfirmedAccount = false)
- **Preconditions:** Owner registered victim@example.com as a voter; victim has no account yet
- **Steps to reproduce:** 1) Attacker registers an account with victim@example.com (no verification needed). 2) Log in. 3) Open the election and vote.
- **Expected result:** Voter slot is linked only after the email is verified.
- **Actual result:** Account is created confirmed and the voter row is linked automatically, so the attacker casts the victim's vote.
- **Evidence / test:** (security) Selenium/manual: register with an unverified email and vote
- **Suggested fix:** Require email confirmation before linking voters; link on confirmation.

### D16: Any signed-in user can self-register as a voter in any published program

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** High / P1
- **Location:** VotingController.Join (RegistrationSource = "self")
- **Preconditions:** Published program intended for a closed list
- **Steps to reproduce:** 1) Create an account. 2) Open the vote page. 3) Click register, then vote.
- **Expected result:** Closed programs accept only owner-registered voters.
- **Actual result:** Self-registration is always open, so the owner's voter list does not limit eligibility.
- **Evidence / test:** (add) VotingController.Join test — teammate scope
- **Suggested fix:** Add an 'open registration' setting per program, default closed.

### D17: Ballot secrecy is not preserved

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** High / P1
- **Location:** Models/Vote.cs (VoterId + CandidateId + IpAddress + UserAgent)
- **Preconditions:** Database or admin access
- **Steps to reproduce:** 1) Query Votes joined with Voters.
- **Expected result:** No link between a voter and their choice.
- **Actual result:** Each vote row stores who voted for whom, plus IP and user agent.
- **Evidence / test:** (review) data-model inspection
- **Suggested fix:** Separate the 'has voted' ledger from the anonymous ballot; drop IP/user agent.

### D18: Hash chain does not cover the vote's content

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** High / P1
- **Location:** BlockchainVote.GenerateHash / BlockchainService.IsChainValidForProgramAsync
- **Preconditions:** Program with blocks
- **Steps to reproduce:** 1) In the database change Votes.CandidateId for one vote. 2) Open Results.
- **Expected result:** Chain reports invalid.
- **Actual result:** Hash uses only VoteId + previous hash + timestamp, so the chain still reports valid.
- **Evidence / test:** (add) BlockchainService test: tamper CandidateId and verify
- **Suggested fix:** Include ProgramId, CandidateId, VoterId reference and VotedAt in the hash.

### D19: Chain validation cannot detect missing or truncated blocks

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Medium / P2
- **Location:** BlockchainService.IsChainValidForProgramAsync
- **Preconditions:** Votes exist without blocks, or last blocks deleted
- **Steps to reproduce:** 1) Delete the newest block rows. 2) Open Results.
- **Expected result:** Invalid (votes without blocks).
- **Actual result:** Returns true: only existing blocks are checked.
- **Evidence / test:** (add) BlockchainService test: votes count != blocks count
- **Suggested fix:** Compare vote count with block count and verify every vote has a block.

### D20: Block queue is in memory, drops writes when full, and is lost on restart

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Medium / P2
- **Location:** VoteBlockQueue (BoundedChannelFullMode.DropWrite), BlockchainBackgroundService
- **Preconditions:** Restart the app right after votes are cast
- **Steps to reproduce:** 1) Cast votes. 2) Restart before the worker runs.
- **Expected result:** Every vote eventually gets a block.
- **Actual result:** Queued vote ids are lost; the log promises a 'sweep' that does not exist.
- **Evidence / test:** (add) background service test with queue at capacity
- **Suggested fix:** On startup (and periodically) create blocks for votes without one.

### D21: Retry delays block the single queue reader

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Low / P3
- **Location:** BlockchainService retry policy (2 s, 4 s, 8 s) with a single-reader channel
- **Preconditions:** A vote whose block keeps failing
- **Steps to reproduce:** 1) Make block creation fail repeatedly.
- **Expected result:** Other votes are not delayed.
- **Actual result:** Each failing vote stalls the queue for about 14 seconds.
- **Evidence / test:** (add) background service test with failing service
- **Suggested fix:** Retry out of band or cap the delay.

### D22: Search orders by an unmapped property and may return HTTP 500

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** High / P1
- **Location:** VotingController.Search (`OrderByDescending(p => p.IsActive)`; IsActive is [NotMapped])
- **Preconditions:** Published programs exist
- **Steps to reproduce:** 1) Request /Voting/Search?q=a.
- **Expected result:** JSON list of up to 8 programs.
- **Actual result:** EF Core cannot translate IsActive to SQL and is expected to throw InvalidOperationException. CONFIRM by calling the endpoint.
- **Evidence / test:** (manual) GET /Voting/Search?q=a; Selenium search test
- **Suggested fix:** Order by a SQL expression: p.IsPublished && p.StartTime <= now && p.EndTime > now.

### D23: Live tallies are visible to voters while voting is still open

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Medium / P2
- **Location:** VotingController.PublicResults
- **Preconditions:** Published, active program
- **Steps to reproduce:** 1) Open /Voting/PublicResults/{id} during the vote.
- **Expected result:** Results hidden until the end (or per program setting).
- **Actual result:** Counts are shown immediately, which can influence later voters.
- **Evidence / test:** (add) VotingController test — teammate scope
- **Suggested fix:** Return results only after EndTime unless the owner enables live results.

### D24: Unpublished (draft) programs can be opened by id

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Low / P3
- **Location:** VotingController.Vote (no IsPublished check; PublicResults does check)
- **Preconditions:** Draft program
- **Steps to reproduce:** 1) Open /Voting/Vote/{id} for a draft.
- **Expected result:** 404 like PublicResults.
- **Actual result:** Closed page rendered with the program's data.
- **Evidence / test:** (add) VotingController test — teammate scope
- **Suggested fix:** Return NotFound when !IsPublished (except for the owner).

### D25: Search has no maximum query length

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Low / P3
- **Location:** VotingController.Search
- **Preconditions:** None
- **Steps to reproduce:** 1) Call Search with a very long q.
- **Expected result:** Rejected or truncated.
- **Actual result:** Passed to a LIKE query unchanged.
- **Evidence / test:** (add) Search test with 10 000-character q
- **Suggested fix:** Limit q to e.g. 100 characters.

### D26: Selenium tests report a pass when nothing ran

- **Status:** Fixed: legacy ProgramOwnerSeleniumTests.cs removed; the flow tests use [SeleniumFact], so they are skipped (not passed) without environment variables.
- **Severity / priority:** Low / P3
- **Location:** Tests/ProgramOwnerSeleniumTests.cs (LoginAsConfiguredOwner returns null -> test returns)
- **Preconditions:** AMARTOOLS_* environment variables not set
- **Steps to reproduce:** 1) Run dotnet test without the variables.
- **Expected result:** Tests reported as skipped.
- **Actual result:** Before the fix the legacy tests passed without running a browser when the variables were missing, and failed with a click-intercepted error when they were set.
- **Evidence / test:** SeleniumFact attribute in ProgramOwnerSeleniumFlowTests.cs
- **Suggested fix:** Use a skippable fact (provided).

### D27: Nullable-reference warnings in four views

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Low / P3
- **Location:** Vote.cshtml(9), PublicResults.cshtml(9), VotingAdmin/Results.cshtml(176), Shared/CreateProgram.cshtml(57)
- **Preconditions:** dotnet build
- **Steps to reproduce:** 1) Build the solution.
- **Expected result:** No warnings.
- **Actual result:** 4 x CS8602 (Dereference of a possibly null reference).
- **Evidence / test:** dotnet build output
- **Suggested fix:** Use Model! or declare non-nullable model.

### D28: A real Data Protection key file is present in the project folder

- **Status:** Fixed: .keys/ removed from the submitted project; it is also excluded by .gitignore and .dockerignore.
- **Severity / priority:** Low / P3
- **Location:** .keys/ (project root of the submitted archive)
- **Preconditions:** Archive or image built from the folder
- **Steps to reproduce:** 1) Inspect the tar / Docker build context.
- **Expected result:** No secrets in the archive or the image.
- **Actual result:** Key file present; .gitignore and .dockerignore cover it but the archive still contains it.
- **Evidence / test:** (process) archive review
- **Suggested fix:** Delete .keys before archiving; keep it out of submissions.

### D29: Voter name length is not validated on registration (model rule 2..150)

- **Status:** Open: from code review. A unit test (D29_RegisterVoter_NameOutsideAllowedLength_IsRejected) is ready to add.
- **Severity / priority:** Low / P3
- **Location:** VotingService.RegisterVoterByEmailAsync (called from ProgramOwnerController.RegisterVoter POST, line ~319)
- **Preconditions:** Owner of a program; crafted POST (browser maxlength can be bypassed)
- **Steps to reproduce:** 1) Register a voter with a 1-character name. 2) Register one with a 151-character name.
- **Expected result:** Both rejected with a validation message.
- **Actual result:** Both are accepted by the service (only null/whitespace is checked); the long name fails later at the database with a generic error.
- **Evidence / test:** D29_RegisterVoter_NameOutsideAllowedLength_IsRejected (2 cases)
- **Suggested fix:** Check Name length 2..150 (and email 200, memberId 100) before SaveChanges.

### D30: Stored XSS / attribute injection through the candidate name in the delete confirmation

- **Status:** Open: from code review. A Selenium test (AddCandidate_NameWithQuote_DoesNotInjectAttributesIntoDeleteForm) is ready to add.
- **Severity / priority:** High / P1
- **Location:** Views/VotingAdmin/ManageCandidates.cshtml line 259 (`onsubmit="return confirm('Delete @Html.Raw(c.Name.Replace(...))?')"`)
- **Preconditions:** A ProgramOwner adds a candidate; an Admin (or the owner) opens ManageCandidates
- **Steps to reproduce:** 1) Add a candidate named  Eve" data-injected="1  (or \');alert(1);// ). 2) Open ManageCandidates and inspect the delete form / submit it.
- **Expected result:** The name is encoded; the form has no extra attributes and no script runs.
- **Actual result:** Html.Raw writes the name unencoded inside an HTML attribute and a JavaScript string. A double quote closes the attribute; a backslash defeats the single-quote escaping. A ProgramOwner can therefore run script in an Admin's session.
- **Evidence / test:** Selenium S12 (AddCandidate_NameWithQuote_DoesNotInjectAttributesIntoDeleteForm); manual: use the name above
- **Suggested fix:** Remove Html.Raw; use @Json.Serialize(c.Name) inside the handler, or a data attribute plus JavaScript confirm.

### D31: Custom URL slug is collected but never used, and its uniqueness is not enforced by the database

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Medium / P2
- **Location:** CreateProgram.cshtml (Slug input); VotingService.ValidateProgramAsync; VotingDbContext / Initialcreate migration (no unique index on Slug)
- **Preconditions:** Program created with a slug
- **Steps to reproduce:** 1) Create a program with slug my-election. 2) Open /Voting/Vote/my-election or any slug URL. 3) Create two programs with the same slug at the same time.
- **Expected result:** The program opens through its slug, and duplicate slugs are impossible.
- **Actual result:** No controller or route reads Slug (published links use the numeric id), so the feature has no effect. Uniqueness is only an application-level check, so concurrent creates can store duplicates.
- **Evidence / test:** Code search: `.Slug` is read only in VotingService; manual: slug URL returns 404
- **Suggested fix:** Add routing/lookup by slug and a unique case-insensitive index, or remove the field.

### D32: Verification fields are never set or read (Vote.IsVerified, Vote.VerifiedAt, Vote.BlockchainReference, Voter.IsVerified, Voter.VerifiedAt)

- **Status:** Open: from code review; not yet reproduced by a test run.
- **Severity / priority:** Low / P3
- **Location:** Models/Vote.cs lines 50-55; Models/Voter.cs lines 60-61
- **Preconditions:** Any recorded vote
- **Steps to reproduce:** 1) Cast a vote. 2) Inspect the Votes and Voters rows.
- **Expected result:** Votes reference their blockchain block and verification state is meaningful.
- **Actual result:** The columns always keep their defaults (false / null), so a vote row cannot be tied to its block and nothing reports verification.
- **Evidence / test:** Code search: no assignment or read outside the model classes
- **Suggested fix:** Populate BlockchainReference when the block is created, or remove the unused columns.

## Notes

- Defects D01 to D11, D13 and D14 were found by reading the code and then checked with the defect tests in `ProgramOwnerBoundaryDefectTests.cs`. Defects D12, D15 to D25 and D27 to D32 come from code review of the services, `VotingController`, `BlockchainService`, Identity registration, a Razor view and the data model; they have not been reproduced by an automated test.
- Some of those involve code owned by other team members; confirm with them before reporting them as this controller's defects.
- D06 can only be reached by bypassing the page's client-side check, so the Selenium test checks the alert and the typed times, while the unit test confirms the server-side behaviour.
- `defect.trx` and `defect-output.txt` show the 3 passing unit tests (D07, D08 name, D09) as regression tests of fixed defects.

## Report format for any new defect

1. Defect ID
2. Affected controller/action and source location
3. Severity and priority
4. Preconditions and test data
5. Steps to reproduce
6. Expected result
7. Actual result
8. Evidence: failing test, log, screenshot, or request/response
9. Fix status and regression test