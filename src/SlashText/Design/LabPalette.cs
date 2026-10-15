using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SlashText.Design;

public static class LabPalette
{
    /// <summary>Only changes Lab.* brushes, leaving the 3.2 palette untouched.</summary>
    public static void Apply(ResourceDictionary target, bool dark, bool animate = false)
    {
        var source = new ResourceDictionary
        {
            Source = new Uri($"/{typeof(LabPalette).Assembly.GetName().Name};component/Styles/VisualLab/{(dark ? "Black" : "Light")}.xaml", UriKind.Relative)
        };
        foreach (string key in source.Keys)
        {
            var next = ((SolidColorBrush)source[key]).Color;
            var old = target.Contains(key) ? target[key] as SolidColorBrush : null;
            var brush = new SolidColorBrush(next);
            if (animate && SystemParameters.ClientAreaAnimation && old is not null)
            {
                var frames = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200), FillBehavior = FillBehavior.Stop };
                frames.KeyFrames.Add(new DiscreteColorKeyFrame(old.Color, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                frames.KeyFrames.Add(new SplineColorKeyFrame(next, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new KeySpline(.25,.1,.25,1)));
                // Start the clock before publishing the brush. Once a Freezable is
                // exposed by a ResourceDictionary, WPF may freeze it while resolving
                // DynamicResource references, which makes BeginAnimation throw on an
                // interactive theme change.
                brush.BeginAnimation(SolidColorBrush.ColorProperty, frames);
            }
            target[key] = brush;
        }
    }
}
