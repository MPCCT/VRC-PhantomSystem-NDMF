using System;
using nadena.dev.ndmf;

namespace MPCCT.PhantomSystem.Editor
{
    internal static class PhantomBuildPassRunner
    {
        public static void Run(BuildContext context, Action<BuildContext> action)
        {
            var state = context.GetState<PhantomBuildState>();
            Run(
                state.Report,
                () => action(context),
                context.AvatarRootObject,
                $"{action.Method.DeclaringType?.Name}.{action.Method.Name}");
        }

        internal static void Run(
            PhantomBuildReport report,
            Action action,
            UnityEngine.Object context = null,
            string passName = null)
        {
            if (!report.BeginPass())
            {
                return;
            }

            try
            {
                action();
                report.AbortIfErrors();
            }
            catch (PhantomBuildAbortException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var diagnosticName = string.IsNullOrWhiteSpace(passName)
                    ? $"{action.Method.DeclaringType?.Name}.{action.Method.Name}"
                    : passName;
                report.InternalError(
                    $"Pass '{diagnosticName}' threw unexpectedly.",
                    context,
                    exception);
                report.AbortIfErrors();
            }
        }
    }
}
