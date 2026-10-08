using System;
using System.Reflection;
using HarmonyLib;
using ScheduleOne.Vehicles;
using GameConsole = ScheduleOne.Console;

namespace S1Interop.CompilerSmoke;

internal static class DynamicFieldProbe
{
    internal static T ReadConsole<T>(string name)
    {
        var field = typeof(GameConsole).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(name);
        return (T)field.GetValue(null)!;
    }

    internal static void WriteConsole<T>(string name, T value)
    {
        var field = typeof(GameConsole).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(name);
        field.SetValue(null, value);
    }

    // Same generic lookup shape as S1-Forklift's Mono LandVehicleAccess helpers.
    internal static T ReadVehicle<T>(LandVehicle vehicle, string name)
    {
        var field = AccessTools.Field(typeof(LandVehicle), name)
            ?? throw new MissingFieldException(name);
        return (T)field.GetValue(vehicle)!;
    }

    internal static void WriteVehicle<T>(LandVehicle vehicle, string name, T value)
    {
        var field = AccessTools.Field(typeof(LandVehicle), name)
            ?? throw new MissingFieldException(name);
        field.SetValue(vehicle, value);
    }
}
