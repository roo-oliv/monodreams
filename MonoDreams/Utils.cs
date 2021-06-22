namespace MonoDreams
{
    public static class Utils
    {
        public static void Swap<T> (ref T lhs, ref T rhs) {
            var temp = lhs;
            lhs = rhs;
            rhs = temp;
        }
    }
}