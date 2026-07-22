# Session Context

## User Prompts

### Prompt 1

Base directory for this skill: /Users/rodrigooliveira/.claude/skills/implement

`implement` executa um plano aprovado até virar um Pull Request aberto, e então encadeia o `/review-fix-loop`. A implementação roda dentro de um **Workflow** (`~/.claude/workflows/implement.js`) — esta invocação de skill é o opt-in explícito do usuário para a orquestração multi-agente, incluindo a autorização expressa para os agentes do workflow **commitarem e darem push na branch de trabalho** (soment...

### Prompt 2

Base directory for this skill: /Users/rodrigooliveira/.claude/skills/refine

`refine` é a etapa de planejamento do pipeline `refine → implement → review-fix-loop`, mas funciona isoladamente. Ela recebe uma intenção em qualquer formato, resolve o conteúdo, decide se precisa entrevistar o usuário, escreve um plano com os artefatos de `.claude/rules/planning.md`, **decide sozinha** se o risco justifica rodar `/deep-plan`, e só então volta ao usuário para a aprovação final. O output é...

