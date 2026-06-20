# Session Context

## User Prompts

### Prompt 1

Let's enhance the physics module's demo:
 1. Make the balls half their current sizes.
 2. Make the headline explanatory text way shorter, we only need to explain the very core, not everything.
 3. Make the two balls colliding to blink brighter colors, independent on who's colliding with whom and their speeds.
 4. Make the balls lose a little bit less energy when colliding.
 5. Make the input for how many blue and red balls there are to accept up to 999999.
 6. Make the blue balls hollow, just pa...

### Prompt 2

Starting on approx. 360 red balls the demo starts stuttering and hanging, are there good optimizations we can do for the collision detection and resolution systems that will allow handling more collisions and colliders? (if there are only speculative and dimishing returns optimizations, don't implement anything)

### Prompt 3

This session is being continued from a previous conversation that ran out of context. The summary below covers the earlier portion of the conversation.

Summary:
1. Primary Request and Intent:
   - **First request (physics demo enhancements)** — six explicit changes to the physics module demo:
     1. Make the balls half their current sizes.
     2. Make the headline explanatory text way shorter — explain only the very core, not everything.
     3. Make two balls colliding blink brighter col...

### Prompt 4

Playing with the physics module demo I've noticed that the balls doesn't sit still and reach equilibrium/stableness under gravity on and floor boost off, this is unexpected, it seems that they're processing infinetely small collisions and constantly shaking and overlapping each other when using a high number of balls. Why is this and is there a graceful/nice way to deal with this? [Image #1]

### Prompt 5

[Image: source: /Users/rodrigooliveira/Desktop/Captura de Tela 2026-06-15 às 15.06.00.png]

### Prompt 6

Let's proceed with your recommendation then, implement it

### Prompt 7

This is great, but there is one thing left to investigate: the balls keep overlapping a little with each other, specially as they come to resting state, instead of only sitting tangent to each other.

### Prompt 8

That's better, but there still uneasiness when the balls are supposedtly resting: they stop blinking bright so I supposed collisions are being ignored, but they stull overlap a little with each other and keeps changing slightly from one position to another, back and forfth, instead of staying still. Also, increase from 16 to 32 sides the collider.

### Prompt 9

Continue

### Prompt 10

There is a weird thing that happens now, as a red ball slows down it starts stop reacting to being hit by other balls, effetively becoming completely still, even when hit by a rapid moving ball. It should instead absorb part of the other ball's energy and go in the appropriate vector resulting from the collision.

