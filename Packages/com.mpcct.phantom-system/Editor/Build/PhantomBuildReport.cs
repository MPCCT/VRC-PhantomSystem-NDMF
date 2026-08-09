using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.UIElements;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed class PhantomBuildReport
    {
        private readonly List<PhantomBuildIssue> issues = new List<PhantomBuildIssue>();

        public IReadOnlyList<string> Errors => issues.Select(issue => issue.Message).ToList();
        internal IReadOnlyList<PhantomBuildIssue> Issues => issues;
        public bool HasErrors => issues.Count > 0;
        public bool IsAborted { get; private set; }

        private int reportedErrorCount;

        public void Warning(string message, UnityEngine.Object context = null)
        {
            Debug.LogWarning("[PhantomSystem] " + message, context);
        }

        public void Info(string message, UnityEngine.Object context = null)
        {
            Debug.Log("[PhantomSystem] " + message, context);
        }

        public void Error(string message, UnityEngine.Object context = null)
        {
            issues.Add(new PhantomBuildIssue(
                PhantomValidationSeverity.ConfigurationError,
                message,
                context,
                null));
        }

        public void InternalError(
            string message,
            UnityEngine.Object context = null,
            System.Exception exception = null)
        {
            issues.Add(new PhantomBuildIssue(
                PhantomValidationSeverity.InternalError,
                "Internal build error: " + message,
                context,
                exception));
        }

        public bool BeginPass()
        {
            return !IsAborted;
        }

        public void ThrowIfErrors()
        {
            AbortIfErrors();
        }

        public void AbortIfErrors()
        {
            if (!HasErrors || IsAborted)
            {
                return;
            }

            IsAborted = true;
            ReportPendingErrorsToNdmf();
            throw new PhantomBuildAbortException();
        }

        private void ReportPendingErrorsToNdmf()
        {
            for (var i = reportedErrorCount; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (issue.Context != null)
                {
                    using (ErrorReport.WithContextObject(issue.Context))
                    {
                        ErrorReport.ReportError(new PhantomBuildError(issue));
                    }
                }
                else
                {
                    ErrorReport.ReportError(new PhantomBuildError(issue));
                }
            }

            reportedErrorCount = issues.Count;
        }
    }

    internal sealed class PhantomBuildIssue
    {
        public PhantomValidationSeverity Severity { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }
        public Exception Exception { get; }

        public PhantomBuildIssue(
            PhantomValidationSeverity severity,
            string message,
            UnityEngine.Object context,
            Exception exception)
        {
            Severity = severity;
            Message = message;
            Context = context;
            Exception = exception;
        }

        public string DiagnosticMessage => Exception == null
            ? Message
            : $"{Message}\n{Exception}";
    }

    internal sealed class PhantomBuildAbortException : System.InvalidOperationException
    {
        public PhantomBuildAbortException()
            : base("PhantomSystem build failed. See the NDMF Console for details.")
        {
        }
    }

    internal sealed class PhantomBuildError : IError
    {
        private const string Title = "PhantomSystem build failed";
        private readonly PhantomBuildIssue issue;

        public PhantomBuildError(PhantomBuildIssue issue)
        {
            this.issue = issue;
        }

        public ErrorSeverity Severity => ErrorSeverity.Error;

        public void AddReference(ObjectReference obj)
        {
            // Required by IError. PhantomSystem supplies its context through
            // ErrorReport.WithContextObject when the error is reported.
        }

        public VisualElement CreateVisualElement(ErrorReport report)
        {
            var root = new VisualElement();
            var title = new Label(Title);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;

            root.Add(title);
            root.Add(new Label(issue.DiagnosticMessage));
            return root;
        }

        public string ToMessage()
        {
            return $"[PhantomSystem] {issue.DiagnosticMessage}";
        }
    }
}
