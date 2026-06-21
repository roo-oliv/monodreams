# Session Context

## User Prompts

### Prompt 1

MonoDreams currently only builds for desktop (Windows, MacOS, and Linux), the goal of this session is to enable targeting Web Browsers (we'll focus on Chrome latest). The chosen path is to use the KNI library. First, get yourself familiar with KNI:
 - https://github.com/kniengine/kni
 - https://darkgenesis.zenithmoon.com/monogame-on-the-web-No-really!.html
 - https://blog.nkast.gr/post/what-s-new-in-kni-v4-02
Second, plan on how to enable devs using MonoDreams to easily target the Web and have g...

### Prompt 2

Just check (by running and taking screenshots) the current state of LoadLevelExampleGameScreen. For most of the work, checking against MonoDreams.Demos will do.

### Prompt 3

Spaw agents to implement this in waves

### Prompt 4

Base directory for this skill: /Users/rodrigooliveira/.claude/skills/implement

`implement` executa um plano aprovado até virar um Pull Request aberto, e então encadeia o `/review-fix-loop`. A implementação roda dentro de um **Workflow** (`~/.claude/workflows/implement.js`) — esta invocação de skill é o opt-in explícito do usuário para a orquestração multi-agente, incluindo a autorização expressa para os agentes do workflow **commitarem e darem push na branch de trabalho** (soment...

