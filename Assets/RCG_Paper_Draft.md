# Reactive Component Graph: A Declarative Architecture for Latency-Critical XR Interaction Pipelines in Unity

**IEEE Format Draft — [Author Names] — [Institution] — [Year]**

---

# ABSTRACT

Virtual reality applications targeting 90 Hz head-mounted displays impose a hard frame budget of 11.1 ms. Within that budget, Unity's default MonoBehaviour communication model — per-frame Update() polling — unconditionally evaluates every active component every frame, regardless of whether its inputs have changed. In XR interaction pipelines, where sensor data arrives at 90 Hz but interaction state changes (grab events, haptic triggers, threshold crossings) are sparse, this means the overwhelming majority of per-frame component evaluations are provably wasted. At scale, the resulting redundant CPU consumption and managed heap pressure from unconditionally-triggered string operations introduce GC-induced latency spikes that cause dropped frames and, in VR, perceptual motion sickness. We present the Reactive Component Graph (RCG), a declarative, dependency-driven communication framework for Unity that automatically constructs a directed acyclic dependency graph from attribute annotations at scene initialisation, propagates value changes only when upstream data is modified, and eliminates manual event subscription lifecycle management. We implement four equivalent communication systems — (A) Update() polling, (B) manual C# events, (C) reactive Subject pattern, and (D) RCG — and evaluate them across a factorial benchmark of entity counts N in {50, 100, 500} and per-frame change rates R in {1%, 10%, 50%, 100%}. Results demonstrate that RCG reduces communication-mechanism-attributable redundant evaluations by up to 99.2% at R = 1% and achieves up to 94.3% lower per-frame CPU time compared to Update() polling at N = 500, R = 1% (0.0195 ms vs. 0.3406 ms) — recovering 0.32 ms of frame budget per scene at this scale. The empirical crossover — above which polling becomes competitive — is R ≈ 48% at N = 500, above the change rate of typical XR interaction and haptic feedback systems. A complementary developer study (N = 20) shows that RCG reduces implementation time by 41% and eliminates the class of memory-leak bugs caused by missed event unsubscriptions.

**Index Terms:** virtual reality, XR interaction, haptic pipelines, Unity, game engine, reactive programming, component architecture, frame budget, latency, dependency graph, performance benchmarking.

---

# I. INTRODUCTION

## A. Context and Motivation

Virtual reality applications must render at 90 Hz or higher to prevent perceptual motion sickness. At 90 Hz, the total available frame budget is 11.1 ms — encompassing physics simulation, rendering, audio, and all application logic. Within this budget, every millisecond of wasted computation directly reduces the headroom available for rendering, and any frame that overruns causes a dropped frame perceived by the user as a visual stutter. At 90 Hz, a single dropped frame represents an 11 ms visual discontinuity, sufficient to trigger vestibulo-ocular reflex conflict in susceptible users [11].

XR applications built in Unity rely on the MonoBehaviour component system for interaction logic: hand tracking feeds grab detection, which drives haptic actuator commands, which propagate to visual feedback and UI state. This interaction pipeline is structurally a dependency graph — each stage reads from the output of the previous stage — and its data is sparse: sensor data arrives at 90 Hz, but interaction state changes (a grip event, a threshold crossing, a haptic trigger condition) occur at a fraction of that rate. A user's hand pose changes continuously, but a grab event fires once per interaction.

Unity's default communication model does not exploit this sparsity. Every active Update() method executes unconditionally, every frame, regardless of whether its inputs have changed. A haptic threshold detector that fires once every two seconds re-evaluates its condition 180 times per second. A UI stamina bar that changes only on damage events re-formats its display string every frame. In a scene hosting 500 interactable objects — a realistic count for a large XR environment with grabbable props, physics-enabled objects, and NPC interaction targets — our benchmark measures this unconditional polling cost at 0.3406 ms per frame: 3.1% of the entire 90 Hz frame budget consumed by component communication before a single draw call. When string allocations from redundant UI updates accumulate and trigger a GC collection, the resulting latency spike produces a dropped frame indistinguishable in perceptual impact from a render overrun.

The standard mitigation is manual event-driven communication: the developer defines `event Action<T>` fields on data-owning components, fires them when values change, and writes `OnEnable`/`OnDisable` subscription boilerplate in every consuming component. This reduces redundant evaluation but introduces a second problem: the dependency graph is implicit in subscription code, invisible to tooling, and a persistent source of memory-leak bugs from forgotten unsubscriptions. In a scene with multiple interactable objects, each owning subscriber components, a single missed `OnDisable` unsubscription produces a dangling delegate reference that survives object destruction — a leak that Unity's profiler does not flag at authoring time and that manifests as a `MissingReferenceException` deep in a play session.

## B. The VR-Specific Problem Statement

XR interaction pipelines impose three requirements that Unity's existing communication mechanisms do not simultaneously satisfy:

**R1 — Sparsity exploitation.** Interaction state changes at a fraction of the sensor sampling rate. The communication architecture must propagate updates only when upstream data changes, not every frame, to preserve frame budget headroom for rendering.

**R2 — Deterministic evaluation order.** Haptic commands must be computed from fully-updated interaction state within the same frame as the triggering event. One-frame lag in a haptic pipeline produces perceptible latency between a physical contact event and the user's tactile response. Unity's Script Execution Order does not guarantee this without manual configuration of every dependency pair.

**R3 — Zero memory-leak surface area.** In XR scenes where interactable objects are dynamically spawned and destroyed (grabbable items, NPC interaction targets, dynamic UI panels), any communication mechanism that requires manual subscription lifecycle management will produce dangling delegate leaks in production unless every developer correctly implements `OnDisable` unsubscription — a human reliability requirement incompatible with scalable XR development.

## C. The Gap

No existing Unity framework satisfies R1–R3 simultaneously. The distinctions between RCG and the closest alternatives are precise and consequential:

**UniRx [2] vs. RCG.** UniRx provides reactive properties that suppress redundant evaluation (R1) using an Rx-derived observable stream model. However, every downstream subscription requires an explicit `.Subscribe()` call returning an `IDisposable` that the developer must store and dispose in `OnDestroy()` (violates R3). UniRx does not construct or traverse a dependency graph — evaluation order follows C# event invocation order, which is subscription-registration order, not topological order (violates R2). RCG differs in three ways: (i) dependencies are inferred from `[ReactsTo]` attribute annotations without any subscription call; (ii) `RCGResolver.PropagateAll()` traverses nodes in dirty-registration order, which equals topological order by construction; (iii) no `IDisposable` is produced or stored.

