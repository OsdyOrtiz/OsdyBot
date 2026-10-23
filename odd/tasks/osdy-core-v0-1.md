# Osdy Core v0.1 — feature document

Objective: plan Osdy Brain (Stage A) and implement the Osdy Core v0.1 MVP (Stage B). Branch: `feat/osdy-core-v0-1`.
Delivery strategy: `exception-ok` (greenfield bootstrap, no PR requested yet; each task is one work-unit commit that can be promoted to a chained PR later). Test runner: `dotnet test` (xUnit, .NET 10 LTS).

## Specs

Naming (L2): the project is "Osdy Brain" (not "Robot Brain"); the core subsystem is "Osdy Core" (not "Brain Core").

- S1 — Stage A planning documents (section 9, ETAPA A, items 1–12): "Documento de visión y alcance del producto", "Arquitectura propuesta, con diagrama de componentes", "Responsabilidades e interfaces principales", "Diseño inicial de entidades y modelos de dominio", "Modelo de eventos y ciclo de ejecución", "Estrategia de memoria y persistencia", "Modelo matemático inicial de evolución de personalidad", "Estrategia de pruebas y simulación", "Roadmap por fases desde v0.1 hasta integración con hardware", "Backlog priorizado con historias de usuario y criterios de aceptación", "Riesgos técnicos, decisiones y alternativas descartadas", "Definición de terminado para cada fase". "Señalá claramente los supuestos y las decisiones pendientes. No presentés hipótesis como hechos técnicos verificados."
- S2 — "Crear y cargar una identidad persistente."
- S3 — "Inicializar tres rasgos de personalidad: curiosidad, sociabilidad y confianza."
- S4 — "Implementar cinco estados: Idle, Curious, Attentive, Interacting y Resting." / "Los estados tienen transiciones válidas."
- S5 — "Crear un ciclo de ejecución autónomo."
- S6 — "Procesar eventos simulados." / "Los eventos se pueden reproducir para depuración."
- S7 — "Implementar decisiones básicas mediante reglas." / "Las decisiones producen resultados verificables."
- S8 — "Persistir experiencias en SQLite." / "Las experiencias sobreviven al reinicio."
- S9 — "Modificar rasgos gradualmente bajo reglas definidas." / "Los cambios de personalidad son graduales y acotados." / "Se pueden simular cientos de experiencias y comprobar que los rasgos permanecen dentro de sus límites." / "diseñá las fórmulas con parámetros configurables y pruebas que verifiquen su estabilidad."
- S10 — "Registrar las decisiones y sus causas." / "El sistema puede explicar cada decisión significativa."
- S11 — "Recuperar memoria, personalidad y estado después de reiniciar." / "Al reiniciar, el cerebro deberá recuperar los datos guardados y continuar desde un estado válido."
- S12 — "Ejecutar el cerebro mediante una aplicación de consola." Output shaped like the example (`[BRAIN] Iniciando...`, `[STATE] Idle`, `[TRAIT] Curiosity: 0.70`, `[EVENT] HumanInteractionDetected`, `[DECISION] ObserveAndEngage`, `[REASON] ...`, `[EXPERIENCE] Saved`, `[LEARNING] Confidence +0.002`).
- S13 — "Incorporar pruebas automatizadas." / "Los tests no dependen de tiempos aleatorios ni esperas reales."
- S14 — Constraints: "La aplicación ejecuta sin conexión a Internet." / "No necesita claves de API." / "El núcleo no tiene dependencias de hardware." Out of scope: LLM APIs, real face recognition, voice, robotic hardware, advanced screens/animations, model training, distributed architectures.
- S15 — "Documentá cómo ejecutar el proyecto." / "Registrá las decisiones de arquitectura relevantes."

Assumption A1: user-facing console tags stay as in the example; technical artifacts are in English.

## Tasks

| ID | Specs | Route | Status | Commit |
|----|-------|-------|--------|--------|
| T1 | S1, S15 | inline | done — structural readback (passive docs, no runnable RED) | 0979496 |
| T2 | S3, S9, S13 | inline | done — RED: compile failure before implementation; GREEN 18/18 | `feat(personality)` 22fd5fb |
| T3 | S4, S6, S7, S9, S10, S13 | inline | done — RED not observed (code written before tests); compensated with 2 mutation checks (budget removed → 3 fail; interaction tracking removed → 1 fail); GREEN 75/75 | 1f3040f |
| T4 | S2, S8, S11 | inline | done — RED: CS0246 `SqliteBrainStore` missing; first GREEN attempt failed (`no such column: seq`), fixed; GREEN 82/82 | 04faba4 |
| T5 | S5, S6, S12, S14, S15 | inline | done — RED: CLI tests written after code (not observed); found + fixed 2 defects during functional run (Investigate did not re-enter Curious; shutdown dropped queued events); GREEN 106/106; runner tests 15× and CLI tests 10× stable; real binary run verified | 91386ec |
| T6 | all | delegated verifier (feature end) | done — 106/106; S1–S8, S10–S15 PASS, S9 PARTIAL (1 blocker: budget window documented as rolling but fixed → docs corrected to the real guarantee ≤ 2× budget at a boundary, worst-case test added, P8 opened); advisories 1–2 fixed with RED→GREEN tests; advisories 3–9 deferred; GREEN 108/108 | `fix:` correction commit |

Route evidence: no Writer trigger (single sequential stream, no parallel disjoint units); parent writes inline. T6 is an independent verifier because the runner adds async ordering (Risk item 4).

## Log

- L1 (2026-10-23, user, verbatim): see the pasted "PROYECTO: ROBOT BRAIN" brief (sections 1–10), ending with "Comenzá presentándome el diseño de arquitectura de Brain Core v0.1, sus decisiones fundamentales, el roadmap y el primer backlog de implementación. Después avanzá con la construcción inicial, siempre respetando el alcance definido."
- L2 (2026-10-23, user, verbatim): "solo que no le pongas Robot Brain, quiero que se llame Osdy Brain, y Core Brain cambiarlo por Osdy Core"
- L3: RDD is off (global) → no native review; ordinary checks only. Forecast > 400 authored lines; strategy `exception-ok` recorded above.
- L4 (2026-10-23): independent verifier report — blocker: fixed budget window vs "rolling" claim (burst at boundary moved Confidence 0.058 in ~3.5 min). Decision: v0.1 keeps the fixed window and documents the exact bound (≤ 2 × BudgetPerWindow in any short span); sliding window deferred as P8. Fixed advisories: schema guard modified refused files (WAL before version check); CLI crashed on non-SQLite files (DbException not caught).
- L5: Deferred advisories for v0.2 backlog: status/export should open the DB read-only (an empty file gets a schema); `run` should stop reading input when the loop stops after repeated failures; timestamps can go backwards (event stamped at post vs tick at processing; simulate --db then run); `events export` drops trailing waits and sub-second offsets; Reload drops a custom DecisionEngine; boot after recovery prints `[STATE] Idle` twice.
- L6: Next step — user review of the planning docs and v0.1; push/PR is the user's decision.
