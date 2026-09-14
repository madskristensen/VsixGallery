# VSIX Gallery Audit Implementation Plan

This plan tracks implementation of the September 2026 website audit. Each work item maps to one focused Git commit. The checklist will be updated as work progresses.

## Scope

- [ ] 1. Establish automated test coverage for critical existing behavior.
- [ ] 2. Harden upload processing: filesystem containment, strict extension IDs, bounded archive extraction, cancellation, safe error responses, URL validation, XML safety, and SVG escaping.
- [ ] 3. Improve package-storage reliability with atomic replacement and safer lifecycle cleanup.
- [ ] 4. Correct HTTP and application behavior: conditional requests, feed timestamps, pagination, unlisted API output, author feeds, optional links, sitemap generation, canonical URLs, and error responses.
- [ ] 5. Secure and improve README rendering with server-side retrieval, sanitization, and caching.
- [ ] 6. Improve accessibility, SEO, performance, content quality, and broken-link handling.
- [ ] 7. Add operational safeguards: health checks, storage diagnostics, and useful telemetry/logging.
- [ ] 8. Harden dependencies and CI: reproducible restore, PR validation, dependency automation, deployment serialization, smoke checks, and safer action references.
- [ ] 9. Run final build/tests and verify the commit history and clean working tree.

## Explicit exclusions

- Publisher ownership or identity.
- Changes to upload authentication.
- Changes to admin authentication.
- Changes to per-extension manage-token authentication.

Security fixes that do not alter those authentication or ownership models remain in scope.

## Progress

| Work item | Status | Commit |
| --- | --- | --- |
| Implementation plan | Complete | `docs: add audit implementation plan` |
| Test foundation | Pending | Pending |
| Upload safety | Pending | Pending |
| Storage reliability | Pending | Pending |
| HTTP correctness | Pending | Pending |
| README safety | Pending | Pending |
| Web quality | Pending | Pending |
| Operations | Pending | Pending |
| Dependencies and CI | Pending | Pending |
| Final validation | Pending | Pending |

## Validation

Each work item will receive the smallest relevant build/test pass before its commit. The final work item will run the complete solution build and test suite.
