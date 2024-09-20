using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.LegacyComponents;
using Newtonsoft.Json;

namespace MonoDreams.Sprite;

public class FrameData
{
    public Rectangle frame { get; set; }
    public int duration { get; set; }
}

public class AnimationData
{
    public Dictionary<string, FrameData> frames { get; set; }
    
    public static AnimationData LoadAnimationData(string path)
    {
        var jsonContent = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<AnimationData>(jsonContent);
    }

    public void PopulateEntityAnimation(Entity entity, Texture2D spriteSheet)
    {
        var entityFrames = new List<Rectangle>();
        var durations = new List<int>();

        foreach (var frameData in frames.Values)
        {
            entityFrames.Add(frameData.frame);
            durations.Add(frameData.duration);
        }

        entity.Set(new DrawInfo(spriteSheet: spriteSheet, source: entityFrames[0], color: Color.White));

        entity.Set(new Animation
        {
            FrameDuration = (float)durations.Average() / 1000f,
            CurrentFrame = 0,
            TotalFrames = entityFrames.Count
        });
    }
}
