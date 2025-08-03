using Microsoft.Xna.Framework.Content;

namespace MonoDreams.Examples.Component;

public class ContentProvider
{
    public ContentManager Content { get; }

    public ContentProvider(ContentManager content)
    {
        Content = content;
    }
}
