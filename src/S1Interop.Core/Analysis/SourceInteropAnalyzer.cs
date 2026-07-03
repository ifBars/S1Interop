using System.Text.RegularExpressions;

namespace S1Interop.Core;

public sealed class SourceInteropAnalyzer
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        WorkspaceTraversal.CommonExcludedDirectoryNames,
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex ClassRegex = new(
        @"^\s*(?:public|internal|private|protected)?\s*(?:(?:sealed|abstract|partial)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*(?<base>[^{\r\n]+))?",
        RegexOptions.Compiled);

    private static readonly Regex PublicMemberRegex = new(
        @"^\s*public\s+(?<modifiers>(?:(?:static|override|virtual|sealed|async|new)\s+)*)?(?<return>[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]\s]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled);

    private static readonly Regex UnityActionIdentifierRegex = new(
        @"(?:^|[^\w.])(?:UnityEngine\.Events\.)?UnityAction(?:<[^>]+>)?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex UnityActionFactoryAssignmentRegex = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*.*(?:ToUnityAction|ConvertDelegate\s*<\s*(?:UnityEngine\.Events\.)?UnityAction)",
        RegexOptions.Compiled);

    private static readonly Regex ListenerIdentifierRegex = new(
        @"\.(?:AddListener|RemoveListener)\(\s*(?<listener>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex ReflectionFieldLookupRegex = new(
        @"\.GetField\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ReflectionPropertyLookupRegex = new(
        @"\.GetProperty\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex TypeOfFieldOrPropertyLookupRegex = new(
        @"typeof\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)\s*\.\s*Get(?:Field|Property)\s*\(\s*""[A-Za-z_][A-Za-z0-9_]*""\s*(?:,|\))",
        RegexOptions.Compiled);

    private static readonly Regex ReflectionLookupReceiverRegex = new(
        @"(?<receiver>typeof\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)|[A-Za-z_][A-Za-z0-9_.]*\s*\.\s*GetType\s*\(\s*\))\s*\.\s*Get(?<kind>Field|Property)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex GetTypeReceiverRegex = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*\.\s*GetType\s*\(\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex BackendTypedIdentifierRegex = new(
        @"(?<type>(?:Il2Cpp)?ScheduleOne(?:\.[A-Za-z_][A-Za-z0-9_]*)+)(?:\?)?(?:\s*<[^>\r\n;()]+>)?\s+(?<name>[_A-Za-z][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled);

    private static readonly Regex ReflectionLookupWithVariableReceiverRegex = new(
        @"(?<receiver>typeof\s*\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)|[A-Za-z_][A-Za-z0-9_.]*\s*\.\s*GetType\s*\(\s*\)|[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*Get(?<kind>Field|Property)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex TypeAliasAssignmentRegex = new(
        @"\b(?:Type|System\.Type)\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<receiver>[A-Za-z_][A-Za-z0-9_.]*\s*\.\s*GetType\s*\(\s*\))\s*;",
        RegexOptions.Compiled);

    private static readonly Regex GuiWindowDelegateArgumentRegex = new(
        @"\bGUI\.Window\s*\(\s*[^,]+\s*,\s*[^,]+\s*,\s*(?<listener>[A-Za-z_][A-Za-z0-9_\.]*)\s*,",
        RegexOptions.Compiled);

    public SourceInteropAnalysis Analyze(string projectPath)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        string projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        if (!Directory.Exists(projectDirectory))
        {
            return new SourceInteropAnalysis(
                fullProjectPath,
                Array.Empty<InjectedTypeAnalysis>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<SourceRisk>(),
                Array.Empty<InteropDiagnostic>());
        }

        string[] sourceFiles = WorkspaceTraversal.EnumerateFiles(projectDirectory, "*.cs", ExcludedDirectoryNames)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        HashSet<string> projectClassNames = DiscoverProjectClassNames(sourceFiles);
        HashSet<string> injectedTypeNames = DiscoverInjectedTypeNames(sourceFiles);
        var injectedTypes = new List<InjectedTypeAnalysis>();
        var diagnostics = new List<InteropDiagnostic>();
        var sourceRisks = new List<SourceRisk>();
        var guardEvidence = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var bridgeEvidence = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sourceFile in sourceFiles)
        {
            AnalyzeFile(fullProjectPath, projectDirectory, sourceFile, projectClassNames, injectedTypeNames, injectedTypes, diagnostics, sourceRisks, guardEvidence, bridgeEvidence);
        }

        return new SourceInteropAnalysis(
            fullProjectPath,
            injectedTypes,
            guardEvidence.ToArray(),
            bridgeEvidence.ToArray(),
            sourceRisks
                .DistinctBy(risk => (risk.Kind, risk.FilePath, risk.Line, risk.Evidence))
                .OrderBy(risk => risk.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(risk => risk.Line)
                .ToArray(),
            diagnostics);
    }

    private static void AnalyzeFile(
        string projectPath,
        string projectDirectory,
        string sourceFile,
        IReadOnlySet<string> projectClassNames,
        IReadOnlySet<string> injectedTypeNames,
        List<InjectedTypeAnalysis> injectedTypes,
        List<InteropDiagnostic> diagnostics,
        List<SourceRisk> sourceRisks,
        SortedSet<string> guardEvidence,
        SortedSet<string> bridgeEvidence)
    {
        string[] lines = File.ReadAllLines(sourceFile);
        string relativePath = Path.GetRelativePath(projectDirectory, sourceFile);
        var spans = DiscoverClassSpans(lines);
        var runtimeGuardStack = new Stack<bool>();
        HashSet<string> runtimeSafeUnityListenerNames = DiscoverRuntimeSafeUnityListenerNames(lines);
        HashSet<int> runtimeSpecificListenerStartLines = DiscoverRuntimeSpecificListenerStartLines(lines);

        for (int index = 0; index < lines.Length; index++)
        {
            UpdateRuntimeGuardStack(lines[index], runtimeGuardStack);

            if (lines[index].Contains("#if !MONO", StringComparison.Ordinal))
            {
                guardEvidence.Add($"{relativePath}:{index + 1} uses #if !MONO as IL2CPP guard");
            }

            if (lines[index].Contains("Il2CppSystem.Collections.Generic.List", StringComparison.Ordinal))
            {
                bridgeEvidence.Add($"{relativePath}:{index + 1} uses Il2CppSystem.Collections.Generic.List");
            }

            if (lines[index].Contains("Il2CppStructArray<", StringComparison.Ordinal))
            {
                bridgeEvidence.Add($"{relativePath}:{index + 1} uses Il2CppStructArray");
            }

            AddLineSourceRisks(
                sourceFile,
                relativePath,
                lines,
                lines[index],
                index,
                runtimeGuardStack.Contains(true),
                runtimeSafeUnityListenerNames,
                runtimeSpecificListenerStartLines.Contains(index),
                sourceRisks);
        }

        foreach (ClassSpan span in spans)
        {
            bool hasRegisterTypeAttribute = HasRegisterTypeAttribute(lines, span.StartLine);
            bool isLikelyInjectedType = injectedTypeNames.Contains(span.Name);
            if (!isLikelyInjectedType)
            {
                continue;
            }

            InjectedTypeAnalysis injectedType = AnalyzeInjectedType(lines, sourceFile, span);
            injectedTypes.Add(injectedType);

            if (!hasRegisterTypeAttribute)
            {
                diagnostics.Add(new InteropDiagnostic(
                    "injected_type_missing_registertype",
                    DiagnosticSeverity.Error,
                    "Project-owned Unity component types that must exist on IL2CPP should be guarded with RegisterTypeInIl2Cpp.",
                    projectPath,
                    null,
                    $"{sourceFile}:{span.StartLine + 1}: {span.Name}"));
            }

            if (!injectedType.HasIntPtrConstructor)
            {
                diagnostics.Add(new InteropDiagnostic(
                    "injected_type_missing_intptr_constructor",
                    DiagnosticSeverity.Error,
                    "IL2CPP injected types registered with RegisterTypeInIl2Cpp should expose a public IntPtr constructor that delegates to base(ptr).",
                    projectPath,
                    null,
                    $"{sourceFile}:{span.StartLine + 1}: {span.Name}"));
            }

            if (injectedType.HasDerivedConstructorPointer && !injectedType.HasDerivedConstructorBody)
            {
                diagnostics.Add(new InteropDiagnostic(
                    "injected_constructor_body_mismatch",
                    DiagnosticSeverity.Error,
                    "ClassInjector.DerivedConstructorPointer<T>() should be paired with ClassInjector.DerivedConstructorBody(this).",
                    projectPath,
                    null,
                    $"{sourceFile}:{span.StartLine + 1}: {span.Name}"));
            }

            AddManagedSurfaceDiagnostics(projectPath, sourceFile, lines, span, projectClassNames, injectedTypeNames, diagnostics);
            AddGameConstructorSignatureDiagnostics(projectPath, sourceFile, lines, span, diagnostics);
        }
    }

    private static void AddLineSourceRisks(
        string sourceFile,
        string relativePath,
        IReadOnlyList<string> lines,
        string line,
        int index,
        bool isRuntimeGuarded,
        IReadOnlySet<string> runtimeSafeUnityListenerNames,
        bool isRuntimeSpecificListenerStart,
        List<SourceRisk> sourceRisks)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return;
        }

        if (isRuntimeGuarded || isRuntimeSpecificListenerStart)
        {
            return;
        }

        if (IsHarmonyTranspilerLine(trimmed))
        {
            sourceRisks.Add(new SourceRisk(
                "HarmonyTranspiler",
                "high",
                sourceFile,
                index + 1,
                "Harmony IL transpilers are not portable to IL2CPP builds.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Replace with prefix/postfix/finalizer patches or runtime-specific patch registration for IL2CPP."));
        }

        if (IsDirectUnityEventListenerLine(trimmed, runtimeSafeUnityListenerNames))
        {
            sourceRisks.Add(new SourceRisk(
                "DirectUnityEventListener",
                "medium",
                sourceFile,
                index + 1,
                "Direct UnityEvent AddListener calls may need runtime-specific delegate conversion on IL2CPP.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer S1API EventHelper/ButtonUtils/ToggleUtils, DelegateSupport.ConvertDelegate, or explicit MONO/IL2CPP listener construction."));
        }

        if (IsDirectDelegateCombineLine(trimmed))
        {
            sourceRisks.Add(new SourceRisk(
                "DirectDelegateCombine",
                "medium",
                sourceFile,
                index + 1,
                "Direct Delegate.Combine/Remove against game events can require IL2CPP delegate conversion.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer S1API EventHelper or DelegateSupport.ConvertDelegate for generated-wrapper event surfaces."));
        }

        if (IsDirectDelegateArgumentInteropLine(trimmed))
        {
            sourceRisks.Add(new SourceRisk(
                "DirectDelegateArgumentInterop",
                "medium",
                sourceFile,
                index + 1,
                "Direct delegate arguments passed to Unity or game callbacks can require IL2CPP delegate conversion.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer S1Interop.Generated.S1InteropDelegateBridge.Convert<TDelegate> so delegate conversion is centralized and runtime-selected."));
        }

        if (IsHarmonyOverloadBindingLine(lines, index))
        {
            sourceRisks.Add(new SourceRisk(
                "HarmonyOverloadBinding",
                "medium",
                sourceFile,
                index + 1,
                "Harmony overload binding uses explicit runtime Type arrays that can drift between Mono and IL2CPP wrappers.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer a generated S1InteropMember method target with ParameterTypeNames so backend-specific type names and by-ref parameters stay centralized."));
        }

        bool isFieldPropertyReflectionFallback = IsFieldPropertyReflectionFallbackLine(lines, index);
        if (isFieldPropertyReflectionFallback)
        {
            sourceRisks.Add(new SourceRisk(
                "FieldPropertyReflectionFallback",
                "medium",
                sourceFile,
                index + 1,
                "Manual reflection fallback between fields and properties is a common Mono/IL2CPP member-shape drift point.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer generated S1InteropMember field/property accessors so the member name, owner type, cache, and get/set behavior stay centralized across runtimes."));
        }

        if (!isFieldPropertyReflectionFallback && IsDirectMemberReflectionLookupLine(lines, index))
        {
            sourceRisks.Add(new SourceRisk(
                "DirectMemberReflectionLookup",
                "low",
                sourceFile,
                index + 1,
                "Direct reflected field/property lookup can drift between Mono and IL2CPP member surfaces.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer generated S1InteropMember declarations so the owner type, member name, static shape, and cached lookup stay centralized across runtimes."));
        }

        if (IsIl2CppByteBufferInteropLine(lines, index, trimmed))
        {
            sourceRisks.Add(new SourceRisk(
                "Il2CppByteBufferInterop",
                "high",
                sourceFile,
                index + 1,
                "Native or game APIs that fill managed byte[] buffers can behave differently under IL2CPP marshalling.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Use an IL2CPP branch with Il2CppStructArray<byte> for receive/fill buffers, copy bytes into a managed byte[] before parsing, and consider pinning send buffers for IL2CPP."));
        }

        if (IsManagedCollectionSignatureInteropLine(lines, index, trimmed))
        {
            sourceRisks.Add(new SourceRisk(
                "ManagedCollectionSignatureInterop",
                "medium",
                sourceFile,
                index + 1,
                "Game-facing signatures that use managed collection types can miss IL2CPP wrapper collection parameters at runtime.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Add a MONO/IL2CPP signature branch so IL2CPP uses Il2CppSystem.Collections.Generic.List<T> for game-owned callback parameters."));
        }

        string codeOnlyLine = StripLineComment(trimmed);
        if (IsIl2CppObjectCastInteropLine(codeOnlyLine))
        {
            sourceRisks.Add(new SourceRisk(
                "Il2CppObjectCastInterop",
                "medium",
                sourceFile,
                index + 1,
                "Plain C# casts or pattern matches against IL2CPP-backed objects can fail to unwrap object proxies.",
                $"{relativePath}:{index + 1}: {trimmed}",
                "Prefer S1Interop.Generated.S1InteropObjectCast.As<T>/Is<T> for backend-neutral casts, or add an IL2CPP branch that calls TryCast<T>() and handles cast failures explicitly."));
        }
    }

    private static bool IsHarmonyTranspilerLine(string line) =>
        line.Contains("HarmonyTranspiler", StringComparison.Ordinal) ||
        line.Contains("IEnumerable<CodeInstruction>", StringComparison.Ordinal) ||
        line.Contains("CodeInstruction", StringComparison.Ordinal) && line.Contains("Transpiler", StringComparison.Ordinal);

    private static bool IsIl2CppByteBufferInteropLine(IReadOnlyList<string> lines, int index, string line)
    {
        if (line.Contains("Il2CppStructArray", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.Contains("SteamNetworking.ReadP2PPacket", StringComparison.Ordinal))
        {
            return true;
        }

        return LooksLikeNativeByteBufferFillCall(line) &&
               DiscoverByteArrayVariableNames(lines).Any(name => line.Contains(name, StringComparison.Ordinal));
    }

    private static bool LooksLikeNativeByteBufferFillCall(string line) =>
        line.Contains('(') &&
        (line.Contains(".Read", StringComparison.Ordinal) ||
         line.Contains(".Receive", StringComparison.Ordinal) ||
         line.Contains(".Recv", StringComparison.Ordinal) ||
         line.Contains(".Get", StringComparison.Ordinal) ||
         line.Contains(".Fill", StringComparison.Ordinal)) &&
        !line.Contains("File.", StringComparison.Ordinal) &&
        !line.Contains("Encoding.", StringComparison.Ordinal) &&
        !line.Contains("MemoryStream", StringComparison.Ordinal);

    private static IReadOnlySet<string> DiscoverByteArrayVariableNames(IReadOnlyList<string> lines)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Regex declarationRegex = new(@"\b(?:byte\[\]|System\.Byte\[\])\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b");
        foreach (string line in lines)
        {
            Match match = declarationRegex.Match(line);
            if (match.Success)
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names;
    }

    private static bool IsManagedCollectionSignatureInteropLine(IReadOnlyList<string> lines, int index, string line) =>
        line.Contains("List<", StringComparison.Ordinal) &&
        line.Contains(')') &&
        !line.Contains('=') &&
        !line.Contains("Il2CppSystem.Collections.Generic.List", StringComparison.Ordinal) &&
        IsGameFacingSignatureContext(lines, index, line);

    private static bool IsGameFacingSignatureContext(IReadOnlyList<string> lines, int index, string line)
    {
        string context = string.Join(
            ' ',
            lines.Skip(Math.Max(0, index - 2)).Take(Math.Min(5, lines.Count - Math.Max(0, index - 2))).Select(value => value.Trim()));
        return context.Contains("Postfix", StringComparison.Ordinal) ||
               context.Contains("Prefix", StringComparison.Ordinal) ||
               context.Contains("__instance", StringComparison.Ordinal) ||
               context.Contains("BindInternal", StringComparison.Ordinal) ||
               context.Contains("Config", StringComparison.Ordinal) ||
               context.Contains("Panel", StringComparison.Ordinal);
    }

    private static bool IsIl2CppObjectCastInteropLine(string line) =>
        line.Contains(" is ", StringComparison.Ordinal) &&
        !line.Contains("Il2CppObjectBase", StringComparison.Ordinal) &&
        !line.Contains("TryCast<", StringComparison.Ordinal) &&
        (line.Contains("UniversalRenderPipelineAsset", StringComparison.Ordinal) ||
         line.Contains("UnityEngine.Object", StringComparison.Ordinal) ||
         line.Contains("Component", StringComparison.Ordinal) ||
         line.Contains("MonoBehaviour", StringComparison.Ordinal));

    private static string StripLineComment(string line)
    {
        int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex < 0 ? line : line[..commentIndex].TrimEnd();
    }

    private static bool IsDirectUnityEventListenerLine(string line, IReadOnlySet<string> runtimeSafeUnityListenerNames)
    {
        if (!line.Contains(".AddListener(", StringComparison.Ordinal))
        {
            return false;
        }

        return !line.Contains("new UnityAction", StringComparison.Ordinal) &&
               !line.Contains("new UnityEngine.Events.UnityAction", StringComparison.Ordinal) &&
               !line.Contains("(UnityAction", StringComparison.Ordinal) &&
               !line.Contains("(UnityEngine.Events.UnityAction", StringComparison.Ordinal) &&
               !line.Contains("new System.Action", StringComparison.Ordinal) &&
               !line.Contains("(System.Action", StringComparison.Ordinal) &&
               !line.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) &&
               !line.Contains("ToUnityAction", StringComparison.Ordinal) &&
               !line.Contains("EventHelper.AddListener", StringComparison.Ordinal) &&
               !line.Contains("EventUtils.AddListener", StringComparison.Ordinal) &&
               !line.Contains("ButtonUtils.AddListener", StringComparison.Ordinal) &&
               !line.Contains("ToggleUtils.AddListener", StringComparison.Ordinal) &&
               !UsesRuntimeSafeListenerName(line, runtimeSafeUnityListenerNames);
    }

    private static bool IsDirectDelegateCombineLine(string line) =>
        (line.Contains("Delegate.Combine", StringComparison.Ordinal) ||
         line.Contains("Delegate.Remove", StringComparison.Ordinal)) &&
        !line.Contains("Il2CppSystem.Delegate.", StringComparison.Ordinal) &&
        !line.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) &&
        !line.Contains("EventHelper.", StringComparison.Ordinal);

    private static bool IsDirectDelegateArgumentInteropLine(string line)
    {
        if (!line.Contains("GUI.Window", StringComparison.Ordinal) ||
            line.Contains("DelegateSupport.ConvertDelegate", StringComparison.Ordinal) ||
            line.Contains("S1InteropDelegateBridge.Convert", StringComparison.Ordinal) ||
            line.Contains("new GUI.WindowFunction", StringComparison.Ordinal))
        {
            return false;
        }

        Match match = GuiWindowDelegateArgumentRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string listener = match.Groups["listener"].Value;
        return listener.Length > 0 &&
               !listener.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsHarmonyOverloadBindingLine(IReadOnlyList<string> lines, int index)
    {
        string line = lines[index];
        if (!line.Contains("AccessTools.Method", StringComparison.Ordinal))
        {
            return false;
        }

        string sourceWindow = GetSourceWindow(lines, index, maxLineCount: 12);
        return sourceWindow.Contains("typeof(", StringComparison.Ordinal) &&
               (sourceWindow.Contains(".MakeByRefType()", StringComparison.Ordinal) ||
                sourceWindow.Contains("new[]", StringComparison.Ordinal) ||
                sourceWindow.Contains("new Type[]", StringComparison.Ordinal) ||
                sourceWindow.Contains("new System.Type[]", StringComparison.Ordinal) ||
                ContainsCollectionExpressionParameterList(sourceWindow));
    }

    private static bool IsFieldPropertyReflectionFallbackLine(IReadOnlyList<string> lines, int index)
    {
        string line = lines[index];
        if (!line.Contains(".GetField(", StringComparison.Ordinal))
        {
            return false;
        }

        string window = GetSourceWindow(lines, Math.Max(0, index - 15), maxLineCount: 31);
        return HasFieldPropertyFallbackPair(window);
    }

    private static bool IsDirectMemberReflectionLookupLine(IReadOnlyList<string> lines, int index)
    {
        string line = lines[index];
        Match lookup = ReflectionLookupReceiverRegex.Match(line);
        if (!lookup.Success)
        {
            return false;
        }

        string receiver = lookup.Groups["receiver"].Value;
        if (!receiver.Contains("typeof(", StringComparison.Ordinal) &&
            !HasKnownBackendGetTypeReceiver(lines, index, receiver))
        {
            return false;
        }

        string sourceWindow = GetSourceWindow(lines, index, maxLineCount: 16);
        string backwardWindow = GetSourceWindow(lines, Math.Max(0, index - 15), Math.Min(16, index + 1));
        return !HasFieldPropertyFallbackPair(sourceWindow) &&
               !HasFieldPropertyFallbackPair(backwardWindow);
    }

    private static bool HasKnownBackendGetTypeReceiver(IReadOnlyList<string> lines, int index, string receiver)
    {
        Match receiverMatch = GetTypeReceiverRegex.Match(receiver);
        if (!receiverMatch.Success)
        {
            return false;
        }

        string receiverName = receiverMatch.Groups["name"].Value.Split('.')[^1];
        int startIndex = Math.Max(0, index - 30);
        string sourceWindow = GetSourceWindow(lines, startIndex, index - startIndex + 1);
        foreach (Match declaration in BackendTypedIdentifierRegex.Matches(sourceWindow))
        {
            if (declaration.Groups["name"].Value.Equals(receiverName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFieldPropertyFallbackPair(string sourceWindow)
    {
        foreach (ReflectionLookup lookup in GetReflectionLookups(sourceWindow))
        {
            if (lookup.Kind != "Field")
            {
                continue;
            }

            if (HasReflectionLookup(sourceWindow, lookup.Receiver, "Property"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReflectionLookup(string sourceWindow, string receiver, string kind) =>
        GetReflectionLookups(sourceWindow)
            .Any(lookup =>
                lookup.Kind == kind &&
                lookup.Receiver == receiver);

    private static IReadOnlyList<ReflectionLookup> GetReflectionLookups(string sourceWindow)
    {
        Dictionary<string, string> aliases = GetTypeAliases(sourceWindow);
        var lookups = new List<ReflectionLookup>();
        foreach (Match match in ReflectionLookupWithVariableReceiverRegex.Matches(sourceWindow))
        {
            string receiver = NormalizeReflectionReceiver(match.Groups["receiver"].Value);
            if (aliases.TryGetValue(receiver, out string? normalizedReceiver))
            {
                lookups.Add(new ReflectionLookup(normalizedReceiver, match.Groups["kind"].Value));
            }
            else if (receiver.Contains("typeof(", StringComparison.Ordinal) ||
                     receiver.Contains(".GetType()", StringComparison.Ordinal))
            {
                lookups.Add(new ReflectionLookup(receiver, match.Groups["kind"].Value));
            }
        }

        return lookups;
    }

    private static Dictionary<string, string> GetTypeAliases(string sourceWindow)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in TypeAliasAssignmentRegex.Matches(sourceWindow))
        {
            aliases[match.Groups["alias"].Value] = NormalizeReflectionReceiver(match.Groups["receiver"].Value);
        }

        return aliases;
    }

    private static string NormalizeReflectionReceiver(string receiver) =>
        Regex.Replace(receiver, @"\s+", string.Empty);

    private static string GetSourceWindow(IReadOnlyList<string> lines, int startIndex, int maxLineCount)
    {
        int endIndex = Math.Min(lines.Count, startIndex + maxLineCount);
        return string.Join('\n', lines.Skip(startIndex).Take(endIndex - startIndex));
    }

    private static bool ContainsCollectionExpressionParameterList(string sourceWindow) =>
        Regex.IsMatch(sourceWindow, @"(?m)^\s*\[\s*$") ||
        Regex.IsMatch(sourceWindow, @"(?m)^\s*\[\s*typeof\s*\(");

    private static void UpdateRuntimeGuardStack(string line, Stack<bool> runtimeGuardStack)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
        {
            runtimeGuardStack.Push(IsRuntimeGuardCondition(trimmed));
            return;
        }

        if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
        {
            bool previous = runtimeGuardStack.Count > 0 && runtimeGuardStack.Pop();
            runtimeGuardStack.Push(previous || IsRuntimeGuardCondition(trimmed));
            return;
        }

        if (trimmed.StartsWith("#else", StringComparison.Ordinal))
        {
            return;
        }

        if (trimmed.StartsWith("#endif", StringComparison.Ordinal) && runtimeGuardStack.Count > 0)
        {
            runtimeGuardStack.Pop();
        }
    }

    private static bool IsRuntimeGuardCondition(string directive) =>
        directive.Contains("IL2CPP", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("MONO", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("IL2CPPMELON", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("IL2CPPBEPINEX", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("MONO_BUILD", StringComparison.OrdinalIgnoreCase) ||
        directive.Contains("MOD_IL2CPP", StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideRuntimeConditional(IReadOnlyList<string> lines, int lineIndex)
    {
        var runtimeGuardStack = new Stack<bool>();
        for (int index = 0; index <= lineIndex && index < lines.Count; index++)
        {
            UpdateRuntimeGuardStack(lines[index], runtimeGuardStack);
        }

        return runtimeGuardStack.Contains(true);
    }

    private static HashSet<string> DiscoverRuntimeSafeUnityListenerNames(IEnumerable<string> lines)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            foreach (Match match in UnityActionIdentifierRegex.Matches(line))
            {
                names.Add(match.Groups["name"].Value);
            }

            Match assignment = UnityActionFactoryAssignmentRegex.Match(line);
            if (assignment.Success)
            {
                names.Add(assignment.Groups["name"].Value);
            }
        }

        return names;
    }

    private static HashSet<int> DiscoverRuntimeSpecificListenerStartLines(IReadOnlyList<string> lines)
    {
        var lineIndexes = new HashSet<int>();
        for (int index = 0; index < lines.Count; index++)
        {
            string trimmed = lines[index].Trim();
            if (!trimmed.Contains(".AddListener(", StringComparison.Ordinal) &&
                !trimmed.Contains(".RemoveListener(", StringComparison.Ordinal))
            {
                continue;
            }

            if (!trimmed.EndsWith("(", StringComparison.Ordinal))
            {
                continue;
            }

            for (int next = index + 1; next < Math.Min(lines.Count, index + 5); next++)
            {
                string nextTrimmed = lines[next].TrimStart();
                if (nextTrimmed.Length == 0)
                {
                    continue;
                }

                if (nextTrimmed.StartsWith("#if ", StringComparison.Ordinal) ||
                    nextTrimmed.StartsWith("#elif ", StringComparison.Ordinal))
                {
                    if (IsRuntimeGuardCondition(nextTrimmed))
                    {
                        lineIndexes.Add(index);
                    }
                }

                break;
            }
        }

        return lineIndexes;
    }

    private static bool UsesRuntimeSafeListenerName(string line, IReadOnlySet<string> runtimeSafeUnityListenerNames)
    {
        Match match = ListenerIdentifierRegex.Match(line);
        return match.Success && runtimeSafeUnityListenerNames.Contains(match.Groups["listener"].Value);
    }

    private static InjectedTypeAnalysis AnalyzeInjectedType(string[] lines, string sourceFile, ClassSpan span)
    {
        string body = string.Join('\n', lines.Skip(span.StartLine).Take(span.EndLine - span.StartLine + 1));
        bool hasIntPtrConstructor = Regex.IsMatch(
            body,
            $@"public\s+{Regex.Escape(span.Name)}\s*\(\s*(?:System\.)?IntPtr\s+\w+\s*\)\s*:\s*base\s*\(\s*\w+\s*\)",
            RegexOptions.Multiline);
        bool hasDerivedPointer = body.Contains($"ClassInjector.DerivedConstructorPointer<{span.Name}>()", StringComparison.Ordinal);
        bool hasDerivedBody = body.Contains("ClassInjector.DerivedConstructorBody(this)", StringComparison.Ordinal);
        IReadOnlyList<string> hiddenMembers = DiscoverHiddenMembers(lines, span);

        return new InjectedTypeAnalysis(
            span.Name,
            sourceFile,
            span.StartLine + 1,
            span.BaseType,
            hasIntPtrConstructor,
            hasDerivedPointer,
            hasDerivedBody,
            hiddenMembers);
    }

    private static IReadOnlyList<string> DiscoverHiddenMembers(string[] lines, ClassSpan span)
    {
        var members = new List<string>();
        for (int index = span.StartLine; index <= span.EndLine; index++)
        {
            Match memberMatch = PublicMemberRegex.Match(lines[index]);
            if (!memberMatch.Success || !HasHideFromIl2CppAttribute(lines, index))
            {
                continue;
            }

            members.Add(memberMatch.Groups["name"].Value);
        }

        return members.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static void AddManagedSurfaceDiagnostics(
        string projectPath,
        string sourceFile,
        string[] lines,
        ClassSpan span,
        IReadOnlySet<string> projectClassNames,
        IReadOnlySet<string> injectedTypeNames,
        List<InteropDiagnostic> diagnostics)
    {
        for (int index = span.StartLine; index <= span.EndLine; index++)
        {
            Match memberMatch = PublicMemberRegex.Match(lines[index]);
            if (!memberMatch.Success)
            {
                continue;
            }

            string memberName = memberMatch.Groups["name"].Value;
            if (string.Equals(memberName, span.Name, StringComparison.Ordinal) ||
                memberMatch.Groups["modifiers"].Value.Contains("override", StringComparison.Ordinal))
            {
                continue;
            }

            if (HasHideFromIl2CppAttribute(lines, index))
            {
                continue;
            }

            string signature = $"{memberMatch.Groups["return"].Value} {memberName}({memberMatch.Groups["params"].Value})";
            string[] managedTypes = FindProjectManagedTypes(signature, projectClassNames, injectedTypeNames);
            if (managedTypes.Length == 0)
            {
                continue;
            }

            diagnostics.Add(new InteropDiagnostic(
                "injected_member_requires_hidefromil2cpp",
                DiagnosticSeverity.Error,
                "IL2CPP injected type member exposes project-local managed helper types; add HideFromIl2Cpp or provide a primitive/generated-wrapper-safe bridge.",
                projectPath,
                null,
                $"{sourceFile}:{index + 1}: {span.Name}.{memberName}({memberMatch.Groups["params"].Value}) uses {string.Join(", ", managedTypes)}"));
        }
    }

    private static void AddGameConstructorSignatureDiagnostics(
        string projectPath,
        string sourceFile,
        string[] lines,
        ClassSpan span,
        List<InteropDiagnostic> diagnostics)
    {
        if (!IsLikelyGameBackedType(span.BaseType))
        {
            return;
        }

        for (int index = span.StartLine; index <= span.EndLine; index++)
        {
            string line = lines[index];
            if (!line.Contains($" {span.Name}(", StringComparison.Ordinal) ||
                !line.Contains("Guid", StringComparison.Ordinal) ||
                !line.Contains("List<", StringComparison.Ordinal) ||
                IsInsideRuntimeConditional(lines, index))
            {
                continue;
            }

            diagnostics.Add(new InteropDiagnostic(
                "game_constructor_requires_il2cpp_signature",
                DiagnosticSeverity.Error,
                "Constructors that mirror game class constructors can need IL2CPP wrapper parameter equivalents under IL2CPP builds.",
                projectPath,
                null,
                $"{sourceFile}:{index + 1}: {span.Name} constructor uses managed Guid/List<T> parameters without an IL2CPP signature branch."));
            return;
        }
    }

    private static bool IsLikelyGameBackedType(string baseType)
    {
        string[] baseTypes = baseType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return baseTypes.Any(candidate =>
            IsLikelyIl2CppInjectedBase(candidate) ||
            IsKnownGameDataBase(candidate) ||
            IsScheduleOneTypeName(candidate));
    }

    private static bool IsKnownGameDataBase(string candidate)
    {
        string simpleName = candidate.Split('.').Last();
        return simpleName is "VehicleData" or "GameData";
    }

    private static bool IsScheduleOneTypeName(string candidate) =>
        candidate.Contains("ScheduleOne.", StringComparison.Ordinal) ||
        candidate.StartsWith("Il2CppScheduleOne.", StringComparison.Ordinal);

    private static string[] FindProjectManagedTypes(
        string signature,
        IReadOnlySet<string> projectClassNames,
        IReadOnlySet<string> injectedTypeNames)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(signature, @"(?<![A-Za-z0-9_])(?<type>[A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_])"))
        {
            string typeName = match.Groups["type"].Value;
            if (projectClassNames.Contains(typeName) && !injectedTypeNames.Contains(typeName))
            {
                found.Add(typeName);
            }
        }

        return found.ToArray();
    }

    private static bool HasRegisterTypeAttribute(string[] lines, int classLine)
    {
        for (int index = Math.Max(0, classLine - 6); index < classLine; index++)
        {
            if (lines[index].Contains("RegisterTypeInIl2Cpp", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasHideFromIl2CppAttribute(string[] lines, int memberLine)
    {
        for (int index = Math.Max(0, memberLine - 6); index < memberLine; index++)
        {
            if (lines[index].Contains("HideFromIl2Cpp", StringComparison.Ordinal))
            {
                return true;
            }

            if (lines[index].Contains("public ", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return false;
    }

    private static IReadOnlyList<ClassSpan> DiscoverClassSpans(string[] lines)
    {
        var spans = new List<ClassSpan>();
        for (int index = 0; index < lines.Length; index++)
        {
            Match match = ClassRegex.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            int endLine = FindClassEndLine(lines, index);
            spans.Add(new ClassSpan(
                match.Groups["name"].Value,
                match.Groups["base"].Success ? match.Groups["base"].Value.Trim() : string.Empty,
                index,
                endLine));
        }

        return spans;
    }

    private static int FindClassEndLine(string[] lines, int startLine)
    {
        bool started = false;
        int depth = 0;
        for (int index = startLine; index < lines.Length; index++)
        {
            foreach (char ch in lines[index])
            {
                if (ch == '{')
                {
                    started = true;
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                }
            }

            if (started && depth <= 0)
            {
                return index;
            }
        }

        return lines.Length - 1;
    }

    private static HashSet<string> DiscoverProjectClassNames(IEnumerable<string> sourceFiles)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string sourceFile in sourceFiles)
        {
            foreach (string line in File.ReadLines(sourceFile))
            {
                Match match = ClassRegex.Match(line);
                if (match.Success)
                {
                    names.Add(match.Groups["name"].Value);
                }
            }
        }

        return names;
    }

    private static HashSet<string> DiscoverInjectedTypeNames(IEnumerable<string> sourceFiles)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string sourceFile in sourceFiles)
        {
            string[] lines = File.ReadAllLines(sourceFile);
            foreach (ClassSpan span in DiscoverClassSpans(lines))
            {
                if (HasRegisterTypeAttribute(lines, span.StartLine) ||
                    IsLikelyIl2CppInjectedBase(span.BaseType))
                {
                    names.Add(span.Name);
                }
            }
        }

        return names;
    }

    private static bool IsLikelyIl2CppInjectedBase(string baseType)
    {
        string[] baseTypes = baseType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string candidate in baseTypes)
        {
            string simpleName = candidate.Split('.').Last();
            if (simpleName is "MonoBehaviour" or
                "InteractableObject" or
                "Equippable" or
                "VehicleData")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGeneratedOrBuildOutput(string projectDirectory, string file)
    {
        return WorkspaceTraversal.HasExcludedPathPart(projectDirectory, file, ExcludedDirectoryNames);
    }

    private sealed record ClassSpan(string Name, string BaseType, int StartLine, int EndLine);

    private sealed record ReflectionLookup(string Receiver, string Kind);
}
