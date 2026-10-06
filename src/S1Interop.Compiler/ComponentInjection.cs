using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>
/// Lowers supported author inheritance chains rooted at <c>UnityEngine.MonoBehaviour</c> into Il2CppInterop injected
/// components: MelonLoader registration attribute, the native wrapping <c>IntPtr</c> constructor, the
/// <c>DerivedConstructorPointer</c>/<c>DerivedConstructorBody</c> managed constructor, and <c>HideFromIl2Cpp</c> on
/// managed-only members. Every other native-derived shape is rejected with a precise error instead of being emitted.
/// </summary>
internal static class ComponentInjection
{
    private const string RegisterAttributeName = "MelonLoader.RegisterTypeInIl2Cpp";
    private const string ClassInjectorName = "Il2CppInterop.Runtime.Injection.ClassInjector";
    private const string HideAttributeName = "Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute";
    private const string MonoBehaviourName = "UnityEngine.MonoBehaviour";

    private const string RegisterAttribute = "global::" + RegisterAttributeName;
    private const string ClassInjector = "global::" + ClassInjectorName;
    private const string HideAttribute = "global::Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp";

    // Messages Unity dispatches by name; hiding them from IL2CPP would silently stop them from running.
    private static readonly ImmutableHashSet<string> UnityMessages = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Awake", "Start", "Update", "FixedUpdate", "LateUpdate", "OnEnable", "OnDisable", "OnDestroy", "Reset",
        "OnValidate", "OnGUI", "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
        "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit", "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
        "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
        "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D", "OnControllerColliderHit",
        "OnBecameVisible", "OnBecameInvisible", "OnWillRenderObject", "OnPreCull", "OnPreRender", "OnPostRender",
        "OnRenderObject", "OnRenderImage", "OnDrawGizmos", "OnDrawGizmosSelected", "OnAnimatorMove", "OnAnimatorIK",
        "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton", "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag",
        "OnTransformParentChanged", "OnTransformChildrenChanged", "OnRectTransformDimensionsChange",
        "OnCanvasGroupChanged", "OnParticleCollision", "OnParticleTrigger", "OnJointBreak", "OnJointBreak2D",
        "OnAudioFilterRead");

    public static readonly DiagnosticDescriptor UnsupportedShape = Error(
        "S1IC011",
        "Native subclass shape is not injectable",
        "'{0}' derives from native type '{1}' but cannot be injected: {2}");

    public static readonly DiagnosticDescriptor UnsupportedConstructor = Error(
        "S1IC012",
        "Constructor cannot be lowered for IL2CPP injection",
        "Constructor '{0}' cannot be lowered for IL2CPP injection: {1}");

    public static readonly DiagnosticDescriptor UnsupportedCallbackSignature = Error(
        "S1IC013",
        "Unity message signature is not IL2CPP-representable",
        "Unity message '{0}' uses '{1}', which IL2CPP class injection cannot expose; Unity would never call it");

    public static readonly DiagnosticDescriptor MissingInjectionSupport = Error(
        "S1IC014",
        "Target references lack IL2CPP injection support",
        "'{0}' cannot be injected because the target references do not define '{1}'");

    private static readonly DiagnosticDescriptor SerializedField = Error("S1IC015", "Injected field serialization shape is not supported",
        "Field '{0}' requests Unity serialization with a shape not supported by native field lowering");
    private static readonly DiagnosticDescriptor PublicField = new("S1IC016", "Injected public field is managed state",
        "Public field '{0}' remains managed state on IL2CPP. Unity serialization is not generated for this field; mark intentional runtime-only state NonSerialized.",
        "S1Interop.Compiler", DiagnosticSeverity.Warning, true);

    /// <summary>
    /// Rewrites one (possibly partial) declaration of a native-derived author class. <paramref name="visited"/> must
    /// be the already lowered form of <paramref name="original"/> with the same member order. Returns
    /// <paramref name="visited"/> unchanged when the class is not native-derived or when an error was reported.
    /// </summary>
    internal static ClassDeclarationSyntax Rewrite(
        ClassDeclarationSyntax original,
        ClassDeclarationSyntax visited,
        SemanticModel model,
        MetadataSymbolMap map,
        ComponentFieldLowering fields,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        out int changes)
    {
        changes = 0;
        if (model.GetDeclaredSymbol(original) is not INamedTypeSymbol type ||
            type.BaseType is not { } baseType ||
            !map.IsNative(baseType))
        {
            return visited;
        }

        bool isPrimary = IsPrimaryDeclaration(type, original);
        Location location = original.Identifier.GetLocation();
        if (isPrimary)
        {
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic && !field.IsReadOnly))
            {
                // Lowered fields become native serialized storage, so the managed-state diagnostics no longer apply.
                if (fields.IsLowered(field)) continue;
                string?[] attributes = field.GetAttributes().Select(attribute => attribute.AttributeClass?.ToDisplayString()).ToArray();
                if (attributes.Contains("System.NonSerializedAttribute")) continue;
                if (attributes.Any(attribute => attribute is "UnityEngine.SerializeField" or "UnityEngine.SerializeReference"))
                    diagnostics.Add(Diagnostic.Create(SerializedField, field.Locations.FirstOrDefault() ?? location, field.Name));
                else if (field.DeclaredAccessibility == Accessibility.Public && IsUnitySerializable(field.Type))
                    diagnostics.Add(Diagnostic.Create(PublicField, field.Locations.FirstOrDefault() ?? location, field.Name));
            }
        }

        if (RejectShape(type, baseType, map) is { } reason)
        {
            if (isPrimary)
            {
                diagnostics.Add(Diagnostic.Create(UnsupportedShape, location, type.Name, baseType.ToDisplayString(), reason));
            }

            return visited;
        }

        foreach (string required in new[] { RegisterAttributeName, ClassInjectorName, HideAttributeName })
        {
            if (map.FindTargetType(required) is null)
            {
                if (isPrimary)
                {
                    diagnostics.Add(Diagnostic.Create(MissingInjectionSupport, location, type.Name, required));
                }

                return visited;
            }
        }

        if (original.Members.Count != visited.Members.Count)
        {
            throw new InvalidOperationException(
                $"Lowering changed the member count of '{type.Name}'; component injection needs member order preserved.");
        }

        // Validate every authored instance constructor across parts before emitting anything, so no part emits half
        // an injection. Each rejected constructor is reported only by the part that declares it.
        IMethodSymbol? authoredDefault = null;
        bool hasPointerConstructor = false;
        bool constructorsValid = true;
        foreach (IMethodSymbol constructor in type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared))
        {
            if (IsPointerConstructor(constructor))
            {
                hasPointerConstructor = true;
                continue;
            }

            if (constructor.Parameters.Length == 0)
            {
                authoredDefault = constructor;
            }

            if (constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax syntax &&
                RejectConstructor(syntax) is { } constructorReason)
            {
                constructorsValid = false;
                if (DeclaredIn(constructor, original))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedConstructor, syntax.Identifier.GetLocation(), constructor.ToDisplayString(), constructorReason));
                }
            }
        }

        if (!constructorsValid)
        {
            return visited;
        }

        // Blocked serialized fields stay unlowered and keep their existing diagnostics; the blocker explains why.
        fields.ReportBlockers(type, original);

        // Non-empty only when every serialized scalar field of this type (across all parts) is lowered.
        ImmutableArray<string> fieldInitialization = fields.InitializationStatements(type);
        string[] registration = RegistrationStatements(type, map).ToArray();

        // The IntPtr constructor carries the authored default body, so it lives in the part that declares that body.
        bool hostsConstructors = authoredDefault is null ? isPrimary : DeclaredIn(authoredDefault, original);
        bool alreadyInjected = authoredDefault is not null && CallsDerivedConstructorPointer(authoredDefault);

        var members = new List<MemberDeclarationSyntax>(visited.Members.Count + 2);
        BlockSyntax? authoredBody = null;
        for (int i = 0; i < visited.Members.Count; i++)
        {
            MemberDeclarationSyntax lowered = visited.Members[i];
            MemberDeclarationSyntax source = original.Members[i];

            if (lowered is ConstructorDeclarationSyntax staticConstructor &&
                staticConstructor.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                // Il2CppClassPointerStore<T> runs the static constructor before reading the native class, so
                // registration (and the HasReferences flag) happens before the first native allocation.
                BlockSyntax staticBody = ToBlock(staticConstructor);
                lowered = staticConstructor.WithBody(staticBody.WithStatements(staticBody.Statements.InsertRange(0,
                    registration.Select(statement => ParseStatement(statement).WithTrailingTrivia(Space)))))
                    .WithExpressionBody(null).WithSemicolonToken(default);
                changes++;
            }
            else if (lowered is ConstructorDeclarationSyntax constructor &&
                !constructor.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                constructor.ParameterList.Parameters.Count == 0)
            {
                if (!alreadyInjected)
                {
                    authoredBody = ToBlock(constructor);
                    lowered = InjectDefaultConstructor(constructor, type.Name, authoredBody, fieldInitialization);
                    changes++;
                }
            }
            else if (!fieldInitialization.IsEmpty && lowered is ConstructorDeclarationSyntax pointerConstructor &&
                !pointerConstructor.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                model.GetDeclaredSymbol(source) is IMethodSymbol pointerSymbol && IsPointerConstructor(pointerSymbol))
            {
                lowered = PrependInitialization(pointerConstructor, fieldInitialization);
                changes++;
            }
            else if (source is MethodDeclarationSyntax sourceMethod && lowered is MethodDeclarationSyntax loweredMethod &&
                model.GetDeclaredSymbol(sourceMethod) is { } methodSymbol && ManagedCoroutineName(methodSymbol, map) is { } managedName)
            {
                var managedMethod = PrependAttribute(loweredMethod.WithIdentifier(Identifier(managedName).WithTriviaFrom(loweredMethod.Identifier)), HideList());
                members.Add(PrependAttribute(managedMethod, AttributeList(SingletonSeparatedList(
                    Attribute(ParseName("global::System.Runtime.CompilerServices.CompilerGenerated"))))));
                lowered = loweredMethod.WithAttributeLists(default)
                    .WithModifiers(TokenList(loweredMethod.Modifiers.Where(token => token.IsKind(SyntaxKind.PublicKeyword) ||
                        token.IsKind(SyntaxKind.PrivateKeyword) || token.IsKind(SyntaxKind.ProtectedKeyword) || token.IsKind(SyntaxKind.InternalKeyword))))
                    .WithReturnType(ParseTypeName("global::" + CoroutineSupportSource.NativeEnumerator).WithTriviaFrom(loweredMethod.ReturnType))
                    .WithBody(null)
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression(CoroutineSupportSource.HelperType + ".Wrap(" + managedName + "())")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)).NormalizeWhitespace();
                if (methodSymbol.OverriddenMethod is not null)
                    lowered = ((MethodDeclarationSyntax)lowered).AddModifiers(Token(SyntaxKind.NewKeyword).WithTrailingTrivia(Space));
                if (type.IsSealed && loweredMethod.Modifiers.Any(SyntaxKind.ProtectedKeyword))
                    lowered = ((MethodDeclarationSyntax)lowered).WithModifiers(TokenList(
                        Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(Space),
                        Token(SyntaxKind.NewKeyword).WithTrailingTrivia(Space)));
                changes++;
            }
            else if (HideIfManagedOnly(source, lowered, model, map, type, diagnostics) is { } hidden)
            {
                lowered = hidden;
                changes++;
            }

            members.Add(lowered);
        }

        if (hostsConstructors)
        {
            string indent = MemberIndent(visited);
            int insertAt = 0;
            if (!hasPointerConstructor)
            {
                // Native creation (AddComponent, scene load) enters through this constructor, so it must run the
                // authored construction body; field initializers are emitted into it by the C# compiler as well.
                // Lowered field storage is initialized first, so authored code (and virtual calls) see authored values.
                BlockSyntax pointerBody = (authoredBody ?? Block()).WithoutTrivia();
                pointerBody = pointerBody.WithStatements(pointerBody.Statements.InsertRange(0, InitializationStatements(fieldInitialization)));
                members.Insert(insertAt++, ParseMember(
                    $"public {type.Name}(global::System.IntPtr pointer) : base(pointer) {pointerBody.ToFullString()}", indent));
                changes++;
            }

            if (authoredDefault is null)
            {
                members.Insert(insertAt, ParseMember(
                    $"{(type.IsAbstract ? "protected" : "public")} {type.Name}() : base({ClassInjector}.DerivedConstructorPointer<{type.Name}>()) " +
                    $"{{ {ClassInjector}.DerivedConstructorBody(this); {string.Join(" ", fieldInitialization)} }}",
                    indent));
                changes++;
            }
        }

        // Every injected class registers itself (after its injected ancestors) and flags its native class before any
        // allocation; the loader does not inherit those flags from the base class.
        if (isPrimary && !type.StaticConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
        {
            members.Insert(0, ParseMember($"static {type.Name}() {{ {string.Join(" ", registration)} }}", MemberIndent(visited)));
            changes++;
        }

        ClassDeclarationSyntax result = visited.WithMembers(List(members));
        if (isPrimary && !HasRegisterAttribute(type))
        {
            result = PrependAttribute(result, AttributeList(SingletonSeparatedList(Attribute(ParseName(RegisterAttribute)))));
            changes++;
        }

        return result;
    }

    internal static string? ManagedCoroutineName(IMethodSymbol method, MetadataSymbolMap map)
    {
        if (map.FindTargetType(CoroutineSupportSource.AdapterType) is null ||
            method.Name != "Start" || method.IsStatic || method.IsGenericMethod || method.Parameters.Length != 0 ||
            method.IsPartialDefinition || method.PartialDefinitionPart is not null ||
            method.ReturnType.ToDisplayString() != "System.Collections.IEnumerator" ||
            !HasSupportedComponentBase(method.ContainingType.BaseType, map) ||
            method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == HideAttributeName)) return null;
        if (!map.IsAuthorType(method.ContainingType))
        {
            var target = map.Resolve(method.ContainingType).Target;
            var helpers = target?.GetMembers().OfType<IMethodSymbol>().Where(candidate =>
                candidate.Name.StartsWith("__S1InteropManagedStart", StringComparison.Ordinal) &&
                !candidate.IsStatic && !candidate.IsGenericMethod && candidate.Parameters.Length == 0 &&
                candidate.ReturnType.ToDisplayString() == "System.Collections.IEnumerator" &&
                candidate.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == HideAttributeName) &&
                candidate.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
                    "System.Runtime.CompilerServices.CompilerGeneratedAttribute")).ToArray();
            return helpers is { Length: 1 } ? helpers[0].Name : null;
        }
        if (method.OverriddenMethod is { } overridden && ManagedCoroutineName(overridden, map) is { } inheritedName)
            return inheritedName;
        string name = "__S1InteropManagedStart";
        int suffix = 0;
        while (method.ContainingType.GetMembers(name).Length != 0) name = "__S1InteropManagedStart" + ++suffix;
        return name;
    }

    private static string? RejectShape(INamedTypeSymbol type, INamedTypeSymbol baseType, MetadataSymbolMap map)
    {
        if (type.IsGenericType)
        {
            return "generic classes are not injected";
        }

        if (type.ContainingType is not null)
        {
            return "nested classes are not injected";
        }

        if (type.IsStatic)
        {
            return "static classes are not injected";
        }

        if (!HasSupportedComponentBase(baseType, map))
        {
            return "the base chain must reach MonoBehaviour through author components or compiler-built component libraries";
        }

        if (type.Interfaces.FirstOrDefault(i => !map.IsAuthorType(i) && map.Resolve(i).Status == TypeMappingStatus.Mapped)
            is { } nativeInterface)
        {
            return $"native interface '{nativeInterface.ToDisplayString()}' requires RegisterTypeInIl2CppWithInterfaces, which is not lowered yet";
        }

        if (type.GetMembers().OfType<IMethodSymbol>().Any(m => m.MethodKind == MethodKind.Destructor))
        {
            return "finalizers conflict with the Il2CppObjectBase lifetime";
        }

        return null;
    }

    private static bool HasSupportedComponentBase(INamedTypeSymbol? type, MetadataSymbolMap map)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == MonoBehaviourName) return true;
            if (current.IsGenericType || current.ContainingType is not null) return false;
            if (map.IsAuthorType(current)) continue;
            if (map.Resolve(current).Target is not { } target || !HasRegisterAttribute(target) ||
                !target.InstanceConstructors.Any(IsPointerConstructor)) return false;
        }
        return false;
    }

    private static bool IsUnitySerializable(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum || type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String)
            return true;
        if (type is IArrayTypeSymbol { Rank: 1 } array) return IsUnitySerializable(array.ElementType);
        if (type is not INamedTypeSymbol named) return false;
        if (named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
            return IsUnitySerializable(named.TypeArguments[0]);
        for (var current = named; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "UnityEngine.Object") return true;
        return named.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.SerializableAttribute");
    }

    private static string? RejectConstructor(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return null;
        }

        if (constructor.ParameterList.Parameters.Count > 0)
        {
            return "Unity creates components without arguments; move the setup into Awake or an initializer method";
        }

        return constructor.Initializer switch
        {
            null => null,
            { ThisOrBaseKeyword.RawKind: (int)SyntaxKind.BaseKeyword, ArgumentList.Arguments.Count: 0 } => null,
            _ when CallsDerivedConstructorPointer(constructor) => null,
            _ => "only an implicit or empty 'base()' initializer can be rewritten",
        };
    }

    private static ConstructorDeclarationSyntax InjectDefaultConstructor(
        ConstructorDeclarationSyntax constructor, string typeName, BlockSyntax authoredBody, ImmutableArray<string> fieldInitialization)
    {
        ConstructorInitializerSyntax initializer = ConstructorInitializer(
                SyntaxKind.BaseConstructorInitializer,
                ArgumentList(SingletonSeparatedList(Argument(
                    ParseExpression($"{ClassInjector}.DerivedConstructorPointer<{typeName}>()")))))
            .NormalizeWhitespace()
            .WithTrailingTrivia(Space);
        // Native storage is only valid once DerivedConstructorBody has bound the managed object; field storage
        // follows it and precedes every authored statement.
        IEnumerable<StatementSyntax> prologue = new[] { ParseStatement($"{ClassInjector}.DerivedConstructorBody(this);") }
            .Concat(InitializationStatements(fieldInitialization));
        BlockSyntax body = authoredBody
            .WithStatements(authoredBody.Statements.InsertRange(0, prologue))
            .NormalizeWhitespace();

        return constructor
            .WithParameterList(constructor.ParameterList.WithoutTrailingTrivia().WithTrailingTrivia(Space))
            .WithInitializer(initializer)
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(body.WithTrailingTrivia(constructor.GetTrailingTrivia()));
    }

    /// <summary>Initializes lowered field storage at the start of an authored native-pointer constructor, after its base call.</summary>
    private static ConstructorDeclarationSyntax PrependInitialization(
        ConstructorDeclarationSyntax constructor, ImmutableArray<string> fieldInitialization)
    {
        BlockSyntax body = ToBlock(constructor);
        body = body.WithStatements(body.Statements.InsertRange(0, InitializationStatements(fieldInitialization)));
        if (constructor.Body is not null)
        {
            return constructor.WithBody(body);
        }

        return constructor
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithBody(body.NormalizeWhitespace().WithLeadingTrivia(Space).WithTrailingTrivia(constructor.GetTrailingTrivia()));
    }

    private static IEnumerable<StatementSyntax> InitializationStatements(ImmutableArray<string> fieldInitialization) =>
        fieldInitialization.Select(statement => ParseStatement(statement).WithTrailingTrivia(Space));

    /// <summary>
    /// Gets whether <see cref="Rewrite"/> injects <paramref name="type"/>. Field lowering is decided from symbols at
    /// every reference site, so this must stay exactly as strict as the gates in <see cref="Rewrite"/>.
    /// </summary>
    internal static bool IsInjectable(INamedTypeSymbol type, MetadataSymbolMap map)
    {
        if (type.BaseType is not { } baseType || !map.IsNative(baseType) || RejectShape(type, baseType, map) is not null)
        {
            return false;
        }

        if (new[] { RegisterAttributeName, ClassInjectorName, HideAttributeName }.Any(required => map.FindTargetType(required) is null))
        {
            return false;
        }

        return !type.InstanceConstructors.Any(constructor =>
            !constructor.IsImplicitlyDeclared && !IsPointerConstructor(constructor) &&
            constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax syntax &&
            RejectConstructor(syntax) is not null);
    }

    /// <summary>Registers every injected ancestor first (a native class needs its base registered), then the type itself.</summary>
    private static IEnumerable<string> RegistrationStatements(INamedTypeSymbol type, MetadataSymbolMap map)
    {
        var ancestors = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type.BaseType;
             current is not null && current.ToDisplayString() != MonoBehaviourName;
             current = current.BaseType)
        {
            ancestors.Add(current);
        }

        ancestors.Reverse();
        foreach (INamedTypeSymbol ancestor in ancestors)
        {
            bool isAbstract = !map.IsAuthorType(ancestor) && map.Resolve(ancestor).Target is { } target
                ? target.IsAbstract
                : ancestor.IsAbstract;
            yield return RegistrationStatement(isAbstract, map.TargetDisplay(ancestor));
        }

        yield return RegistrationStatement(type.IsAbstract, type.Name);
    }

    private static string RegistrationStatement(bool isAbstract, string typeName) =>
        $"{InjectionSupportSource.Helper}.{(isAbstract ? "RegisterAbstract" : "RegisterComponent")}<{typeName}>();";

    private static BlockSyntax ToBlock(ConstructorDeclarationSyntax constructor) =>
        constructor.Body ??
        (constructor.ExpressionBody is { } arrow ? Block(ExpressionStatement(arrow.Expression)) : Block());

    /// <summary>
    /// Adds HideFromIl2Cpp to members whose signatures injection cannot represent. Hiding keeps them callable from
    /// managed code; only native-side dispatch (SendMessage, Invoke) loses them, which could not reach them anyway.
    /// </summary>
    private static MemberDeclarationSyntax? HideIfManagedOnly(
        MemberDeclarationSyntax source,
        MemberDeclarationSyntax lowered,
        SemanticModel model,
        MetadataSymbolMap map,
        INamedTypeSymbol self,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (source.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                model.GetSymbolInfo(attribute).Symbol is IMethodSymbol constructor &&
                constructor.ContainingType.ToDisplayString() == HideAttributeName))
        {
            return null;
        }

        switch (source, lowered)
        {
            case (MethodDeclarationSyntax method, MethodDeclarationSyntax loweredMethod)
                when model.GetDeclaredSymbol(method) is { } symbol:
            {
                // Abstract slots remain CLR contracts. Some Il2CppInterop builds reserve no native storage for
                // newly introduced abstract vtable slots; concrete overrides are exposed normally.
                if (symbol.IsAbstract) return PrependAttribute(loweredMethod, HideList());
                ITypeSymbol? unsupported = FirstUnsupported(symbol, map, self);
                if (unsupported is null && !symbol.IsGenericMethod)
                {
                    return null;
                }

                if (!symbol.IsStatic && UnityMessages.Contains(symbol.Name))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedCallbackSignature,
                        method.Identifier.GetLocation(),
                        symbol.Name,
                        unsupported?.ToDisplayString() ?? "generic parameters"));
                    return null;
                }

                return PrependAttribute(loweredMethod, HideList());
            }

            case (OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax, BaseMethodDeclarationSyntax loweredOperator)
                when model.GetDeclaredSymbol(source) is IMethodSymbol symbol && FirstUnsupported(symbol, map, self) is not null:
                return PrependAttribute(loweredOperator, HideList());

            case (BasePropertyDeclarationSyntax property, BasePropertyDeclarationSyntax loweredProperty)
                when model.GetDeclaredSymbol(property) is IPropertySymbol symbol:
            {
                if (symbol.IsAbstract) return HideAccessors(loweredProperty);
                bool supported = symbol.RefKind == RefKind.None &&
                                 IsRepresentable(symbol.Type, map, self) &&
                                 symbol.Parameters.All(p => p.RefKind == RefKind.None && IsRepresentable(p.Type, map, self));
                return supported ? null : HideAccessors(loweredProperty);
            }

            case (EventDeclarationSyntax @event, EventDeclarationSyntax loweredEvent)
                when model.GetDeclaredSymbol(@event) is { } symbol && (symbol.IsAbstract || !IsRepresentable(symbol.Type, map, self)):
                return HideAccessors(loweredEvent);

            case (EventFieldDeclarationSyntax eventField, EventFieldDeclarationSyntax loweredField)
                when eventField.Declaration.Variables.Any(v =>
                    model.GetDeclaredSymbol(v) is IEventSymbol symbol && (symbol.IsAbstract || !IsRepresentable(symbol.Type, map, self))):
                // Field-like event accessors are compiler generated; the 'method:' target reaches them.
                return PrependAttribute(loweredField, AttributeList(
                    AttributeTargetSpecifier(Token(SyntaxKind.MethodKeyword)).WithTrailingTrivia(Space),
                    SingletonSeparatedList(Attribute(ParseName(HideAttribute)))));

            default:
                return null;
        }
    }

    /// <summary>Il2CppInterop filters injected methods individually, so property and event accessors carry the attribute.</summary>
    private static BasePropertyDeclarationSyntax HideAccessors(BasePropertyDeclarationSyntax property)
    {
        if (property is PropertyDeclarationSyntax { AccessorList: null, ExpressionBody: { } propertyArrow } expressionProperty)
        {
            property = expressionProperty
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithAccessorList(GetterOnly(propertyArrow));
        }
        else if (property is IndexerDeclarationSyntax { AccessorList: null, ExpressionBody: { } indexerArrow } expressionIndexer)
        {
            property = expressionIndexer
                .WithExpressionBody(null)
                .WithSemicolonToken(default)
                .WithAccessorList(GetterOnly(indexerArrow));
        }

        AccessorListSyntax accessors = property.AccessorList!;
        return property.WithAccessorList(accessors.WithAccessors(List(accessors.Accessors.Select(HideAccessor))));
    }

    private static AccessorDeclarationSyntax HideAccessor(AccessorDeclarationSyntax accessor)
    {
        if (HasHideAttribute(accessor.AttributeLists))
        {
            return accessor;
        }

        SyntaxTriviaList leading = accessor.GetLeadingTrivia();
        AccessorDeclarationSyntax stripped = accessor.WithoutLeadingTrivia();
        return stripped.WithAttributeLists(stripped.AttributeLists.Insert(
            0, HideList().WithLeadingTrivia(leading).WithTrailingTrivia(Space)));
    }

    private static AccessorListSyntax GetterOnly(ArrowExpressionClauseSyntax arrow) =>
        AccessorList(SingletonList(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithExpressionBody(arrow.WithoutTrivia().WithLeadingTrivia(Space))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                .WithLeadingTrivia(Space)
                .WithTrailingTrivia(Space)))
            .WithLeadingTrivia(Space);

    private static ITypeSymbol? FirstUnsupported(IMethodSymbol method, MetadataSymbolMap map, INamedTypeSymbol self)
    {
        if (method.RefKind != RefKind.None || !IsRepresentable(method.ReturnType, map, self))
        {
            return method.ReturnType;
        }

        return method.Parameters
            .FirstOrDefault(p => p.RefKind != RefKind.None || !IsRepresentable(p.Type, map, self))?
            .Type;
    }

    /// <summary>
    /// Conservative injection surface: primitives, strings, pointers, the injected type itself, and any nongeneric
    /// native reference or value type that maps onto target metadata. Managed collections, arrays, and other author types stay
    /// managed-only.
    /// </summary>
    private static bool IsRepresentable(ITypeSymbol type, MetadataSymbolMap map, INamedTypeSymbol self)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return true;
        }

        if (SymbolEqualityComparer.Default.Equals(type, self))
        {
            return true;
        }

        return type is INamedTypeSymbol { IsGenericType: false } named &&
               !map.IsAuthorType(named) &&
               map.Resolve(named) is { Status: TypeMappingStatus.Mapped, Target: { } target } &&
               (map.IsNative(named) || target.IsValueType);
    }

    internal static bool IsPointerConstructor(IMethodSymbol constructor) =>
        constructor.Parameters is [{ Type.SpecialType: SpecialType.System_IntPtr, RefKind: RefKind.None }];

    internal static bool CallsDerivedConstructorPointer(IMethodSymbol constructor) =>
        constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ConstructorDeclarationSyntax syntax &&
        CallsDerivedConstructorPointer(syntax);

    private static bool CallsDerivedConstructorPointer(ConstructorDeclarationSyntax constructor) =>
        constructor.Initializer?.ArgumentList.Arguments
            .Any(argument => argument.ToString().Contains("DerivedConstructorPointer", StringComparison.Ordinal)) == true;

    // Author binding is checked before lowering, so attribute identity is available even through aliases.
    private static bool HasRegisterAttribute(INamedTypeSymbol type) =>
        type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == RegisterAttributeName);

    private static bool HasHideAttribute(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().Replace("global::", "", StringComparison.Ordinal)
                is HideAttributeName or "Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp");

    /// <summary>Partial classes receive class-level output exactly once: in the first part by path and position.</summary>
    private static bool IsPrimaryDeclaration(INamedTypeSymbol type, ClassDeclarationSyntax declaration)
    {
        SyntaxReference? first = type.DeclaringSyntaxReferences
            .OrderBy(reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Span.Start)
            .FirstOrDefault();
        return first is not null &&
               first.SyntaxTree == declaration.SyntaxTree &&
               first.Span == declaration.Span;
    }

    private static bool DeclaredIn(ISymbol symbol, ClassDeclarationSyntax declaration) =>
        symbol.DeclaringSyntaxReferences.Any(reference =>
            reference.SyntaxTree == declaration.SyntaxTree && declaration.Span.Contains(reference.Span));

    private static AttributeListSyntax HideList() =>
        AttributeList(SingletonSeparatedList(Attribute(ParseName(HideAttribute))));

    private static T PrependAttribute<T>(T member, AttributeListSyntax list)
        where T : MemberDeclarationSyntax
    {
        SyntaxTriviaList leading = member.GetLeadingTrivia();
        IEnumerable<SyntaxTrivia> indent = leading.Reverse().TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
        AttributeListSyntax placed = list
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(TriviaList(ElasticCarriageReturnLineFeed).AddRange(indent));
        T stripped = member.WithoutLeadingTrivia();
        return (T)stripped.WithAttributeLists(stripped.AttributeLists.Insert(0, placed));
    }

    private static string MemberIndent(ClassDeclarationSyntax declaration)
    {
        if (declaration.Members.FirstOrDefault() is { } first)
        {
            return string.Concat(first.GetLeadingTrivia().Reverse()
                .TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());
        }

        string classIndent = string.Concat(declaration.GetLeadingTrivia().Reverse()
            .TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse());
        return classIndent + "    ";
    }

    private static MemberDeclarationSyntax ParseMember(string text, string indent) =>
        ParseMemberDeclaration(text)!
            .NormalizeWhitespace()
            .WithLeadingTrivia(Whitespace(indent))
            .WithTrailingTrivia(ElasticCarriageReturnLineFeed, ElasticCarriageReturnLineFeed);

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "S1Interop.Compiler", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
