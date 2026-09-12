using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace MPCCT.PhantomSystem.Editor
{
    /// <summary>
    /// Keeps diagnostic templates and data separate until display time, including
    /// nested reasons. Parameter names, paths and exception details remain literal.
    /// </summary>
    internal sealed class PhantomDiagnostic
    {
        internal string Key { get; }
        private readonly object[] arguments;
        private readonly string literal;
        private readonly PhantomDiagnostic[] parts;
        private readonly string separator;

        private PhantomDiagnostic(string separator, PhantomDiagnostic[] parts)
        {
            this.separator = separator;
            this.parts = parts;
        }

        internal static PhantomDiagnostic Join(string separator, IEnumerable<PhantomDiagnostic> parts) =>
            new PhantomDiagnostic(separator, parts.ToArray());

        internal PhantomDiagnostic(string key, object[] arguments)
        {
            Key = key;
            this.arguments = (object[])arguments.Clone();
        }

        private PhantomDiagnostic(string literal) => this.literal = literal;

        public override string ToString()
        {
            if (parts != null) return string.Join(separator, parts.Select(part => part?.ToString()));
            return Key == null
                ? literal
                : string.Format(CultureInfo.CurrentCulture, PhantomLocalization.S(Key), arguments);
        }

        public static implicit operator PhantomDiagnostic(string literal) =>
            literal == null ? null : new PhantomDiagnostic(literal);

        public static implicit operator string(PhantomDiagnostic diagnostic) => diagnostic?.ToString();
    }
}
