using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Scope = "module")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Scope = "module", Justification = "Test code does not run under a synchronization context; ConfigureAwait(false) adds noise without benefit.")]