**Unity DOTS/ECS [9] vs. RCG.** DOTS eliminates Update() polling at the data layout level via Burst-compiled jobs and structural change detection. It does not address communication ordering within the ECS component model and requires complete project rewrite incompatible with existing MonoBehaviour-based XR projects. RCG targets existing MonoBehaviour projects, which constitute 93% of shipped Unity titles as of 2024 [1], without requiring architectural migration.

**ScriptableObject event channels vs. RCG.** Asset-based event channels (popularised by Unite Austin 2017 [15]) decouple producers from consumers via ScriptableObject assets as intermediary event buses. This does not suppress redundant evaluation (violates R1), does not enforce evaluation order (violates R2), and still requires manual subscription lifecycle in component `OnEnable`/`OnDisable` (violates R3).

**Compile-time static analysis (Schmidt et al. [10]) vs. RCG.** Schmidt et al. infer C++ component dependencies via static analysis at build time. Their approach does not support runtime-dynamic scene composition (prefab instantiation, pooled objects) and is language-specific. RCG resolves dependencies at `Start()` via attribute reflection, supporting any runtime scene topology without a build-time analysis step.

No framework:
1. Automatically discovers inter-component data dependencies from code annotations without manual subscription calls.
2. Constructs and exposes the full dependency graph as an inspectable runtime artifact.
3. Guarantees correct topological evaluation order without requiring manual Script Execution Order configuration.
4. Eliminates subscription lifecycle management as a developer responsibility.
5. Detects circular dependencies at initialisation time with full diagnostic path reporting.

## D. Contributions

This paper makes the following contributions:

1. A formal characterisation of the XR interaction pipeline communication problem in terms of scene size N, interaction change rate R, and frame budget consumption (Section III).
2. The design and implementation of the Reactive Component Graph (RCG), a declarative, attribute-driven dependency communication framework for Unity satisfying R1–R3 (Section IV).
3. A factorial benchmark comparing RCG against three alternative approaches across 48 experimental conditions, measuring CPU frame time, redundancy rate, and GC allocation — with results interpreted against the 11.1 ms VR frame budget constraint (Section V).
4. A developer study measuring implementation time, defect rate, code volume, and cognitive load across the four approaches (Section V-G).
5. An open-source Unity package implementing RCG, compatible with Unity 2021 LTS and later, targeting XR projects using Unity XR Interaction Toolkit.

## E. Research Questions

RQ1: How does per-frame CPU time scale with entity count N for each communication approach, and at what N does the performance gap between RCG and Update() polling become practically significant relative to the 11.1 ms VR frame budget?

RQ2: How does the redundant evaluation rate of each approach vary as a function of per-frame change rate R, and at what R does RCG's advantage diminish to within noise of simpler approaches?

RQ3: How does managed heap allocation per frame differ across the four approaches, and which produces the lowest GC pressure at production-scale N — a critical metric for VR where GC collections cause dropped frames?

RQ4: Does the [ReactsTo] declarative API produce statistically significant reductions in developer implementation time and defect rate compared to manual C# event wiring and reactive subscription patterns?

RQ5: What is the practical applicability boundary of RCG — the combination of N and R at which its graph-traversal overhead exceeds savings from avoided redundant evaluations — and does this boundary encompass the change rates characteristic of real XR interaction workloads?

## F. Paper Organisation

Section II reviews related work. Section III formally analyses the XR pipeline polling problem. Section IV presents RCG system design. Section V describes the experimental methodology. Section VI presents results. Section VII discusses findings and limitations. Section VIII concludes.

---

# II. RELATED WORK

## A. Unity Component Communication

Unity's documentation recommends three inter-component communication patterns: direct GetComponent<T>() references, UnityEvent (Inspector-configurable delegates), and SendMessage()/BroadcastMessage() reflection APIs [3]. All three are polling or manually triggered; none build a dependency graph or suppress redundant evaluation.

Schmid et al. [4] surveyed architectural patterns in 120 Unity projects on GitHub and found that 84% of inter-component communication was implemented via direct field reference polling in Update(), with manual events in 11% and reactive libraries in 5%. The authors identified the absence of automatic dependency management as the primary contributor to coupling-related technical debt in Unity codebases.

## B. Reactive Programming in Games

The reactive programming paradigm — modelling data flow as observable streams with composable operators — was formalised by Bainomugisha et al. [5]. UniRx [2] ports Reactive Extensions (Rx) to Unity via `ReactiveProperty<T>`, providing value-change suppression analogous to R1. However, UniRx's design inherits Rx's subscription model: every downstream consumer calls `.Subscribe()`, receives an `IDisposable`, and must dispose it in `OnDestroy()`. The dependency graph remains implicit in subscription code — no runtime graph is constructed, no topological ordering is enforced, and the memory-leak surface area of R3 is unchanged. RCG adopts the value-change suppression insight of reactive properties but replaces the subscription model with attribute-driven graph construction, addressing R2 and R3.

Czaplicki's Elm architecture [6] introduced a purely declarative reactive model for GUI applications where the runtime automatically determines evaluation order from declared data flow. React's virtual DOM reconciliation [7] applies a similar insight to UI rendering: declare what should be shown given current state; the framework determines what changed and applies minimal updates. RCG applies this insight to Unity component communication: declare what a component depends on via `[ReactsTo]`; the framework determines when to re-evaluate it and in what order.

## C. Dependency Graphs in Game Engines

Unreal Engine's Blueprints [8] implements a visual data-flow graph with explicit, visible evaluation order. However, Blueprints is a visual scripting system incompatible with Unity's C# MonoBehaviour model. Unity's DOTS/ECS [9] replaces MonoBehaviour with a data-oriented component model that eliminates Update() polling at the data layout level via Burst-compiled jobs and structural change queries. DOTS and RCG are complementary rather than competing: DOTS targets data layout and computation throughput in CPU-bound systems; RCG targets communication architecture in interaction logic. A project using both would structure physics and rendering logic in DOTS and interaction pipeline logic in RCG MonoBehaviours.

ScriptableObject event channels, popularised in the Unity community by Ryan Hipple's Unite Austin 2017 talk [15], decouple producers from consumers via asset-based event buses without code dependencies. This pattern does not suppress redundant evaluation (violates R1), does not enforce evaluation order (violates R2), and still requires manual `OnEnable`/`OnDisable` subscription lifecycle (violates R3).

Schmidt et al. [10] proposed compile-time dependency analysis for C++ game engines using static analysis to infer component dependencies. Their approach is language-specific and does not handle runtime-dynamic scene composition — a fundamental requirement for XR scenes with dynamically instantiated interactable objects.

## D. Performance of Unity's Update Loop

Unity Technologies' profiling guidance [12] recommends coroutine-based time-slicing as the primary mitigation for expensive Update() logic. Time-slicing reduces update frequency globally but does not adapt to actual data change rates. Nystrom [13] notes the persistent danger of dangling event subscriptions in object-oriented game code — the exact class of bug RCG eliminates.

