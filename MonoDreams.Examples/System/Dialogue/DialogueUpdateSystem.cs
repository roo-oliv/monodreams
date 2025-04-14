using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.State;
using MonoDreams.Examples.Component.Dialogue;

namespace MonoDreams.Examples.System.Dialogue // Or your appropriate systems namespace
{
    /// <summary>
    /// Updates the dynamic state of the Dialogue UI, such as text reveal progress
    /// and indicator visibility.
    /// </summary>
    [With(typeof(DialogueUIStateComponent))] // Query for entities with the state component
    public sealed class DialogueUpdateSystem : AEntitySetSystem<GameState>
    {
        public DialogueUpdateSystem(World world)
        : base(world) // Run sequentially by default
        { }

        protected override void Update(GameState state, in Entity entity)
        {
            // Get component by reference to modify it
            ref var uiState = ref entity.Get<DialogueUIStateComponent>();

            // Only update if the UI is active
            if (!uiState.IsActive)
            {
                // Ensure reveal state is reset if UI becomes inactive? Optional.
                // uiState.IsRevealed = false;
                // uiState.VisibleCharacterCount = 0;
                return;
            }

            // --- Update Text Reveal State ---
            int maxChars = uiState.CurrentText?.Length ?? 0;

            // Check if already revealed or instant reveal speed
            if (uiState.IsTextFullyRevealed || uiState.TextRevealingSpeed <= 0)
            {
                uiState.VisibleCharacterCount = maxChars;
                uiState.IsTextFullyRevealed = true; // Ensure flag is set
            }
            else
            {
                // Initialize start time if it's marked as invalid (e.g., after text change)
                if (float.IsNaN(uiState.TextRevealStartTime))
                {
                    uiState.TextRevealStartTime = state.TotalTime; // Assuming GameState has TotalTime in seconds
                }

                float elapsedTime = state.TotalTime - uiState.TextRevealStartTime;
                int targetVisibleChars = (int)Math.Floor(elapsedTime * uiState.TextRevealingSpeed);
                uiState.VisibleCharacterCount = Math.Max(0, Math.Min(targetVisibleChars, maxChars));
                uiState.IsTextFullyRevealed = (uiState.VisibleCharacterCount >= maxChars);
            }

            // --- Update Next Indicator Visibility ---
            // Basic logic: Show indicator when text is fully revealed.
            // TODO: Refine this logic later based on dialogue runner state (e.g., waiting for input).
            uiState.ShowNextIndicator = uiState.IsTextFullyRevealed;

            // --- Update Indicator Animation State (Future Task) ---
            // if (uiState.ShowNextIndicator) { /* Update bobble offset based on state.TotalTime */ }
        }
    }
}