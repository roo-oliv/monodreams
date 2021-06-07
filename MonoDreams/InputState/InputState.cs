namespace MonoDreams.InputState
{
    public class InputState
    {
        public bool Active { get; private set; }
        public bool JustActivated { get; private set; }
        public bool JustReleased { get; private set; }

        public void Update(bool active)
        {
            JustActivated = !Active && active;
            JustReleased = Active && !active;
            Active = active;
        }
    }
}