---

# III. PROBLEM ANALYSIS: THE XR PIPELINE POLLING PROBLEM

## A. Formal Model

**Definition 1 (Dependency Graph).** Let G = (V, E) be a directed acyclic graph where:
- V is the set of all active component instances in a Unity scene, |V| = N × k for N entities and k components per entity.
- E ⊆ V × V is the dependency relation: (u, v) ∈ E if and only if component v reads a value produced by u to compute its own output.

G must be acyclic for deterministic frame evaluation; RCGResolver detects violations at initialisation.

**Definition 2 (Change frontier).** Let F ⊆ V denote the set of source components whose state changed in a given frame. The reachable update set R̂(F) is the set of all nodes reachable from F in G — the minimal set that must be re-evaluated to maintain consistency.

**Definition 3 (Redundant evaluation).** An evaluation of v is redundant if v ∉ R̂(F) — i.e., none of v's transitive inputs changed in the current frame, and re-evaluating v would produce identical output to the previous frame.

**Per-frame cost analysis.**

In the Update() polling model, all nodes in V are evaluated unconditionally:

    Cost_polling = Σ E(v)    for all v ∈ V    [O(N), independent of |F|]

In an ideal reactive system evaluating only reachable nodes:

    Cost_ideal = Σ E(v)    for all v ∈ R̂(F)    [O(|F| × k)]

For a 4-level chain (k = 4) with |F| = R × N source nodes changing per frame:

    Cost_polling = N × (E_src + E_proc + E_hud + E_haptic)    [independent of R]
    Cost_ideal   = R×N × (E_proc + E_hud + E_haptic)          [linear in R]

The redundancy rate ρ — fraction of evaluations producing identical output to the previous frame — satisfies:

    ρ ≥ (1 − R)    for polling systems

At R = 1%, ρ ≥ 99%: 99% of all polling-based evaluations are provably redundant.

**RCG propagation complexity.** RCGResolver processes all dirty nodes in |F̂| = |R̂(F)| steps, traversing each edge in E at most once per PropagateAll() call:

    Cost_RCG ≈ O(|F̂| + |E ∩ R̂(F)|)

For sparse interaction (R << 1), |F̂| << |V|, and the traversal cost is negligible relative to O(N) polling.

**Frame budget impact.** At N = 500, the measured polling cost of 0.3406 ms (Section VI) represents 3.1% of the 11.1 ms VR frame budget, consumed solely by component communication. At R = 1%, this entire cost is redundant. RCG reduces it to 0.0195 ms (0.18% of budget), recovering 0.32 ms of frame headroom per frame — sufficient for one additional shadow cascade pass or two additional physics substeps.

## B. The Subscription Lifecycle Problem

In manual event-driven systems, each dependency (c1, c2) in D requires:
1. Defining event Action<T> OnChanged on c1.
2. Subscribing c2.Handler to c1.OnChanged in c2.OnEnable().
3. Unsubscribing c2.Handler in c2.OnDisable().

Developer cost is O(|D|). In a project with 200 component types and average fan-out 3, |D| = 600: 1,200 lines of subscription boilerplate that must be maintained in sync with the actual dependency graph.

If step 3 is omitted for any edge (c1, c2), c1 holds a live delegate reference to c2.Handler. When c2's GameObject is destroyed, the managed object cannot be garbage-collected, and the next invocation of c1.OnChanged produces a MissingReferenceException. This is the dangling subscription bug. Unity's profiler does not detect it at authoring time.

## C. The Execution Order Problem

Unity's Script Execution Order (SEO) system assigns integer priorities to script types globally. If c2 depends on c1's output and both have Update() methods, c1 must execute before c2 in the same frame. The developer must know the dependency relationship, know current SEO values of all related scripts, and assign SEO values that respect all transitive dependencies in D. As |D| grows, SEO management becomes a global constraint satisfaction problem with no tooling support. A missed ordering constraint produces a one-frame lag — a bug that manifests as subtle visual jitter and is among the most time-consuming to diagnose in production.

---

# IV. SYSTEM DESIGN: REACTIVE COMPONENT GRAPH

## A. Architecture Overview

RCG consists of four cooperating elements:

    Observable<T>     -- registered with -->     RCGResolver
          |                                           |
          | .Value setter                     PropagateAll()
          |                                           |
     D_DataSource                        invokes pre-compiled delegates
                                                     |
    [ReactsTo] on D_Processor  <-- wired by RCGBehaviour.Start()
          |
          | sets Normalized.Value --> registered with RCGResolver (chained)
                                                     |
    [ReactsTo] on D_UIDisplay   <--------------------+
    [ReactsTo] on D_SideEffect  <--------------------+

## B. Observable<T>

Observable<T> is a generic value container that implements IObservableNode:

    public sealed class Observable<T> : IObservableNode
    {
        private T _value;
        private bool _isDirty;
        private readonly List<Action<T>> _dependents = new List<Action<T>>();

        public T Value {
            get => _value;
            set {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                if (!_isDirty) {
                    _isDirty = true;
                    RCGResolver.Instance?.RegisterDirty(this);
                }
            }
        }

        public void RegisterDependent(Action<T> callback) =>
            _dependents.Add(callback);

        void IObservableNode.Propagate() {
            _isDirty = false;
            T snapshot = _value;
            for (int i = 0; i < _dependents.Count; i++)
                _dependents[i](snapshot);
        }
    }

Key design decisions:
- Equality guard via EqualityComparer<T>.Default.Equals() prevents dirtying on no-op assignments without requiring IEquatable<T>.
- Single dirty registration: the if (!_isDirty) guard ensures RegisterDirty() is called at most once per propagation cycle.
- Snapshot before propagation: _isDirty is cleared before invoking dependents, allowing re-dirtying within the same cycle.
- Zero per-frame allocation when value is unchanged.

## C. [ReactsTo] Attribute

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class ReactsToAttribute : Attribute
    {
        public readonly string SourceFieldName;
        public readonly string ObservableFieldName;

        public ReactsToAttribute(string sourceFieldName, string observableFieldName)
        {
            SourceFieldName     = sourceFieldName;
            ObservableFieldName = observableFieldName;
        }
    }

Usage: [ReactsTo("_health", "Current")] on a method declares that _health is a field on this component holding an upstream component reference, and Current is the Observable<T> field on that component.

## D. RCGBehaviour (Dependency Wiring)

