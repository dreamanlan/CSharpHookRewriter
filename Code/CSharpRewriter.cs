using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace RoslynTool
{
    internal class CreateChecker : CSharpSyntaxWalker
    {
        public bool ExistCreate
        {
            get { return m_ExistCreate; }
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            m_ExistCreate = true;
            base.VisitObjectCreationExpression(node);
        }
        public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
        {
            m_ExistCreate = true;
            base.VisitAnonymousObjectCreationExpression(node);
        }
        public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            m_ExistCreate = true;
            base.VisitArrayCreationExpression(node);
        }
        public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
        {
            m_ExistCreate = true;
            base.VisitImplicitArrayCreationExpression(node);
        }
        public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
        {
            m_ExistCreate = true;
            base.VisitStackAllocArrayCreationExpression(node);
        }
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (!m_ExistCreate) {
                var symInfo = m_Model.GetSymbolInfo(node);
                var sym = symInfo.Symbol;
                if (null != sym && SymbolTable.Instance.AssemblySymbol != sym.ContainingAssembly) {
                    bool exclude = false;
                    var infos = SymbolTable.Instance.GetInjectInfos(m_ProjectFileName);
                    foreach (var info in infos) {
                        if (info.ExcludeAssemblies.Contains(sym.ContainingAssembly.Name)) {
                            exclude = true;
                            break;
                        }
                    }
                    if (!exclude) {
                        bool existInclude = false;
                        bool include = false;
                        foreach (var info in infos) {
                            if (info.IncludeAssemblies.Count > 0) {
                                existInclude = true;
                            }
                            if (info.IncludeAssemblies.Contains(sym.ContainingAssembly.Name)) {
                                include = true;
                                break;
                            }
                        }
                        if (existInclude) {
                            m_ExistCreate = include;
                        } else {
                            m_ExistCreate = true;
                        }
                    }
                }
            }
            base.VisitInvocationExpression(node);
        }

        public CreateChecker(string name, SemanticModel model)
        {
            m_Model = model;
            m_ProjectFileName = name;
        }

        private SemanticModel m_Model = null;
        private string m_ProjectFileName = string.Empty;
        private bool m_ExistCreate = false;
    }
    internal class CSharpRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            var methodDecl = node.Parent as BaseMethodDeclarationSyntax;
            var accessorDecl = node.Parent as AccessorDeclarationSyntax;
            if (null != methodDecl) {
                var sym = m_Model.GetDeclaredSymbol(methodDecl);
                var fullName = SymbolTable.CalcFullNameWithTypeParameters(sym, true);
                var newNode = TryInject(node, sym, fullName);
                if (null != newNode)
                    return newNode;
            } else if (null != accessorDecl) {
                var sym = m_Model.GetDeclaredSymbol(accessorDecl);
                var fullName = SymbolTable.CalcFullNameWithTypeParameters(sym, true);
                var newNode = TryInject(node, sym, fullName);
                if (null != newNode)
                    return newNode;
            }
            return base.VisitBlock(node);
        }
        
        private SyntaxNode TryInject(BlockSyntax node, IMethodSymbol sym, string fullName)
        {
            if (null != m_InjectInfos) {
                HookInfo mlog = null;
                HookInfo psample = null;
                foreach (var info in m_InjectInfos) {
                    bool exclude = false;
                    foreach (var regex in info.DontInjects) {
                        if (regex.IsMatch(fullName)) {
                            exclude = true;
                            break;
                        }
                    }
                    if (!exclude) {
                        bool include = false;
                        if (info.Injects.Count <= 0) {
                            include = true;
                        } else {
                            foreach (var regex in info.Injects) {
                                if (regex.IsMatch(fullName)) {
                                    include = true;
                                    break;
                                }
                            }
                        }
                        if (include) {
                            if (null != info.MemoryLog) {
                                CreateChecker checker = new CreateChecker(m_ProjectFileName, m_Model);
                                checker.Visit(node);
                                if (checker.ExistCreate) {
                                    mlog = info.MemoryLog;
                                }
                            }
                            if (null != info.ProfilerSample) {
                                psample = info.ProfilerSample;
                            }
                        }
                    }
                }
                if (null != mlog || null != psample) {
                    if (null != mlog) {
                        node = InjectMemoryLog(node, sym, fullName, mlog);
                    }
                    if (null != psample) {
                        node = InjectProfilerSample(node, sym, fullName, psample);
                    }
                    return node;
                }
            }
            return null;
        }
        private BlockSyntax InjectMemoryLog(BlockSyntax node, IMethodSymbol sym, string fullName, HookInfo hookInfo)
        {
            var firstLeading = node.GetLeadingTrivia().ToFullString();
            var txt = node.ToFullString();            

            int ix = 0;
            while (ix < firstLeading.Length && (firstLeading[ix] == '\t' || firstLeading[ix] == ' '))
                ++ix;

            string leading = firstLeading.Substring(0, ix);
            int indent = 0;
            var sb = new StringBuilder();
            sb.AppendFormat("{0}{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}try{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}{1}.{2}(\"{3}\");", GetIndent(leading, indent), hookInfo.FullClassName, hookInfo.BeginMethodName, fullName);
            sb.AppendLine();
            sb.Append(txt);

            --indent;
            sb.AppendFormat("{0}}}finally{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}{1}.{2}(\"{3}\");", GetIndent(leading, indent), hookInfo.FullClassName, hookInfo.EndMethodName, fullName);
            sb.AppendLine();

            --indent;
            sb.AppendFormat("{0}}}", GetIndent(leading, indent));
            sb.AppendLine();

            --indent;
            sb.AppendFormat("{0}}}", GetIndent(leading, indent));
            sb.AppendLine();

            var newNode = SyntaxFactory.ParseStatement(sb.ToString());
            return newNode as BlockSyntax;
        }
        private BlockSyntax InjectProfilerSample(BlockSyntax node, IMethodSymbol sym, string fullName, HookInfo hookInfo)
        {
            var firstLeading = node.GetLeadingTrivia().ToFullString();
            var txt = node.Statements.ToFullString();

            int ix = 0;
            while (ix < firstLeading.Length && (firstLeading[ix] == '\t' || firstLeading[ix] == ' '))
                ++ix;

            string leading = firstLeading.Substring(0, ix);
            int indent = 0;
            var sb = new StringBuilder();
            sb.AppendFormat("{0}{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}try{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}{1}.{2}(\"{3}\");", GetIndent(leading, indent), hookInfo.FullClassName, hookInfo.BeginMethodName, fullName);
            sb.AppendLine();
            sb.Append(txt);

            --indent;
            sb.AppendFormat("{0}}}finally{{", GetIndent(leading, indent));
            sb.AppendLine();
            ++indent;

            sb.AppendFormat("{0}{1}.{2}();", GetIndent(leading, indent), hookInfo.FullClassName, hookInfo.EndMethodName);
            sb.AppendLine();
            
            --indent;
            sb.AppendFormat("{0}}}", GetIndent(leading, indent));
            sb.AppendLine();

            --indent;
            sb.AppendFormat("{0}}}", GetIndent(leading, indent));
            sb.AppendLine();

            var newNode = SyntaxFactory.ParseStatement(sb.ToString());
            return newNode as BlockSyntax;
        }
        private string GetIndent(string leading, int indent)
        {
            return leading + c_IndentString.Substring(0, indent);
        }

        public CSharpRewriter(string name, SemanticModel model)
        {
            m_Model = model;
            m_ProjectFileName = name;
            m_InjectInfos = SymbolTable.Instance.GetInjectInfos(name);
        }

        private SemanticModel m_Model = null;
        private string m_ProjectFileName = string.Empty;
        private List<InjectInfo> m_InjectInfos = new List<InjectInfo>();

        private const string c_IndentString = "\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t\t";
    }
}
