# Vision and scope

Osdy Brain is the brain of a small robotic pet, conceptually inspired by EMO and AIBI (LivingAI). Its defining feature: **its personality evolves gradually and explainably from what it experiences.** It is not a chatbot with eyes and not a thin wrapper around an LLM.

The goal is *convincing, coherent autonomous behavior*. We make no claim that the system is conscious or has real emotions; states such as "curious" are simulated control variables.

## Principles

1. **Brain first, body later.** Osdy Core runs as an independent process on macOS (development) and Linux (target, possibly Raspberry Pi CM5 — *assumption*, hardware not yet chosen).
2. **The core owns no devices.** Perception arrives as events; expression leaves as actions. Cameras, microphones, motors and screens are adapters.
3. **Deterministic and explainable.** Same inputs and same starting snapshot produce the same decisions. Every significant decision records its rule and reasons; every trait change records the experience that caused it.
4. **Offline by default.** AI providers (later) extend cognition and conversation; the brain keeps working without them.
5. **Small, observable increments.** Each phase ships something runnable and tested.

## Long-term capabilities

| Capability | First phase |
|------------|-------------|
| Persistent identity and traits | v0.1 |
| Internal states with valid transitions | v0.1 |
| Autonomous loop, reactive and spontaneous behavior | v0.1 |
| Episodic memory of experiences (SQLite) | v0.1 |
| Gradual, bounded personality learning | v0.1 |
| Memory query, correction, export, deletion | v0.2 |
| Full trait set (shyness, behavioral energy, playfulness, initiative) | v0.2 |
| Simulated affective states with intensity and decay | v0.3 |
| Preferences and habits (behavioral memory) | v0.3 |
| Provider-agnostic LLM integration, semantic memory | v0.4 |
| Real perception and expression adapters | v0.5 |
| Embedded deployment and physical body | v0.6+ |

## v0.1 scope (Osdy Core v0.1)

**In scope**

1. Create and load a persistent identity.
2. Three traits: curiosity, sociability, confidence.
3. Five states: Idle, Curious, Attentive, Interacting, Resting.
4. Autonomous execution loop.
5. Simulated events.
6. Rule-based decisions.
7. Experiences persisted in SQLite.
8. Gradual trait changes under defined rules.
9. Decisions recorded with their causes.
10. Memory, personality and state recovered after restart.
11. Console application.
12. Automated tests.

**Out of scope:** LLM APIs, real face recognition, voice, robotic hardware, advanced screens/animations, training own models, distributed architectures.

## Success criteria (v0.1)

- Runs offline, without API keys, without hardware.
- Decisions are verifiable and explainable.
- Experiences survive restarts; the brain resumes from a valid state.
- Personality changes are gradual and bounded, proven by simulating hundreds of experiences.
- Events can be replayed for debugging.
- Tests use no randomness and no real waiting.

## Ethical guardrails

- The robot must never be optimized to maximize human attention. There is no "engagement" reward signal; spontaneous behavior is rate-limited.
- Observed facts are stored separately from inferred interpretations.
- Memories belong to the owner: they can be inspected, exported and deleted (v0.2).