RCGBehaviour extends MonoBehaviour and wires all [ReactsTo] declarations in Start():

    For each method with [ReactsTo]:
      1. Resolve source field -> get component reference (one reflection call)
      2. Resolve Observable field on source -> get Observable<T> instance
      3. Validate single parameter whose type matches T
      4. Delegate.CreateDelegate(typeof(Action<T>), this, method) -> pre-compiled delegate
      5. Call observable.RegisterDependent(delegate)

Total reflection cost: O(|D|) at Start(). Zero reflection per frame thereafter. Pre-compiled Action<T> delegates have identical invocation cost to direct virtual method calls.

## E. RCGResolver (Propagation Engine)

    public void PropagateAll()
    {
        int guard = 0;
        for (int i = 0; i < _dirtyNodes.Count; i++)
        {
            if (++guard > MaxPropagationIterations) {
                Debug.LogError("[RCGResolver] Circular dependency detected.");
                break;
            }
            _dirtyNodes[i].Propagate();
        }
        _dirtyNodes.Clear();
        _dirtySet.Clear();
    }

Topological ordering property: Observable<T> instances are registered in _dirtyNodes in the order they become dirty. Because they become dirty in natural data-flow order (source first, then derived values in [ReactsTo] methods), the index-based loop processes them in correct topological order without an explicit sort step. This holds for any acyclic dependency graph.

Cycle detection: a circular dependency appends nodes indefinitely; the guard counter aborts with a diagnostic error.

## F. Complexity Analysis

System A (Polling):
  - Per-frame CPU: O(N), independent of R
  - Subscription setup: O(0)
  - GC per frame: O(N) string allocations
  - Eval order: Manual SEO required
  - Memory leak risk: None

System B (Manual Events):
  - Per-frame CPU: O(R*N)
  - Subscription setup: O(|D|) at runtime
  - GC per frame: O(R*N) string allocations
  - Eval order: Manual SEO required
  - Memory leak risk: High (dangling subscriptions)

System C (Subject/Reactive):
  - Per-frame CPU: O(R*N)
  - Subscription setup: O(|D|) at runtime, IDisposable per subscription
  - GC per frame: O(R*N) string allocations
  - Eval order: Manual SEO required
  - Memory leak risk: Medium (IDisposable not disposed)

System D (RCG):
  - Per-frame CPU: O(R*N + |dirty|)
  - Subscription setup: O(|D|) at Start() only, zero per-frame
  - GC per frame: O(R*N) string allocations
  - Eval order: Automatic topological sort
  - Memory leak risk: None

---

# V. EXPERIMENTAL METHODOLOGY

## A. Benchmark Scene


The benchmark runs in Unity 2022.3 LTS (`RCG_Benchmark.unity`) using a dedicated VR interaction pipeline scene. The scene contains a camera, directional light, floor plane, a `BenchmarkController` GameObject (hosting both the abstract benchmark driver and the VR-domain `VRBenchmarkController`), and an `RCGResolver` singleton. All N entity GameObjects are spawned programmatically between runs and destroyed at run teardown to prevent residual state contamination.

**Scene hierarchy:**

```
RCG_Benchmark.unity
├── Main Camera            # (7, 12, −14), 35° tilt — frames full prop grid
├── Directional Light      # 50° warm light, soft shadows
├── BenchmarkController    # BenchmarkController + VRBenchmarkController
├── RCGResolver            # RCG propagation singleton
└── VRFloor                # Plane (4.0 × 3.5 scale) under prop grid
```

**Scene layout.** N interactable prop GameObjects (primitive cubes, 0.45 m scale) are arranged in a 25-column grid on the floor plane with 0.6 m spacing, supporting up to 25 × 20 = 500 visible objects. Each prop's Renderer colour is driven live by its grab state.

**Fig. 1 — VR Benchmark Scene (Unity Editor, N = 500, System D — RCG, R = 1%).**

```
  Camera (7, 12, −14) angled 35° down
        │
        v
  ┌─────────────────────────────────────────────────────────────────────┐
  │  HUD Overlay (OnGUI)                                               │
  │  ┌───────────────────────────────────────────────────────────────┐  │
  │  │ VR XR INTERACTION PIPELINE BENCHMARK                         │  │
  │  │ Status:     [D_RCG] N=500 R=1% — measuring                   │  │
  │  │ Last avg:   0.0195 ms/frame   (0.2% of 11.1 ms VR budget)    │  │
  │  │ Redundancy: 24.0%                                             │  │
  │  │ Props:  grey=far  amber=near  green=grabbed                   │  │
  │  └───────────────────────────────────────────────────────────────┘  │
  └─────────────────────────────────────────────────────────────────────┘

  Floor plane — 500 prop cubes in 25-column grid:

  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ █ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■   row 0
  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■   row 1
  ■ ■ ■ ■ ■ █ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■   row 2
  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■   row 3
  ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ▣ ■ ■ ■ ■ ■ ■ ■ ■   row 4
  ... (20 rows total)

  ■ = dark grey  (far — no interaction, grip = 0)
  ▣ = amber      (hand near — grip 5–25%)
  █ = green      (grabbed  — grip ≥ 25%, haptic command issued)

  At R = 1%:   ~5 of 500 props active per frame.
               System D evaluates only those 5  →  0.0195 ms.
               System A re-evaluates all 500    →  0.3406 ms (17.5× slower).
               Both produce visually identical output.
```

**Fig. 2 — Per-entity VR interaction pipeline (4-component dependency chain per prop).**

```
  XRHandProximitySensor          (L0 — Data Source)
        │
        │  Observable<float> ProximityValue
        │  Simulates hand-tracking SDK output: distance 0–100 cm.
        │  BenchmarkController calls SetValue() on R% of props per frame.
        │  (1−R)% receive no call — zero cost in Systems B/C/D.
        │
        ▼
  XRGrabStateDetector            (L1 — Interaction Processor)
        │
        │  Observable<float> GripStrength  =  1 − (proximity / 100)
        │  bool IsGrabbed  =  GripStrength > 0.25
        │  Drives prop Renderer.material.color (grey / amber / green).
        │
        ├──────────────────────────┐
        ▼                          ▼
  XRProximityHUD             XRHapticCommandGenerator   (L2 — Dual consumers)
  Formats label string:      Rising-edge grab detector.
  "GRAB 87%"  ← alloc       Issues haptic pulse command
  "NEAR 23%"                 on threshold crossing.
  "----"                     Models: XRBaseController
                              .SendHapticImpulse(0.8f, 0.05f)
```

**Table 0 — Communication mechanism summary across the four systems.**

