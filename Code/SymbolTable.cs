using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace RoslynTool
{
    internal class HookInfo
    {
        internal string FullClassName = string.Empty;
        internal string BeginMethodName = string.Empty;
        internal string EndMethodName = string.Empty;
    }
    internal class InjectInfo
    {
        internal HookInfo MemoryLog = null;
        internal HookInfo ProfilerSample = null;
        internal List<Regex> ExcludeAssemblies = new List<Regex>();
        internal List<Regex> IncludeAssemblies = new List<Regex>();
        internal List<Regex> DontInjects = new List<Regex>();
        internal List<Regex> Injects = new List<Regex>();
    }
    internal class SymbolTable
    {
        internal CSharpCompilation Compilation
        {
            get { return m_Compilation; }
        }
        internal IAssemblySymbol AssemblySymbol
        {
            get { return m_AssemblySymbol; }
        }
        internal HashSet<string> Namespaces
        {
            get { return m_Namespaces; }
        }
        internal void Init(CSharpCompilation compilation, string cfgPath)
        {
            m_Compilation = compilation;
            m_AssemblySymbol = compilation.Assembly;
            INamespaceSymbol nssym = m_AssemblySymbol.GlobalNamespace;
            InitRecursively(nssym);

            Dsl.DslFile dslFile = new Dsl.DslFile();
            if (dslFile.Load(Path.Combine(cfgPath, "hookrewriter.dsl"), (msg) => { Console.WriteLine(msg); })) {
                foreach (var info in dslFile.DslInfos) {
                    var func = info.First;
                    var call = func.Call;
                    var fid = info.GetId();
                    if (fid != "project")
                        continue;
                    var cid = call.GetParamId(0);
                    List<InjectInfo> list;
                    if (!m_InjectInfos.TryGetValue(cid, out list)) {
                        list = new List<InjectInfo>();
                        m_InjectInfos.Add(cid, list);                        
                    }
                    var injectInfo = new InjectInfo();
                    list.Add(injectInfo);
                    foreach (var comp in func.Statements) {
                        var cd = comp as Dsl.CallData;
                        if (null != cd) {
                            var mid = cd.GetId();
                            if (mid == "InjectMemoryLog") {
                                if (cd.GetParamNum() >= 3) {
                                    var c = cd.GetParamId(0);
                                    var m1 = cd.GetParamId(1);
                                    var m2 = cd.GetParamId(2);
                                    injectInfo.MemoryLog = new HookInfo { FullClassName = c, BeginMethodName = m1, EndMethodName = m2 };
                                } else {
                                    injectInfo.MemoryLog = new HookInfo { FullClassName = "Utility", BeginMethodName = "MemoryLogBegin", EndMethodName = "MemoryLogEnd" };
                                }
                            } else if (mid == "InjectProfilerSample") {
                                if (cd.GetParamNum() >= 3) {
                                    var c = cd.GetParamId(0);
                                    var m1 = cd.GetParamId(1);
                                    var m2 = cd.GetParamId(2);
                                    injectInfo.ProfilerSample = new HookInfo { FullClassName = c, BeginMethodName = m1, EndMethodName = m2 };
                                } else {
                                    injectInfo.ProfilerSample = new HookInfo { FullClassName = "UnityEngine.Profiling.Profiler", BeginMethodName = "BeginSample", EndMethodName = "EndSample" };
                                }
                            } else if (mid == "ExcludeAssembly") {
                                var r = cd.GetParamId(0);
                                var regex = new Regex(r, RegexOptions.Compiled);
                                injectInfo.ExcludeAssemblies.Add(regex);
                            } else if (mid == "IncludeAssembly") {
                                var r = cd.GetParamId(0);
                                var regex = new Regex(r, RegexOptions.Compiled);
                                injectInfo.IncludeAssemblies.Add(regex);
                            } else if (mid == "DontInject") {
                                var r = cd.GetParamId(0);
                                var regex = new Regex(r, RegexOptions.Compiled);
                                injectInfo.DontInjects.Add(regex);
                            } else if (mid == "Inject") {
                                var r = cd.GetParamId(0);
                                var regex = new Regex(r, RegexOptions.Compiled);
                                injectInfo.Injects.Add(regex);
                            }
                        }
                    }
                }
            }
        }
        internal List<InjectInfo> GetInjectInfos(string project)
        {
            List<InjectInfo> list;
            m_InjectInfos.TryGetValue(project, out list);
            return list;
        }
        
        private void InitRecursively(INamespaceSymbol nssym)
        {
            string ns = GetNamespaces(nssym);
            m_Namespaces.Add(ns);
            foreach (var newSym in nssym.GetNamespaceMembers()) {
                InitRecursively(newSym);
            }
        }
        
        private SymbolTable() { }

        private CSharpCompilation m_Compilation = null;
        private IAssemblySymbol m_AssemblySymbol = null;
        private HashSet<string> m_Namespaces = new HashSet<string>();
        private Dictionary<string, List<InjectInfo>> m_InjectInfos = new Dictionary<string, List<InjectInfo>>();
        
        internal static SymbolTable Instance
        {
            get { return s_Instance; }
        }
        private static SymbolTable s_Instance = new SymbolTable();
        
        internal static string CalcFullNameWithTypeParameters(ISymbol type, bool includeSelfName)
        {
            if (null == type)
                return string.Empty;
            List<string> list = new List<string>();
            if (includeSelfName) {
                list.Add(CalcNameWithTypeParameters(type));
            }
            INamespaceSymbol ns = type.ContainingNamespace;
            var ct = type.ContainingType;
            string name = string.Empty;
            if (null != ct) {
                name = CalcNameWithTypeParameters(ct);
            }
            while (null != ct && name.Length > 0) {
                list.Insert(0, name);
                ns = ct.ContainingNamespace;
                ct = ct.ContainingType;
                if (null != ct) {
                    name = CalcNameWithTypeParameters(ct);
                } else {
                    name = string.Empty;
                }
            }
            while (null != ns && ns.Name.Length > 0) {
                list.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", list.ToArray());
        }
        internal static string CalcNameWithTypeParameters(ISymbol sym)
        {
            if (null == sym)
                return string.Empty;
            var typeSym = sym as INamedTypeSymbol;
            if (null != typeSym) {
                return CalcNameWithTypeParameters(typeSym);
            } else {
                return sym.Name;
            }
        }
        internal static string CalcNameWithTypeParameters(INamedTypeSymbol type)
        {
            if (null == type)
                return string.Empty;
            List<string> list = new List<string>();
            list.Add(type.Name);
            foreach (var param in type.TypeParameters) {
                list.Add(param.Name);
            }
            return string.Join("_", list.ToArray());
        }
        private static string GetNamespaces(INamespaceSymbol ns)
        {
            List<string> list = new List<string>();
            while (null != ns && ns.Name.Length > 0) {
                list.Insert(0, ns.Name);
                ns = ns.ContainingNamespace;
            }
            return string.Join(".", list.ToArray());
        }
    }
}
