using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.UIElements;

namespace MPCCT.PhantomSystem.Editor
{
    public sealed class PhantomBuildReport
    {
        public List<string> Errors { get; } = new List<string>();

        private readonly List<UnityEngine.Object> errorContexts = new List<UnityEngine.Object>();
        private int reportedErrorCount;

        public void Warning(string message, UnityEngine.Object context = null)
        {
            Debug.LogWarning("[PhantomSystem] " + message, context);
        }

        public void Error(string message, UnityEngine.Object context = null)
        {
            Errors.Add(message);
            errorContexts.Add(context);
            Debug.LogError("[PhantomSystem] " + message, context);
        }

        public void ThrowIfErrors()
        {
            if (Errors.Count == 0)
            {
                return;
            }

            ReportPendingErrorsToNdmf();
            throw new System.InvalidOperationException("PhantomSystem build failed. See the NDMF Console for details.");
        }

        private void ReportPendingErrorsToNdmf()
        {
            for (var i = reportedErrorCount; i < Errors.Count; i++)
            {
                var context = i < errorContexts.Count ? errorContexts[i] : null;
                if (context != null)
                {
                    using (ErrorReport.WithContextObject(context))
                    {
                        ErrorReport.ReportError(new PhantomBuildError(Errors[i]));
                    }
                }
                else
                {
                    ErrorReport.ReportError(new PhantomBuildError(Errors[i]));
                }
            }

            reportedErrorCount = Errors.Count;
        }
    }

    internal sealed class PhantomBuildError : IError
    {
        private const string Title = "PhantomSystem build failed";
        private readonly string message;

        public PhantomBuildError(string message)
        {
            this.message = message;
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
            root.Add(new Label(message));
            return root;
        }

        public string ToMessage()
        {
            return $"[PhantomSystem] {message}";
        }
    }
}