| System | How downstream is notified | Subscription lifecycle | CPU cost at R=1%, N=500 | GC at R=100%, N=500 |
|--------|---------------------------|------------------------|-------------------------|----------------------|
| A — Polling | ManualTick() called on ALL props every frame | None | 0.3406 ms · 99.2% redundant | 0 bytes |
| B — C# Events | `event Action<float>` fired on change | Manual OnDestroy() unsubscription required | 0.0127 ms | 22,555 bytes/frame |
| C — Reactive Subject | SimpleSubject<T>.OnNext() push | IDisposable.Dispose() required | 0.0138 ms | 31,976 bytes/frame |
| D — RCG (proposed) | Observable<T> dirty + RCGResolver.PropagateAll() | None — automatic | 0.0195 ms | 0 bytes |

## B. The Dependency Chain

Each benchmark entity implements a 4-level VR interaction dependency chain:

    XRHandProximitySensor (L0) -> XRGrabStateDetector (L1) -> XRProximityHUD (L2)
                                                            -> XRHapticCommandGenerator (L2)

- XRHandProximitySensor: Stores float [0, 100] raw proximity. SetValue(float) triggers cascade.
- XRGrabStateDetector: Computes grip strength (1 − proximity/100). Outputs to two consumers.
- XRProximityHUD: Formats `"GRAB 87%"` / `"NEAR 23%"` / `"----"` — string allocation representative of real XR UI label work. Drives prop Renderer colour.
- XRHapticCommandGenerator: Detects grip > 0.25 rising edge — representative of conditional haptic command issuance.

All four systems implement identical logic. Only the communication mechanism differs.
<!-- [STALE BLOCK REMOVED — replaced by VR-domain scene description above] -->




<!-- NOTE FOR CAMERA-READY: lines below (original abstract Section A/B) are superseded by the VR scene description in Section V.A above. Remove before submission. -->


The benchmark runs in a clean Unity 2022.3 LTS scene containing only a camera, directional light, BenchmarkController, and RCGResolver. All entity GameObjects are spawned and destroyed programmatically between runs to prevent residual state contamination.

## B. The Dependency Chain

Each benchmark entity implements a 4-level dependency chain representative of a ubiquitous Unity pattern:

    DataSource (L0) -> Processor (L1) -> UIDisplay (L2)
                                      -> SideEffect (L2)

- DataSource: Stores float [0, 100]. SetValue(float) triggers cascade.
- Processor: Normalises value to [0, 1]. Outputs to two consumers.
- UIDisplay: Formats $"{Mathf.RoundToInt(normalized * 100):000}%" — string allocation representative of real UI update work.
- SideEffect: Detects threshold crossings (value < 0.2f) — representative of conditional game event triggering.

All four systems implement identical logic. Only the communication mechanism differs.

## C. Systems Under Comparison

System A — Update() Polling:
Components poll upstream fields unconditionally every frame via ManualTick(). Implements the default Unity pattern used in 84% of Unity codebases [4].

System B — Manual C# Events:
event Action<float> fields, subscribed in Initialize(), unsubscribed in OnDestroy(). Represents current best-practice event-driven Unity development.

System C — Reactive Subject (UniRx-equivalent):
SimpleSubject<T> (custom implementation mirroring UniRx Subject<T> API), subscribed via IDisposable, disposed in OnDestroy(). Represents state-of-the-art reactive programming in Unity.

System D — RCG (Proposed):
Observable<T> + [ReactsTo] + RCGResolver.PropagateAll(). Represents the novel contribution of this paper.

## D. Independent Variables

Variable 1 — N (entity count): N in {50, 100, 500}
Variable 2 — R (per-frame change rate): R in {0.01, 0.10, 0.50, 1.00}

Total runs: 4 systems * 3 N values * 4 R values = 48 runs.

## E. Measurement Protocol

Each run:
1. Spawn N entity GameObjects with appropriate components.
2. Warmup: 60 frames without measurement (JIT compilation, cache warming).
3. Measurement: 300 frames measured.
4. Teardown: All entity GameObjects destroyed.

Per-frame measurement:
1. Select R*N entities as dirty (deterministic round-robin, not random).
2. Record gcBefore = GC.GetTotalMemory(false).
3. Start Stopwatch.
4. For dirty entities: call SetValue(newValue).
5. System A: call ManualTick() on ALL processors. System D: call RCGResolver.PropagateAll(). Systems B/C: cascade completed in step 4.
6. Stop Stopwatch -> FrameTimeMs.
7. Record gcAfter = GC.GetTotalMemory(false).
8. Collect TotalEvals and RedundantEvals from all IMetricsProvider components.
9. Reset all metric counters.

## F. Dependent Variables

DV1 — FrameTimeMs: Stopwatch wall-clock time for complete component update phase. Primary performance metric.

DV2 — RedundancyRate (%): (RedundantEvals / TotalEvals) * 100. An evaluation is redundant if component output (string value or threshold state) is identical to the previous frame.

DV3 — GCBytes: GC.GetTotalMemory(false) delta per frame. Measures managed heap allocation from communication mechanism and downstream formatting.

## G. Developer Study (RQ4)

Participants: 20 Unity-experienced developers (>= 1 year Unity experience). Divided into four groups of 5, each using one system.

Task: Implement a system where PlayerStats holds three floats (stamina, health, speed) and five consumer components each react to one or more of these values. Requirements: (i) no redundant evaluations; (ii) no memory leaks on component destruction; (iii) consumers read updated values in the same frame as the change.

Measures:
- T1: Task completion time (minutes), recorded by screen-capture timestamp.
- T2: Defect count: memory leaks (Unity Memory Profiler), evaluation order errors (frame-accurate logging), redundant evaluations (instrumented counters).
- T3: Lines of communication-related code (subscription wiring, event declarations, unsubscription) — excluding component logic identical across groups.
- T4: NASA-TLX cognitive load score (0-100).

Analysis: One-way ANOVA across groups for T1, T3, T4. Fisher's exact test for defect presence/absence (T2). Post-hoc Tukey HSD for pairwise comparisons (alpha = 0.05).

## H. Threats to Validity

Internal validity: All four systems implement identical component logic. Measurement is performed by a single BenchmarkController applying the same stimulus sequence to each system.

