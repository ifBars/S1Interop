using Mono.Cecil;
using Mono.Cecil.Cil;

namespace S1Interop.Compiler;

/// <summary>Recognizes complete field-like event CAS loops before any reconstruction is proposed.</summary>
internal static class AtomicEventBody
{
    internal static bool Matches(MethodDefinition method, EventDefinition @event, FieldDefinition field)
    {
        if (method.IsStatic != field.IsStatic || field.IsInitOnly || field.IsLiteral ||
            field.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ThreadStaticAttribute") ||
            field.Name != @event.Name || field.DeclaringType != method.DeclaringType ||
            @event.DeclaringType != method.DeclaringType || !method.HasBody || method.Body.ExceptionHandlers.Count != 0 ||
            method.Parameters.Count != 1 || method.ReturnType.FullName != "System.Void" ||
            method.Parameters[0].ParameterType.FullName != @event.EventType.FullName ||
            field.FieldType.FullName != @event.EventType.FullName || method.Body.Variables.Count != 3 ||
            method.Body.Variables.Any(v => v.VariableType.FullName != @event.EventType.FullName)) return false;
        string? operation = @event.AddMethod == method ? "Combine" : @event.RemoveMethod == method ? "Remove" : null;
        if (operation is null) return false;
        var body = method.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        if (method.IsStatic)
        {
            if (body.Length != 18 || !Field(body[0], Code.Ldsfld, field) || !Field(body[9], Code.Ldsflda, field) ||
                body[5].OpCode.Code != Code.Ldarg_0) return false;
            // Normalize only the two proven static storage operations and callback argument.
            // Original loop instructions retain identity, so the retry target is still checked exactly.
            body = [Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, field),
                .. body[1..5], Instruction.Create(OpCodes.Ldarg_1), .. body[6..9],
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldflda, field), .. body[10..]];
        }
        if (body.Length != 20) return false;
        return body[0].OpCode.Code == Code.Ldarg_0 && Field(body[1], Code.Ldfld, field) &&
            Local(body[2], store: true, 0) && Local(body[3], store: false, 0) && Local(body[4], store: true, 1) &&
            Local(body[5], store: false, 1) && body[6].OpCode.Code == Code.Ldarg_1 &&
            body[7].OpCode.Code == Code.Call && body[7].Operand is MethodReference combine &&
            !combine.HasThis && combine.DeclaringType.FullName == "System.Delegate" && combine.Name == operation &&
            combine.ReturnType.FullName == "System.Delegate" && combine.Parameters.Count == 2 &&
            combine.Parameters.All(p => p.ParameterType.FullName == "System.Delegate") &&
            body[8].OpCode.Code == Code.Castclass && body[8].Operand is TypeReference cast && cast.FullName == field.FieldType.FullName &&
            Local(body[9], store: true, 2) && body[10].OpCode.Code == Code.Ldarg_0 && Field(body[11], Code.Ldflda, field) &&
            Local(body[12], store: false, 2) && Local(body[13], store: false, 1) &&
            body[14].OpCode.Code == Code.Call && CompareExchange(body[14].Operand, field.FieldType) &&
            Local(body[15], store: true, 0) && Local(body[16], store: false, 0) && Local(body[17], store: false, 1) &&
            body[18].OpCode.Code is Code.Bne_Un or Code.Bne_Un_S && body[18].Operand == body[3] && body[19].OpCode.Code == Code.Ret;
    }

    private static bool Field(Instruction instruction, Code code, FieldDefinition field) =>
        instruction.OpCode.Code == code && instruction.Operand is FieldReference reference && reference.FullName == field.FullName;

    private static bool CompareExchange(object operand, TypeReference fieldType) => operand is GenericInstanceMethod method &&
        !method.HasThis && method.DeclaringType.FullName == "System.Threading.Interlocked" && method.Name == "CompareExchange" &&
        method.GenericArguments.Count == 1 && method.GenericArguments[0].FullName == fieldType.FullName &&
        method.ReturnType is GenericParameter { Type: GenericParameterType.Method, Position: 0 } &&
        method.Parameters.Count == 3 &&
        method.Parameters[0].ParameterType is ByReferenceType { ElementType: GenericParameter { Type: GenericParameterType.Method, Position: 0 } } &&
        method.Parameters.Skip(1).All(p => p.ParameterType is GenericParameter { Type: GenericParameterType.Method, Position: 0 });

    private static bool Local(Instruction instruction, bool store, int index) => instruction.OpCode.Code switch
    {
        Code.Stloc_0 => store && index == 0,
        Code.Stloc_1 => store && index == 1,
        Code.Stloc_2 => store && index == 2,
        Code.Ldloc_0 => !store && index == 0,
        Code.Ldloc_1 => !store && index == 1,
        Code.Ldloc_2 => !store && index == 2,
        Code.Stloc or Code.Stloc_S => store && instruction.Operand is VariableDefinition variable && variable.Index == index,
        Code.Ldloc or Code.Ldloc_S => !store && instruction.Operand is VariableDefinition variable && variable.Index == index,
        _ => false
    };
}
