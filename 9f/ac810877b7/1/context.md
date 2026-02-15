# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix Level Selection Screen — Buttons Invisible After CullingSystem Fix

## Context

The previous fix made `MasterRenderSystem` require `Visible` on all Main-target
entities. This correctly culls world sprites outside camera bounds, but it also
hides the level selection screen's buttons and title text — they target
`RenderTargetID.Main` but never receive the `Visible` component.

The level selection screen doesn't use `CullingSystem` (no camera-based
culling n...

### Prompt 2

Open a new Pull Request with all the changes currently made localy. Read them all to give a good but brief description for the Pull Request (I've gh installed, you can use it).

