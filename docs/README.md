# Osdy Brain — documentation

Osdy Brain is the autonomous, evolving brain of a small robotic pet. **Osdy Core** is its runtime nucleus. Version v0.1 runs on a regular computer, with simulated events and no hardware, LLM or Internet.

## Reading order

| # | Document | Answers |
|---|----------|---------|
| 1 | [Vision and scope](vision.md) | What are we building, and what are we NOT building? |
| 2 | [Architecture](architecture.md) | Components, responsibilities, ports and interfaces |
| 3 | [Domain model](domain-model.md) | Entities, value objects and their invariants |
| 4 | [Events and execution loop](execution-loop.md) | How inputs become decisions, states and actions |
| 5 | [Memory and persistence](memory-and-persistence.md) | What is stored, where, and how it survives restarts |
| 6 | [Personality model](personality-model.md) | The math behind gradual, bounded trait evolution |
| 7 | [Testing and simulation](testing-strategy.md) | How we prove determinism, stability and evolution |
| 8 | [Roadmap](roadmap.md) | Phases from v0.1 to hardware, with Definition of Done |
| 9 | [Backlog](backlog.md) | Prioritized user stories and acceptance criteria |
| 10 | [Decisions and risks](decisions.md) | ADRs, rejected alternatives, risks, assumptions, open questions |

## Conventions

- **Verified** means checked against code, tests or an official source. Everything else is labelled *assumption* or *pending decision*.
- Technical artifacts (code, docs, identifiers) are in English. Console output follows the user's Spanish example (see ADR-008).
