using System.Reflection;
using UnityEngine;

namespace S1Interop.CompilerSmoke;

internal sealed class CachedAudioReflectionProbe
{
    // ConsoleForAll uses this private readonly descriptor pattern across methods.
    private readonly FieldInfo? callbacks;

    internal CachedAudioReflectionProbe()
    {
        callbacks = typeof(AudioSettings).GetField(nameof(AudioSettings.OnAudioConfigurationChanged),
            BindingFlags.NonPublic | BindingFlags.Static);
    }

    internal object? Read() => callbacks?.GetValue(null);
    internal void Write(object? value) => callbacks?.SetValue(null, value);
}
