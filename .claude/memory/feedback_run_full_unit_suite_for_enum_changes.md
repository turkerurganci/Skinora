---
name: feedback_run_full_unit_suite_for_enum_changes
description: Adding an enum value breaks exact-count parity tests in sibling test projects — run the full Unit-filter suite before pushing, not just the touched project
type: feedback
---

In the Skinora backend, adding a value to any `Skinora.Shared.Enums` enum breaks **exact-count + completeness parity tests that live in different test projects than the code**, so running only the touched project's tests passes locally but CI's Unit job fails.

Concrete guards (WP7 hit both with a new `AuditAction` value):
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — `<Enum>_ShouldHaveNValues` (exact `Assert.Equal(N, ...)`) + a `<Enum>_ShouldContainExpectedValue` `[Theory]` with one `[InlineData]` per value. Update the count AND add the InlineData.
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` — `Every_AuditAction_Has_A_Category` iterates `Enum.GetValues<AuditAction>()` and `AuditLogCategoryMap.CategoryFor` **throws** for an unmapped value (also a latent prod bug in the `GET /admin/audit-logs` path). Add the new action to `AuditLogCategoryMap` (`backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs`) AND bump the `ActionsInCategory_<CAT>_Returns_N` count test.

**Why:** these are intentional "no silent enum drift" guards; the count assertions are exact, not `>=`.

**How to apply:** before pushing any enum change, run CI's exact Unit filter locally, not just the touched project —
`dotnet test Skinora.sln -c Release --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"`.
CI's Unit job uses that filter; the API integration suite (`Skinora.API.Tests`) is in the Integration job and will NOT surface these. Related: [[feedback_claude_watches_ci_always]].
