# Session Context

## User Prompts

### Prompt 1

# Bootstrap AI-Docs and `deep-review` skill for a new repository

Use this prompt to bootstrap, in a previously undocumented codebase, a documentation + review-skill stack that captures the system's **business invariants** and **technical premises** in a form future AI agents (and humans) can load as context. The end goal is for a `/deep-review` slash-command in the target repo to produce findings well above a generic review.

Token spend is not a constraint here. Depth is. The output is high-le...

### Prompt 2

1. The experience aimed is for full control via code, unlike Unity and Unreal where your code has to conform to the engine and can only be placed in restrict specific places, Monodreams aims to be the same as Spring is for web developers, you're in control but we've got you covered with most of what's common need for most games, the price is that the developer is expected to embrace ECS architecture. Today, devs wanting control only have bare MonoGame and have to do the heavy lifting themselves ...

### Prompt 3

5. The dev will be free to create their own Transform custom component if they wish/need, what I fear most is inconsistency, the dev creates a MyTransform and start using both, Transform and MyTransform, this will lead to a complicated code, hard do debug problems, and a lot of duplication. Systems on the other hand should tend to be more agnostic, like if they were saying: if a component is in such conditions, I do this and that, no matter what. So you're isolated and, of course this can lead t...

### Prompt 4

11. I haven't stumbled on this problem yet, so I don't have an opinion.
12. I don't know, as of today, all children should be disposed alongside their parent, but nothing prevents that we add support for children outliving their parents if needed.
13. Good question, today the engine assumes one collider of the same type per entity. But this is just something that wasn't given too much thought about.
14. I haven't experienced flickering but this is a recent implementation. I remember implementing...

### Prompt 5

17. nuance: the system just renders at Transform.WorldPosition, whatever it is. If you want HierarchySystem to run before rendering (most of the cases you do), then yes. But the rendering pipeline doesn't cares about HierarchySystem or whatever system, it's a little like functional programming, it performs an operation on an input, it doesn't assumes anything about the input. This is true for the concept of ECS, so to all Systems. Of course, a dev can code a System that heavily assumes other sys...

### Prompt 6

25. Yes, it confuses me. The problema I was trying to solve is how to handle complex UI elements like a dialogue UI that has the background banner, the avatars on each end, the text, and the visual indication of line finished and dialogue in a state waiting on input to advance to the next dialogue line. I just wanted to handle that in a simple and elegant manner.
26. Reorganizing is in scope, it's a little bit messy. What I want actually is to have those divided into packs/modules, just like Spr...

### Prompt 7

30. Let's go with all of your suggestions.
31. "Added a super specialized component/system that solves one specific pain point right now but doesn't evolve well in the framework to deliver value for more implementations and play nice with other features in MonoDreams"
32. No
33. Worth it, include this option

### Prompt 8

Right

### Prompt 9

- docs/hierarchy-transform/premises.md — all good
- docs/rendering/premises.md — all good
- docs/collision/premises.md — all good (Tunneling at high velocity: the only prevention is keeping the velocity and collider sizes reasonable. this is a known gap/limitation.)
- docs/physics/premises.md — all good (High-speed velocity caps: no caps exist; Gravity per-entity scaling: per-entity gravity scaling; `RigidBody.Mass`: only that one use but nothing prevents other uses)
- docs/level-loading...

### Prompt 10

Continue from where you left off.

### Prompt 11

Os agentes serão instruídos a ler a documentação antes de qualquer implementação? Não adianta isto ser lido apenas pelo skill deep-review

