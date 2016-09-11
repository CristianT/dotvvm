using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DotVVM.Framework.Compilation.Parser.Binding.Tokenizer;

namespace DotVVM.Framework.Compilation.Parser.Binding.Parser
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public abstract class BindingParserNode
    {
        protected string DebuggerDisplay => $"(S: {StartPosition}, L: {Length}, Err: {NodeErrors.Count}): {ToDisplayString()}";

        public int StartPosition { get; internal set; }

        public int Length { get; internal set; }


        public List<BindingToken> Tokens { get; internal set; }


        public List<string> NodeErrors { get; private set; }

        public bool HasNodeErrors
        {
            get { return NodeErrors.Any(); }
        }

        public IBindingParserNodeContext Context { get; set; }


        public BindingParserNode()
        {
            Tokens = new List<BindingToken>();
            NodeErrors = new List<string>();
        }


        public virtual IEnumerable<BindingParserNode> EnumerateNodes()
        {
            yield return this;
        }

        public BindingParserNode FindNodeByPosition(int position)
        {
            return EnumerateNodes().LastOrDefault(n => n.StartPosition <= position && position < n.StartPosition + n.Length);
        }

        public abstract IEnumerable<BindingParserNode> EnumerateChildNodes();

        public abstract string ToDisplayString();

    }
}