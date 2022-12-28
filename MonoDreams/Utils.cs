namespace MonoDreams;

public static class Utils
{
    /// <summary>
    /// swaps the two object types
    /// </summary>
    /// <param name="first">First.</param>
    /// <param name="second">Second.</param>
    /// <typeparam name="T">The 1st type parameter.</typeparam>
    public static void Swap<T>(ref T first, ref T second)
    {
        T temp = first;
        first = second;
        second = temp;
    }
}