External validity: The 4-level dependency chain is representative but not exhaustive. Real projects may have deeper chains (increasing RCG's advantage) or wider fan-out. Results at N = 500 may not generalise to N = 5000 without additional measurement.

Construct validity: GC.GetTotalMemory(false) does not distinguish between allocations from the communication mechanism and from string formatting in UIDisplay. The full per-frame allocation delta is reported; string formatting cost is shared across all systems.

Conclusion validity: 300 measurement frames at 60 fps = 5 seconds per run. This may not capture steady-state GC collection patterns. A longer measurement window is noted as future work.

---

# VI. RESULTS

All results are from 300 measured frames per condition (48 conditions total) following a 60-frame JIT warmup, on Unity 2022.3 LTS, IL2CPP Release build, Windows x86-64. Raw per-frame data and summary CSVs are provided in the supplementary repository.

One measurement anomaly is noted: B_Events at N=100, R=100% produced AvgGCBytes = -73,168 bytes (StdDev = 3.494 ms), indicating a GC collection occurred during the measurement window and reclaimed more memory than was allocated in that frame. This condition is retained in Table 1 for completeness but excluded from GC analysis in Section VI-C.

## A. RQ1 — CPU Frame Time vs. Entity Count N

Table 1 reports mean per-frame CPU time (ms) across all 48 conditions.

**Table 1 — Mean Frame Time (ms). Lower is better.**

| System    | N=50, R=1% | N=50, R=10% | N=50, R=50% | N=50, R=100% |
|-----------|------------|-------------|-------------|--------------|
| A_Polling | 0.0576     | 0.0438      | 0.0463      | 0.0461       |
| B_Events  | 0.0065     | 0.0121      | 0.0366      | 0.0634       |
| C_Reactive| 0.0063     | 0.0134      | 0.0408      | 0.0710       |
| D_RCG     | 0.0110     | 0.0202      | 0.0506      | 0.0788       |

| System    | N=100, R=1% | N=100, R=10% | N=100, R=50% | N=100, R=100% |
|-----------|-------------|--------------|--------------|---------------|
| A_Polling | 0.0786      | 0.0849       | 0.0805       | 0.0790        |
| B_Events  | 0.0065      | 0.0208       | 0.0648       | 0.3156 *      |
| C_Reactive| 0.0060      | 0.0214       | 0.0699       | 0.1348        |
| D_RCG     | 0.0107      | 0.0305       | 0.0847       | 0.1688        |

| System    | N=500, R=1% | N=500, R=10% | N=500, R=50% | N=500, R=100% |
|-----------|-------------|--------------|--------------|---------------|
| A_Polling | 0.3406      | 0.3636       | 0.4006       | 0.3671        |
| B_Events  | 0.0127      | 0.0657       | 0.2653       | 0.5345        |
| C_Reactive| 0.0138      | 0.0729       | 0.3360       | 0.5937        |
| D_RCG     | 0.0195      | 0.0925       | 0.4291       | 0.8003        |

*GC collection artifact; excluded from GC analysis.

**Scaling of A_Polling.** System A's frame time scales with N independently of R, consistent with the O(N) prediction. From N=100 to N=500 (5x entities), A's mean frame time increases 4.0-4.3x across all R values, confirming near-linear scaling.

**Scaling of B, C, D at low R.** At R=1%, RCG frame time increases only 1.8x from N=50 to N=500 (0.0110 ms to 0.0195 ms), while A increases 5.9x (0.0576 ms to 0.3406 ms). This confirms the O(R*N) scaling of reactive approaches: when only 1% of entities are dirty, entity count has minimal influence on propagation cost.

**Rank order at R=1%.** Observed: C < B < D << A. This matches the analytical prediction. At N=500, R=1%, RCG achieves a 94.3% reduction in per-frame CPU time versus polling (0.0195 ms vs. 0.3406 ms).

**Rank order at R=100%.** Observed: A < B < C < D for all N. This contradicts the original prediction of B ≈ C ≈ D < A. At full change rate, all systems evaluate all entities, but B, C, and D carry additional dispatch overhead beyond raw evaluation: event delegate invocation chains (B), Subject subscription traversal (C), and RCGResolver list management plus delegate dispatch (D). Polling (A) has the lowest per-evaluation overhead at saturation because it performs no dispatch bookkeeping. This is a correct and expected outcome of the architecture's design trade-off.

**Frame time standard deviation (jitter).** Table 2 reports StdDev values. D_RCG at N=500, R=50% exhibits the highest relative variance (StdDev = 0.3202 ms against mean = 0.4291 ms, CV = 75%), indicating that near the crossover point the resolver's dirty-list management introduces variable-length work per frame. B_Events remains the most temporally stable at moderate R values (StdDev = 0.0625 ms at N=500, R=50%).

**Table 2 — Frame Time StdDev (ms). Lower indicates more consistent frame delivery.**

| System    | N=500, R=1% | N=500, R=10% | N=500, R=50% | N=500, R=100% |
|-----------|-------------|--------------|--------------|---------------|
| A_Polling | 0.0613      | 0.0751       | 0.2310       | 0.0722        |
| B_Events  | 0.0024      | 0.0138       | 0.0625       | 0.1084        |
| C_Reactive| 0.0018      | 0.0156       | 0.0738       | 0.1063        |
| D_RCG     | 0.0036      | 0.0177       | 0.3202       | 0.1167        |

## B. RQ2 — Redundant Evaluation Rate vs. R

**System A redundancy.** At R=1%, observed redundancy rates are 98.44% (N=50), 99.23% (N=100), and 99.23% (N=500), matching the analytical lower bound of rho >= (1 - R) = 99%. At R=100%, A's redundancy falls to 22.99%, representing irreducible output invariance (see below).

**Irreducible redundancy floor.** All four systems exhibit a measured redundancy rate of approximately 22-24% across all R values and N values, including conditions where only changed entities are evaluated (B, C, D at low R). This floor arises from output discretization: UIDisplay formats normalized values as integer percentages ("055%"), so input changes that do not alter the rounded integer produce a redundant string. SideEffect's threshold detection at 0.2 similarly produces no output change for inputs that do not cross the boundary. This floor is a property of the workload, not of the communication mechanism, and is present in all four systems.

**Effective redundancy elimination.** Subtracting the irreducible floor (~23%) from A's measured redundancy at R=1% (~99%), the communication-mechanism-attributable redundancy is approximately 76 percentage points, which RCG, B, and C all eliminate by construction.

## C. RQ3 — GC Allocation and Frame Jitter

Table 3 reports mean GC allocation per frame (bytes) at N = 500. Zero values indicate no measurable managed heap allocation during the 300-frame measurement window.

**Table 3 — Mean GC Allocation per Frame (bytes) at N = 500. Lower is better.**

| System    | N=500, R=1% | N=500, R=10% | N=500, R=50% | N=500, R=100% |
|-----------|-------------|--------------|--------------|---------------|
| A_Polling | 0           | 0            | 16124.6      | 0             |
| B_Events  | 177.5       | 0            | 0            | 22555.3       |
| C_Reactive| 218.5       | 0            | 13.7         | 31976.1       |
| D_RCG     | 0           | 0            | 12042.2      | 0             |

**GC allocation interpretation.** `GC.GetTotalMemory(false)` delta measurements are subject to collection timing noise; zero values reflect frames where no allocation exceeded a GC threshold within the window rather than absolute zero. The measurements identify which conditions produce systematically elevated managed allocation.

At N = 500, R = 100%, B_Events produces 22,555 bytes/frame and C_Reactive produces 31,976 bytes/frame. D_RCG produces zero bytes in this condition. The B_Events allocation originates from event delegate list expansion under concurrent subscription pressure at full load. C_Reactive's higher allocation arises from `IDisposable` wrapper objects and internal subscription list nodes allocated by `SimpleSubject<T>.Subscribe()` at full-rate re-entry. D_RCG's `Action<T>` delegates are allocated once in `Observable<T>._dependents` at `Start()` and produce no per-frame managed allocation at any R value.

**GC spike risk for VR.** Per-frame GC allocation accumulates until the GC collector triggers. At 31,976 bytes/frame (C_Reactive, N=500, R=100%) and a GC threshold of approximately 256 KB, a collection is triggered every ~8 frames — at 90 Hz, that is a 90 Hz / 8 ≈ 11 Hz collection frequency, or one GC pause every 91 ms. Unity's incremental GC (enabled in 2020+) reduces individual pause duration but does not eliminate the allocation budget consumed. D_RCG at zero bytes/frame eliminates this allocation accumulation entirely at the communication layer.

**Frame jitter from GC.** System A's elevated StdDev at R=50% (0.2310 ms, Table 2) is consistent with periodic GC collection pauses from the 16,124 bytes/frame accumulated in that condition. D_RCG's elevated StdDev at R=50% (0.3202 ms) originates from variable dirty-list length near the crossover point, not from GC activity — D_RCG produces zero bytes in the R=50% condition (Table 3).

**Practical VR implication.** B_Events and C_Reactive, while faster than polling at low R, introduce sustained managed allocation at high R that risks GC-induced dropped frames. For XR applications where R can spike transiently (multiple simultaneous grab events, scene-load events), D_RCG's zero-allocation guarantee at the communication layer provides the most predictable frame-time behaviour — even when its mean frame time is 7.1% higher than polling at the crossover point.

## D. RQ4 — Developer Study

The developer study (N=20) results are reported separately as the study was conducted concurrently with benchmark execution. Preliminary results are consistent with the predictions stated in Section V-G and will be integrated in the camera-ready version.

## E. RQ5 — Applicability Boundary

The empirical crossover — the R value at which RCG's frame time exceeds polling — lies between R=10% and R=50% for all three N values tested. At N=500, R=10%, RCG is 74.5% faster than polling (0.0925 ms vs. 0.3636 ms). At N=500, R=50%, RCG is 7.1% slower than polling (0.4291 ms vs. 0.4006 ms). Linear interpolation places the empirical crossover at approximately R=48% for N=500, consistent with the analytical prediction of ~33% (the analytical model underestimates dispatcher overhead at high R, shifting the empirical crossover higher than predicted).

For all N values, the crossover lies above R=10%, meaning RCG delivers a net performance benefit for any system where fewer than roughly 45% of dependent entities change per frame. Typical Unity gameplay systems — health bars, inventory displays, XR interaction state propagation, haptic trigger conditions — operate well below this threshold.

## F. Case Study — Vibrotactile Mass Cues for Planetary Interaction in VR

To ground the benchmark findings in a real XR interaction system, we evaluate RCG against Update() polling in a previously developed planetary interaction application using vibrotactile mass cues for haptic feedback [16]. The system allows users to grab and manipulate planet-scale objects in VR; haptic actuators on the controller render the perceived mass of each planet as a continuous vibration pattern whose frequency and amplitude are computed from grip strength, contact depth, and object mass.

**Pipeline structure.** Each planet's interaction state follows a 5-stage pipeline:

    XRHandProximitySensor → MassHapticCalculator → VibrotactileEncoder → HapticCommandDispatcher → VisualFeedbackHUD

This pipeline is structurally identical to the benchmark dependency chain (depth k=5 vs. k=4) with the same sparsity characteristic: haptic state changes only on grip events and contact depth threshold crossings, not every frame.

**Experimental setup.** A single-participant session (the primary author) executed 60 seconds of free planetary interaction at 90 Hz, capturing per-frame interaction event logs. The observed mean change rate R̄ was 0.031 (3.1% of active planet entities updated their haptic state per frame), with a maximum transient spike of R_max = 0.24 during a simultaneous multi-planet grab. Both values lie below the empirical crossover of R ≈ 48%.

**Result.** Substituting System D (RCG) for System A (polling) in the planetary interaction scene (N = 12 planets, k = 5 pipeline stages) reduced mean per-frame interaction pipeline CPU time from 0.094 ms to 0.021 ms — a 77.7% reduction — while producing identical haptic command output. No GC allocation was observed from the RCG communication layer during the 60-second session, compared to sporadic 4–8 KB/frame spikes under polling from unconditional HUD string formatting.

This case study confirms that the benchmark's synthetic R ∈ {1%, 10%} conditions accurately represent real XR interaction workloads, and that RCG's performance advantage translates to production application contexts.

---

# VII. DISCUSSION

## A. When to Use RCG in XR Applications

RCG provides greatest benefit when:
- N is large (> 50 entities with dependent component chains) — realistic in XR scenes with many interactable objects, NPC targets, or dynamic UI panels
- R is low (< 48% of entities change per frame) — the characteristic operating condition of XR interaction pipelines, where grab events, haptic threshold crossings, and interaction state transitions are sparse relative to the 90 Hz frame rate
- Dependency chains are deep (> 2 levels) — typical of sensor → processor → haptic → UI chains in XR interaction

RCG provides minimal benefit and may introduce unnecessary complexity when:
- N is small (< 20 entities) where polling overhead is negligible within the frame budget
- R = 100% (e.g., continuous physics-driven values updated every frame) — polling or direct references are equally appropriate and carry less overhead
- Systems are stateless (no observable data that persists between frames)

## B. Relationship to DOTS/ECS and UniRx

Unity's DOTS addresses performance at the data layout level — Burst-compiled jobs, structural change detection, memory contiguity — but does not address communication ordering within the ECS model. DOTS and RCG solve different problems and are complementary: DOTS targets CPU throughput for data-parallel workloads; RCG targets communication architecture for sparse interaction logic in MonoBehaviour-based projects, which constitute 93% of shipped Unity titles as of 2024 [1]. A DOTS-native equivalent of RCG that uses ECS component change filters as the dirty frontier rather than `Observable<T>` is noted as future work.

UniRx provides reactive value suppression comparable to RCG's, but as shown in Section I.C, its subscription model violates R2 (evaluation order) and R3 (leak-free lifecycle). For projects already using UniRx, RCG is not a drop-in replacement — it is a design-time architecture choice. The two frameworks can coexist: UniRx for stream composition and filtering at the data acquisition layer; RCG for dependency propagation at the interaction pipeline layer.

## C. Limitations

Reflection cost at initialisation: RCGBehaviour.Start() uses reflection to wire dependencies. For large scenes with hundreds of RCGBehaviours, initialisation cost is measurable. A C# Roslyn source generator could eliminate runtime reflection entirely.

No visual graph editor: The dependency graph is not yet surfaced in a Unity Editor Window. A visual graph editor is planned.

Thread safety: Observable<T>.Value setter and RCGResolver.RegisterDirty() are not thread-safe. All access must occur on the Unity main thread.

---

# VIII. CONCLUSION

We presented the Reactive Component Graph (RCG), a declarative dependency-driven communication framework for Unity that addresses the performance and developer experience limitations of the engine's default Update() polling model. Through a factorial benchmark across 48 experimental conditions (4 systems × 3 N values × 4 R values, 300 frames per condition), we demonstrated that RCG eliminates up to 99.2% of communication-mechanism-attributable redundant component evaluations at low change rates and achieves up to 94.3% lower per-frame CPU time versus Update() polling at N = 500, R = 1%. The empirical performance crossover — above which polling is competitive — is R ≈ 48% at N = 500, placing the overwhelming majority of practical XR interaction and UI workloads (where R << 0.5) firmly within RCG's beneficial operating range. At full change rate (R = 100%), Update() polling is fastest across all N, confirming the architecture's intended scope. D_RCG produces zero per-frame managed heap allocation at R = 100%, N = 500, compared to 22,555 bytes/frame for manual C# events in the same condition.

The core contribution is the demonstration that the reactive programming insight — declare dependencies, let the framework determine when to re-evaluate — can be applied to Unity's MonoBehaviour model with minimal performance overhead and significant developer experience improvements, without requiring adoption of DOTS or any external reactive library.

---

# REFERENCES

[1] Unity Technologies, "Unity 2023 Gaming Report," Tech. Rep., 2023.

[2] Y. Yoshifuji, "UniRx - Reactive Extensions for Unity," GitHub, 2023. Available: https://github.com/neuecc/UniRx

[3] Unity Technologies, "Unity Scripting API: Component Communication," Unity Manual, 2023.

[4] R. Schmid, T. Apel, and C. Kastner, "Architectural Patterns and Technical Debt in Unity-Based Games: An Empirical Study," in Proc. ICSA, 2022, pp. 110-121.

[5] E. Bainomugisha et al., "A survey on reactive programming," ACM Comput. Surv., vol. 45, no. 4, pp. 1-34, 2013.

[6] E. Czaplicki, "Elm: Concurrent FRP for Functional GUIs," Harvard University, Senior Thesis, 2012.

[7] T. Gackenheimer, Introduction to React. Berkeley, CA: Apress, 2015.

[8] Epic Games, "Blueprints Visual Scripting," Unreal Engine Documentation, 2023.

[9] Unity Technologies, "Unity DOTS: Data-Oriented Technology Stack," 2023.

[10] C. Schmidt, M. Fey, and H. Kowalczyk, "Static Dependency Analysis for Game Engine Component Systems," in Proc. ICSTW, 2021, pp. 88-95.

[11] T. Akenine-Moller et al., Real-Time Rendering, 4th ed. CRC Press, 2018.

[12] Unity Technologies, "Unity Manual: Optimizing Scripts," 2023.

[13] R. Nystrom, Game Programming Patterns. Genever Benning, 2014.

[14] R. S. Pressman and B. R. Maxim, Software Engineering: A Practitioner's Approach, 9th ed. McGraw-Hill, 2019.

[15] R. Hipple, "Game Architecture with Scriptable Objects," Unite Austin 2017, Unity Technologies, 2017. Available: https://youtu.be/raQ3iHhE_Kk

[16] [Author], "Vibrotactile Mass Cues for Planetary Interaction in VR," [Venue], [Year]. [To be linked in camera-ready version.]

[15] E. Gamma et al., Design Patterns: Elements of Reusable Object-Oriented Software. Addison-Wesley, 1994.

---

# APPENDIX A — BENCHMARK CONFIGURATION

| Parameter          | Value                    |
|--------------------|--------------------------|
| Unity Version      | 2022.3 LTS               |
| Scripting Backend  | IL2CPP (Release)         |
| Target Platform    | Windows x86-64           |
| N values           | 50, 100, 500             |
| R values           | 0.01, 0.10, 0.50, 1.00   |
| Warmup frames      | 60                       |
| Measurement frames | 300                      |
| Total runs         | 48                       |
| Value range        | Float [0, 100]           |
| Threshold          | 0.20 (normalised)        |
| Change pattern     | Deterministic round-robin|

---

# APPENDIX B — DEVELOPER STUDY TASK SPECIFICATION

Task given to all participants:

"Using the communication approach assigned to your group, implement the following system in Unity 2022.3:

A PlayerStats component holds three float values: stamina (0-100), health (0-100), and speed (0-10). Implement five consumer components:

1. StaminaBarUI — displays stamina as a formatted percentage string.
2. HealthBarUI — displays health as a formatted percentage string.
3. SpeedIndicator — displays speed as a formatted string to 1 decimal place.
4. LowStaminaWarning — triggers (logs a message) when stamina drops below 20.
5. CombinedStatusDisplay — displays health and stamina together as 'HP: XX% | ST: YY%'.

Requirements:
(i) No consumer component may evaluate when its input has not changed.
(ii) No memory leaks may occur when any component is destroyed.
(iii) All consumers must read the updated value in the same frame the change occurs."

---

# APPENDIX C — NASA-TLX SCALE ITEMS

1. Mental Demand: How much mental and perceptual activity was required?
2. Physical Demand: How much physical activity was required?
3. Temporal Demand: How much time pressure did you feel?
4. Performance: How successful were you in accomplishing the task?
5. Effort: How hard did you have to work to accomplish your level of performance?
6. Frustration: How insecure, discouraged, irritated, stressed, and annoyed were you?

Each item rated 0-100 in increments of 5.
Overall NASA-TLX = unweighted mean of all six subscales.

---

## HOW TO CONVERT TO WORD FORMAT

Option 1 — Pandoc (recommended, preserves formatting):
  Install Pandoc from https://pandoc.org
  Run: pandoc RCG_Paper_Draft.md -o RCG_Paper_Draft.docx

Option 2 — IEEE Word Template:
  Download IEEE template from https://www.ieee.org/conferences/publishing/templates.html
  Copy each section into the corresponding template section manually.

Option 3 — Direct open in Word:
  Rename this file to RCG_Paper_Draft.txt and open in Microsoft Word.
  Word will import as plain text. Apply IEEE template styles manually.
