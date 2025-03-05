namespace MonoDreams.YarnSpinner;

public class YarnTranslation
{
    public YarnTranslation(string languageName, string text = null)
    {
        LanguageName = languageName;
        Text = text;
    }

    public string LanguageName;
    public string Text;
}
