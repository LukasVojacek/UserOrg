// This file is used by Code Analysis to suppress certain warnings.
// CA1056 / CA1054: CallbackUrl is intentionally modeled as string because it is an opaque identifier
// understood by the ICallbackPort implementation. It may be a URL, a queue name, a topic name, etc.
// Forcing Uri would break consumers that use non-URL callback targets.
// CA1032: Exception constructors are intentionally limited — only (errorCode, message) and
// (errorCode, message, innerException) are valid; parameterless/message-only would produce
// meaningless "UNKNOWN" error codes.
// CA1716: The fluent flow-builder DSL deliberately uses verbs (Step, GoTo, When) and the parameter
// name "next" that happen to collide with VB reserved keywords. These names are the public authoring
// surface and renaming them for VB interop would harm the C# DSL's readability; this library targets C#.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "CallbackUrl is an opaque identifier, not necessarily a URL.", Scope = "member", Target = "~P:WorkflowEngine.Contracts.Abstractions.IRequestSnapshot.CallbackUrl")]
[assembly: SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "CallbackUrl is an opaque identifier, not necessarily a URL.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Models.SubmitOptions")]
[assembly: SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "CallbackUrl is an opaque identifier, not necessarily a URL.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Models.SubmitOptions")]
[assembly: SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Constructors intentionally require errorCode parameter.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Exceptions.PermanentException")]
[assembly: SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Constructors intentionally require errorCode parameter.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Exceptions.RetryableException")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Deliberate fluent DSL verbs; library targets C#.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Abstractions.IFlowBuilder")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Deliberate fluent DSL verbs; library targets C#.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Abstractions.ITransitionSource`1")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Deliberate fluent DSL verbs; library targets C#.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Abstractions.IBranch`1")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Deliberate fluent DSL verbs; library targets C#.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Abstractions.IGateTransitions`1")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Deliberate fluent DSL verbs; library targets C#.", Scope = "type", Target = "~T:WorkflowEngine.Contracts.Abstractions.IGateBranch`1")]