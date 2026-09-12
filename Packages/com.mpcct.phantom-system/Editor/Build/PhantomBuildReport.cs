using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using L = MPCCT.PhantomSystem.Editor.PhantomLocalization;

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

        public void Warning(string message, UnityEngine.Object context = null) =>
            Warning((PhantomDiagnostic)message, context);

        internal void Warning(PhantomDiagnostic message, UnityEngine.Object context = null)
        {
            // Warnings appear in NDMF's console, but never enter the blocking error list.
            ReportToNdmf(new PhantomBuildIssue(PhantomValidationSeverity.Warning, message, context, null));
        }

        public void Info(string message, UnityEngine.Object context = null)
        {
            Debug.Log("[PhantomSystem] " + message, context);
        }

        internal void Info(PhantomDiagnostic message, UnityEngine.Object context = null) =>
            Info(message?.ToString(), context);

        public void Error(string message, UnityEngine.Object context = null) =>
            Error((PhantomDiagnostic)message, context);

        internal void Error(PhantomDiagnostic message, UnityEngine.Object context = null)
        {
            issues.Add(new PhantomBuildIssue(
                PhantomValidationSeverity.ConfigurationError, message, context, null));
        }

        public void InternalError(
            string message,
            UnityEngine.Object context = null,
            Exception exception = null) =>
            InternalError((PhantomDiagnostic)message, context, exception);

        internal void InternalError(
            PhantomDiagnostic message,
            UnityEngine.Object context = null,
            Exception exception = null)
        {
            issues.Add(new PhantomBuildIssue(
                PhantomValidationSeverity.InternalError, message, context, exception));
        }

        public bool BeginPass() => !IsAborted;

        public void ThrowIfErrors() => AbortIfErrors();

        public void AbortIfErrors()
        {
            if (!HasErrors || IsAborted) return;

            IsAborted = true;
            for (var i = reportedErrorCount; i < issues.Count; i++)
            {
                ReportToNdmf(issues[i]);
            }
            reportedErrorCount = issues.Count;
            throw new PhantomBuildAbortException();
        }

        private static void ReportToNdmf(PhantomBuildIssue issue)
        {
            using (ErrorReport.WithContextObject(issue.Context))
            {
                ErrorReport.ReportError(new PhantomBuildError(issue));
            }
        }
    }

    internal sealed class PhantomBuildIssue
    {
        public PhantomValidationSeverity Severity { get; }
        internal PhantomDiagnostic Diagnostic { get; }
        public string Message => Severity == PhantomValidationSeverity.InternalError
            ? L.F("diagnostic.report.internal", Diagnostic)
            : Diagnostic?.ToString();
        public UnityEngine.Object Context { get; }
        public Exception Exception { get; }

        public PhantomBuildIssue(
            PhantomValidationSeverity severity,
            PhantomDiagnostic message,
            UnityEngine.Object context,
            Exception exception)
        {
            Severity = severity;
            Diagnostic = message;
            Context = context;
            Exception = exception;
        }

        public string DiagnosticMessage => Exception == null
            ? Message
            : $"{Message}\n{Exception}";
    }

    internal sealed class PhantomBuildAbortException : InvalidOperationException
    {
        public PhantomBuildAbortException()
            : base(L.S("diagnostic.report.aborted"))
        {
        }
    }

    internal sealed class PhantomBuildError : SimpleError
    {
        private readonly PhantomBuildIssue issue;

        public PhantomBuildError(PhantomBuildIssue issue) => this.issue = issue;

        public override Localizer Localizer => L.Localizer;
        public override string TitleKey => issue.Severity == PhantomValidationSeverity.Warning
            ? "diagnostic.report.warning"
            : "diagnostic.report.failed";
        public override ErrorSeverity Severity => issue.Severity == PhantomValidationSeverity.Warning
            ? ErrorSeverity.NonFatal
            : ErrorSeverity.Error;
        public override string FormatDetails() => issue.DiagnosticMessage;
        public override string ToMessage() => $"[PhantomSystem] {issue.DiagnosticMessage}";

        public override VisualElement CreateVisualElement(ErrorReport report) =>
            new SceneSafeError(this, report).CreateVisualElement(report);

        /// <summary>
        /// Keep NDMF's standard localized UI, but treat references to a closed scene
        /// as text. The underlying report and its object references stay intact.
        /// </summary>
        private sealed class SceneSafeError : SimpleError
        {
            private readonly PhantomBuildError source;
            private readonly ErrorReport report;

            internal SceneSafeError(PhantomBuildError source, ErrorReport report)
            {
                this.source = source;
                this.report = report;
            }

            public override Localizer Localizer => source.Localizer;
            public override string TitleKey => source.TitleKey;
            public override ErrorSeverity Severity => source.Severity;

            public override ObjectReference[] References => CanReadReportScene()
                ? source.References
                : source.References.Where(IsPersistent).ToArray();

            public override string FormatDetails()
            {
                var details = source.FormatDetails();
                if (CanReadReportScene()) return details;

                var unresolved = source.References
                    .Where(reference => !IsPersistent(reference))
                    .Select(reference => string.IsNullOrEmpty(reference.Path)
                        ? report?.AvatarRootPath ?? reference.ToString()
                        : reference.Path)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct()
                    .ToArray();
                return unresolved.Length == 0
                    ? details
                    : details + "\n" + string.Join("\n", unresolved);
            }

            private bool CanReadReportScene()
            {
                if (report == null) return false;
                try
                {
                    // NDMF 1.14 does not check Scene.IsValid before enumerating roots.
                    // A valid scene with a missing avatar is safe: NDMF shows plain text.
                    report.TryResolveAvatar(out _);
                    return true;
                }
                catch (ArgumentException)
                {
                    // Test Runner and scene changes can unload the report's scene.
                    return false;
                }
            }

            private static bool IsPersistent(ObjectReference reference) =>
                reference.Object != null && EditorUtility.IsPersistent(reference.Object);
        }
    }
}
