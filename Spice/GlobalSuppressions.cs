// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// CA1720: Identifiers should not contain type names
// Suppressed for Param class factory methods that intentionally use Arrow type names
// for API clarity and consistency with Java/Go SDKs.
[assembly: SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Factory methods intentionally named after Arrow types for API clarity and SDK consistency",
    Scope = "type",
    Target = "~T:Spice.Params.Param")]

// CA1001: Types that own disposable fields should be disposable
// SpiceClientBuilder transfers ownership of the SpiceClient to the caller via Build().
// The builder is not responsible for disposal - the caller becomes the owner.
[assembly: SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Builder pattern transfers ownership to caller via Build() method",
    Scope = "type",
    Target = "~T:Spice.SpiceClientBuilder")]
