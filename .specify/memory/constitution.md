<!--
  Sync Impact Report
  ==================
  Version change: (none) → 1.0.0
  Modified principles: N/A (initial creation)
  Added sections:
    - Core Principles (I–V)
    - Technical Standards
    - Development Workflow
    - Governance
  Removed sections: N/A
  Templates requiring updates: None (all templates are generic; no hardcoded principle references)
  Follow-up TODOs: None
-->

# Adam Constitution

## Core Principles

### I. Clarity-First Specifications

All work MUST start with clear, independently testable user stories
organized by priority (P1, P2, P3...). Each specification MUST define
measurable acceptance criteria and edge cases before implementation
begins. Ambiguity MUST be resolved during specification — never deferred
to planning or implementation.

### II. Incremental Delivery

Features MUST be delivered in independent, value-producing increments.
Each user story MUST be independently testable and deployable. The
highest-priority story (MVP) MUST be completed before lower-priority
stories are started. No story expands scope until the prior story
delivers value.

### III. Test Verification (NON-NEGOTIABLE)

Tests MUST be defined and reviewed before implementation code is written.
The Red-Green-Refactor cycle MUST be followed: write a failing test,
confirm it fails, implement the minimum code to pass, then refactor.
No feature is complete without verified tests passing.

### IV. Self-Documenting Development

Architecture decisions, data models, API contracts, and operational
runbooks MUST be documented alongside code. Documentation is a
first-class deliverable, not an afterthought. Every significant decision
MUST include rationale. Documentation lives in the same repository as
code.

### V. Simplicity & YAGNI

Start with the simplest solution that meets the specified requirements.
MUST NOT add functionality until it is demonstrably needed by a defined
user story. Complexity MUST be justified in the plan when it violates
this principle.

## Technical Standards

Technology choices (language, framework, storage, platform) MUST be
explicitly stated in the plan before implementation begins.

Code MUST follow language-idiomatic conventions and project-wide style
guides. Linting and formatting tools MUST be configured before the first
implementation task.

Error handling MUST be explicit and context-rich. Structured logging
MUST be used for runtime observability.

Testing frameworks and patterns MUST be selected during planning and
documented in the plan.

## Development Workflow

All feature work follows the Specify → Plan → Tasks → Implement workflow.
Each phase MUST complete before the next begins. Review gates exist
between Specify→Plan and Plan→Tasks.

Constitution compliance MUST be verified during the planning phase
(Constitution Check gate). Complexity violations against Principle V
require documented justification in the plan.

Use `AGENTS.md` for runtime development guidance and tool-specific
conventions.

## Governance

This constitution supersedes all other development practices.

Amendments require:
- Documentation of the proposed change and its rationale
- Approval from the project maintainer
- A migration plan for any in-flight work affected by the change

Versioning follows MAJOR.MINOR.PATCH:
- MAJOR: Backward incompatible governance/principle removals or redefinitions
- MINOR: New principle/section added or materially expanded guidance
- PATCH: Clarifications, wording, typo fixes, non-semantic refinements

All PRs and reviews MUST verify compliance with this constitution.
Any deviation MUST be documented and justified.

**Version**: 1.0.0 | **Ratified**: 2026-05-11 | **Last Amended**: 2026-05-11
