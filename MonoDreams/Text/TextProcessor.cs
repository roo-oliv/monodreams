using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Text;

public static class TextProcessor
{
    public static List<(BitmapFontGlyph glyph, Color color)> GetGlyphs(DynamicText text, Vector2 origin)
    {
        var result = new List<(BitmapFontGlyph glyph, Color color)>();
        var colorStack = new Stack<Color>();
        colorStack.Push(TextColor.DefaultGray);

        // var bbCodeRegex = new Regex(@"\[(?<tag>/?\w+)(?:=(?<param>\w+))?\]");
        // var matches = bbCodeRegex.Matches(text.Value);

        var currentIndex = 0;
        var lineWidth = 0f;
        var discount = Vector2.Zero;

        // foreach (Match match in matches)
        // {
        //     var tag = match.Groups["tag"].Value;
        //     var param = match.Groups["param"].Value;
        //
        //     var substring = text.Value.Substring(currentIndex, match.Index - currentIndex);
        //     foreach (var glyph in text.Font.GetGlyphs(substring, origin))
        //     {
        //         var bitmapFontGlyph = glyph;
        //         bitmapFontGlyph.Position.X -= xDiscount;
        //         if (text.MaxLineWidth > 0 && lineWidth + bitmapFontGlyph.FontRegion.Width > text.MaxLineWidth)
        //         {
        //             bitmapFontGlyph.Position.Y += text.Font.LineHeight * 2;
        //             xDiscount += lineWidth;
        //             lineWidth = 0;
        //         }
        //
        //         result.Add((bitmapFontGlyph, colorStack.Peek()));
        //         if (bitmapFontGlyph.FontRegion != null)
        //         {
        //             lineWidth += bitmapFontGlyph.FontRegion.XAdvance;
        //         }
        //     }
        //
        //     switch (tag)
        //     {
        //         case "color":
        //             colorStack.Push(TextColor.Parse(param));
        //             break;
        //         case "/color":
        //             if (colorStack.Count > 1)
        //             {
        //                 colorStack.Pop();
        //             }
        //             break;
        //     }
        //
        //     currentIndex = match.Index + match.Length;
        // }

        string remaining = text.Value[currentIndex..];
        foreach (var glyph in text.Font.GetGlyphs(remaining, origin))
        {
            var bitmapFontGlyph = glyph;
            if (text.MaxLineWidth > 0 && lineWidth + bitmapFontGlyph.FontRegion.Width > text.MaxLineWidth)
            {
                discount += new Vector2(lineWidth, -text.Font.LineHeight);
                lineWidth = 0;
            }
            bitmapFontGlyph.Position -= discount;

            result.Add((bitmapFontGlyph, colorStack.Peek()));
            if (bitmapFontGlyph.FontRegion != null)
            {
                lineWidth += bitmapFontGlyph.FontRegion.XAdvance;
            }
        }

        return result;
    }
    
    public static List<(BitmapFontGlyph glyph, Color color)> GetGlyphs(DynamicText text, Vector2 origin, float maxWidth)
    {
        var position = new Vector2(origin.X, origin.Y);
        var result = new List<(BitmapFontGlyph glyph, Color color)>();
        var colorStack = new Stack<Color>();
        colorStack.Push(TextColor.DefaultGray);

        // var bbCodeRegex = new Regex(@"\[(?<tag>/?\w+)(?:=(?<param>\w+))?\]");
        // var matches = bbCodeRegex.Matches(text.Value);

        var currentIndex = 0;
        var lineWidth = 0f;
        // foreach (Match match in matches)
        // {
        //     var tag = match.Groups["tag"].Value;
        //     var param = match.Groups["param"].Value;
        //
        //     var substring = text.Value.Substring(currentIndex, match.Index - currentIndex);
        //     var processedSubstring = bbCodeRegex.Replace(substring, "");
        //     var size = text.Font.MeasureString(processedSubstring);
        //
        //     if (lineWidth + size.Width > maxWidth)
        //     {
        //         position.Y += text.Font.LineHeight;
        //         position.X = origin.X;
        //         lineWidth = 0;
        //     }
        //
        //     foreach (var glyph in text.Font.GetGlyphs(processedSubstring, position))
        //     {
        //         result.Add((glyph, colorStack.Peek()));
        //         if (glyph.FontRegion != null)
        //         {
        //             position += new Vector2(glyph.FontRegion.XAdvance, 0);
        //             lineWidth += glyph.FontRegion.XAdvance;
        //         }
        //     }
        //
        //     switch (tag)
        //     {
        //         case "color":
        //             colorStack.Push(TextColor.Parse(param));
        //             break;
        //         case "/color":
        //         {
        //             if (colorStack.Count > 1)
        //             {
        //                 colorStack.Pop();
        //             }
        //
        //             break;
        //         }
        //     }
        //
        //     currentIndex = match.Index + match.Length;
        // }

        string remaining = text.Value[currentIndex..];
        // var processedRemaining = bbCodeRegex.Replace(remaining, "");
        var remainingSize = text.Font.MeasureString(remaining);

        if (lineWidth + remainingSize.Width > maxWidth)
        {
            position.Y += text.Font.LineHeight;
            position.X = origin.X;
        }

        foreach (var glyph in text.Font.GetGlyphs(remaining, position))
        {
            result.Add((glyph, colorStack.Peek()));
        }

        return result;
    }
}