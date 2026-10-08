using System.Collections.Generic;
using System.Reflection;
using GameConsole = ScheduleOne.Console;

namespace S1Interop.CompilerSmoke;

internal sealed class ConsoleReflectionProbe
{
    private readonly FieldInfo? commandsField = typeof(GameConsole).GetField("commands", BindingFlags.NonPublic | BindingFlags.Static);

    internal Dictionary<string, GameConsole.ConsoleCommand>? Read()
    {
        // ConsoleForAll's Mono branch uses this cached lookup and negated pattern.
        if (commandsField?.GetValue(null) is not Dictionary<string, GameConsole.ConsoleCommand> commands)
            return null;
        return commands;
    }

    internal void Write(Dictionary<string, GameConsole.ConsoleCommand> commands) => commandsField!.SetValue(null, commands);
}
