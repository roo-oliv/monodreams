# Session Context

## User Prompts

### Prompt 1

I think monodreams is getting mature enough to split things and keep them organized as packages. Help me do that by defining which packages we'll provide. I've just quickly thought of having a dedicated package for 'rendering' (this would include the rendering systems, DrawComponent, Drawable, mesh vs Sprite), another for 'transform', another for 'camera', another for 'collision' (or would be two? SAT collision and AABB collision?), another for 'text' (or two? one for static text and another for...

### Prompt 2

Setup tasks so you promote and then create the registry and the monodreams CLI dotnet tool. We'll test the whole thing in this branch.

### Prompt 3

Kick it off

### Prompt 4

[Request interrupted by user]

### Prompt 5

This session is being continued from a previous conversation that ran out of context. The summary below covers the earlier portion of the conversation.

Summary:
1. Primary Request and Intent:
   The user is building MonoDreams, an opensource 2D game engine on MonoGame + DefaultEcs ECS. They want to convert it to a **shadcn-style code distribution model** — users own copied source via a CLI rather than referencing opaque NuGet packages — to maximize AI-agent visibility/editability of the cod...

### Prompt 6

Carry on with the planned tasks

### Prompt 7

Now, just for me to be sure about this architecture, just discuss with/explain to me: why registry's block.json stays in a separate folder from each block source code? At a first glance I fear drift: the source code start to stay out of sync with the actual files and folders in MonoDreams/, wouldn't a more explicit blocks division in the source code and accompaining block.json file make this tighter and easily organizable?

### Prompt 8

Do it now

### Prompt 9

1. Let's apply some naming conventions, systems I believe all already follow this: suffix file/class names with *System. The same for components: *Component (most components don't follow this), this way just by reading the files from some block you know at a glance which systems and components it has.
2. Update the root README.md, or even just rewrite it, the focus should now be the end user (the gamedev), and we can have a separate .md file for those who want to contribute to the project instru...

