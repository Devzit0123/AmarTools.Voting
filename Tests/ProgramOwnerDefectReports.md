# ProgramOwnerController defect report

## Execution evidence

- Scope: `ProgramOwnerController.cs`
- Test project: `Tests/AmarTools.Voting.Tests.csproj`
- Defect tests: `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category=Defect"`
- Result: **21 failed, 3 skipped (Selenium), 0 exceptions**
- Non-defect tests: `dotnet test Tests/AmarTools.Voting.Tests.csproj --filter "Category!=Defect"`
- Result: **98 passed, 8 skipped (Selenium), 0 failed**
- Evidence: `defect-output.txt`, `nondefect-output.txt`, `Tests/TestResults/defect.trx`, `Tests/TestResults/nondefect.trx`

## Confirmed defects (13)

All failures are assertion-based (Assert.True/False/Single/Empty/Equal), confirming reproducible application behavior.

### D01: Edit can change times and publish state after voting starts or ends (Confirmed)
- **Status**: Confirmed
- **Severity**: High
- **Location**: `ValidateAndUpdateProgramAsync`, no lifecycle check
- **Evidence**: D01_Edit_EndedProgram_CannotBeReopened, D01_Edit_StartedProgram_CannotBeRescheduledIntoTheFuture (Assert.True() Failure)
- **Impact**: Ended elections can be reopened by editing times/publish state

### D02: AddCandidate and DeleteCandidate work during active or ended elections (Confirmed)
- **Status**: Confirmed
- **Severity**: High
- **Location**: `AddCandidate` and `DeleteCandidate`, lines 162, 232 (no election state checks)
- **Evidence**: D02_AddCandidate_WhileProgramIsActive_IsRejected, D02_AddCandidate_AfterProgramEnded_IsRejected, D02_DeleteCandidate_WhileProgramIsActive_IsRejected (Assert.True() Failure)
- **Impact**: Candidate lists can be modified during voting, invalidating the ballot

### D03: Deleting a candidate cascade-deletes its votes (Confirmed)
- **Status**: Confirmed
- **Severity**: High
- **Location**: `FK_Votes_Candidates` constraint set to Cascade
- **Evidence**: D03_DeleteCandidate_WithRecordedVotes_IsRejected (Assert.Single() Failure: collection was empty)
- **Impact**: Vote counts can be altered by deleting candidates after voting

### D04: Removing a voter who already voted cascade-deletes their vote (Confirmed)
- **Status**: Confirmed
- **Severity**: High
- **Location**: `FK_Votes_Voters` constraint set to Cascade; `RemoveVoter` has no HasVoted check
- **Evidence**: D04_RemoveVoter_VoterWhoAlreadyVoted_IsRejected (assertion failure in remove operation)
- **Impact**: Vote counts can be altered by removing voters after voting

### D05: Program delete cascade-deletes all votes without lifecycle protection (Confirmed)
- **Status**: Confirmed
- **Severity**: Medium
- **Location**: `Delete` action, cascade foreign keys, deleting user cascades to their programs
- **Evidence**: D05_Delete_EndedProgramWithVotes_IsNotHardDeleted (Assert.True() Failure)
- **Impact**: Audit trail lost; program deletion right after end destroys vote records

### D06: When duration check fails, ValuesAreUtc flag is set but times remain local (Confirmed)
- **Status**: Confirmed
- **Severity**: Medium
- **Location**: `ValidateProgramAsync`, line 556, re-render after validation failure
- **Evidence**: D06_DurationFailure_DoesNotLeaveLocalTimesFlaggedAsUtc (custom assertion message)
- **Impact**: Form redisplay shifts times incorrectly for the user

### D07: AddCandidate accepts 1-character name (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `AddCandidate` model validation; model requires ≥2 chars
- **Evidence**: D07_AddCandidate_SingleCharacterName_IsRejected (Assert.True() Failure)
- **Impact**: Data inconsistency between client and server validation

### D08: No length checks for name, code, description, imageUrl (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `AddCandidate` controller; no length enforced before DB insert (150, 50, 500, 500)
- **Evidence**: D08_AddCandidate_FieldOverMaxLength_IsRejectedWithValidationMessage (Assert.True() Failure ×4 for name, code, description, imageUrl)
- **Impact**: Database constraint violations show generic error messages to users

### D09: Image URL like //host/x.png is accepted (protocol-relative URL) (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `AddCandidate` URL validation
- **Evidence**: D09_AddCandidate_ProtocolRelativeImageUrl_IsRejected (Assert.True() Failure)
- **Impact**: Potential XSS via protocol-relative or data: URI injection

### D10: Candidate code uniqueness is case-sensitive (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `AddCandidate` duplicate check; "A" and "a" both allowed
- **Evidence**: D10_AddCandidate_CodeThatDiffersOnlyByCase_IsRejectedAsDuplicate (Assert.True() Failure)
- **Impact**: Case-variant candidate codes create duplicate-like confusion

### D11: DeleteCandidate returns NotFoundResult before ownership check; authorization inconsistency (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `DeleteCandidate` ID enumeration risk; `RemoveVoter` returns Forbid for missing program, others return NotFound
- **Evidence**: D11_DeleteCandidate_ExistingAndMissingCandidateOfOtherProgram_BehaveTheSame (Assert.Equal() Failure: Expected ForbidResult, Actual NotFoundResult)
- **Impact**: Inconsistent error responses; ID enumeration vulnerability

### D13: Create doesn't trim Description but Edit does (Confirmed)
- **Status**: Confirmed
- **Severity**: Low
- **Location**: `Create` action doesn't trim description; `Edit` does (line suggests TryNormalizeSlug always returns true)
- **Evidence**: D13_Create_TrimsDescriptionLikeEditDoes (Assert.True() Failure)
- **Impact**: Whitespace inconsistency between create and update paths

### D14: No email format check, no length limits, voters registered after election ends (Confirmed)
- **Status**: Confirmed
- **Severity**: Medium
- **Location**: `RegisterVoter` POST, `ValidateRegisterVoter`; email validation absent
- **Evidence**: D14_RegisterVoter_InvalidEmailFormat_IsRejected, D14_RegisterVoter_AfterElectionEnded_IsRejected (Assert.False() Failure)
- **Impact**: Invalid emails accepted; voters can be registered after voting ends

## Skipped Selenium defects

The following defect scenarios depend on browser automation and are skipped until environment variables are configured:

- S03: AddCandidate_JavascriptImageUrl_IsRejected (related to D09)
- S07: AddCandidate_ProtocolRelativeImageUrl_IsRejected (related to D09)
- S08: AddCandidate_SingleCharacterName_IsRejected (related to D07)

## Test result summary

- Unit defect tests: 21 confirmed failures (D01–D11, D13, D14)
- Unit non-defect tests: 98 passed
- Selenium flow tests: 11 skipped (requires `AMARTOOLS_BASE_URL`, `AMARTOOLS_TEST_EMAIL`, `AMARTOOLS_TEST_PASSWORD`)
- **Total**: 130 tests (98 passing, 21 confirmed failures, 11 skipped)

## Report format for any future confirmed defect

1. Defect ID
2. Affected controller/action and source line
3. Severity and priority
4. Preconditions and test data
5. Reproduction steps
6. Expected result
7. Actual result
8. Evidence: failing test, log, screenshot, or request/response
9. Fix status and regression test
