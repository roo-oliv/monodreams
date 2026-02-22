# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Entity Hierarchy: `ChildOf` Component

## Context

The debug inspector shows all entities as a flat list. Entities that logically belong to others (NPC's dialogue zone, NPC's interaction icon, Player's orbs) should display as a tree. Additionally, destroying a parent entity should cascade to its children. Currently, `Transform.Parent` establishes spatial hierarchy at the Transform level but is invisible at the entity level — there's no way to query "children of...

### Prompt 2

commit this

