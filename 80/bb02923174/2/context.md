# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Guard Debug Inspector with `#if DEBUG`

## Context

The debug inspector uses `System.Reflection` (`GetFields`, `GetProperties`, `FieldInfo.GetValue`, `PropertyInfo.GetValue`) in `ComponentIntrospector` and `DebugInspector` to introspect ECS components at runtime. This is incompatible with AOT compilation needed for console builds.

Since the inspector is a development-only tool, the simplest AOT-safe solution is to strip all inspector code from Release builds usi...

### Prompt 2

commit everything currently uncommited

