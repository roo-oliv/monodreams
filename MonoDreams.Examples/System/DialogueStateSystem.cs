using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

// public class DialogueStateSystem : AEntitySetSystem<GameState>
// {
//     private World _world;
//     
//     public DialogueStateSystem(World world)
//         : base(world.GetEntities().With<DialogueState>().With<DialogueComponent>().AsSet())
//     {
//         _world = world;
//         
//         // Subscribe to dialogue events
//         world.Subscribe<DialogueAdvanceMessage>(OnAdvanceDialogue);
//         world.Subscribe<DialogueChoiceMessage>(OnDialogueChoice);
//     }
//     
//     protected override void Update(GameState state, in Entity entity)
//     {
//         var dialogueState = entity.Get<DialogueState>();
//         
//         // Only process active dialogues
//         if (!dialogueState.IsActive) return;
//         
//         // Handle state transitions based on current node type
//         switch (dialogueState.CurrentNodeType)
//         {
//             case DialogueNodeType.Line:
//                 // Handle line nodes
//                 break;
//             case DialogueNodeType.Options:
//                 // Handle option nodes
//                 break;
//             case DialogueNodeType.Command:
//                 // Handle command nodes
//                 break;
//             case DialogueNodeType.End:
//                 // Handle end nodes
//                 dialogueState.IsActive = false;
//                 _world.Publish(new DialogueEndMessage(entity));
//                 break;
//         }
//     }
//     
//     private void OnAdvanceDialogue(in DialogueAdvanceMessage message)
//     {
//         // Advance dialogue to next node
//     }
//     
//     private void OnDialogueChoice(in DialogueChoiceMessage message)
//     {
//         // Process choice selection
//     }
// }
