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
    }

    private static bool IsHarmonyTranspilerLine(string line) =>
        line.Contains("HarmonyTranspiler", StringComparison.Ordinal) ||
        line.Contains("IEnumerable<CodeInstruction>", StringComparison.Ordinal) ||
        line.Contains("CodeInstruction", StringComparison.Ordinal) && line.Contains("Transpiler", StringComparison.Ordinal);

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
        Match fieldLookup = ReflectionLookupReceiverRegex.Matches(line)
            .FirstOrDefault(match => match.Groups["kind"].Value == "Field")!;
        if (fieldLookup is null || !fieldLookup.Success)
        {
            return false;
        }

        string receiver = NormalizeReflectionReceiver(fieldLookup.Groups["receiver"].Value);
        string forwardWindow = GetSourceWindow(lines, index, maxLineCount: 16);
        string backwardWindow = GetSourceWindow(lines, Math.Max(0, index - 15), Math.Min(16, index + 1));
        return HasReflectionLookup(forwardWindow, receiver, "Property") ||
               HasReflectionLookup(backwardWindow, receiver, "Property");
    }

    private static bool IsDirectMemberReflectionLookupLine(IReadOnlyList<string> lines, int index)
    {
        string line = lines[index];
        if (!TypeOfFieldOrPropertyLookupRegex.IsMatch(line))
        {
            return false;
        }

        string sourceWindow = GetSourceWindow(lines, index, maxLineCount: 16);
        string backwardWindow = GetSourceWindow(lines, Math.Max(0, index - 15), Math.Min(16, index + 1));
        return !HasFieldPropertyFallbackPair(sourceWindow) &&
               !HasFieldPropertyFallbackPair(backwardWindow);
    }

    private static bool HasFieldPropertyFallbackPair(string sourceWindow)
    {
        foreach (Match match in ReflectionLookupReceiverRegex.Matches(sourceWindow))
        {
            if (match.Groups["kind"].Value != "Field")
            {
                continue;
            }

            if (HasReflectionLookup(sourceWindow, NormalizeReflectionReceiver(match.Groups["receiver"].Value), "Property"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReflectionLookup(string sourceWindow, string receiver, string kind) =>
        ReflectionLookupReceiverRegex.Matches(sourceWindow)
            .Any(match =>
                match.Groups["kind"].Value == kind &&
                NormalizeReflectionReceiver(match.Groups["receiver"].Value) == receiver);

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
}
