using CK.Setup;

namespace CK.Cris;

/// <summary>
/// Triggers the implementation of the Cris commands (this extends the
/// Poco TypeScript export).
/// <para>
/// This class should be static but because of the stupid https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0718
/// it is not so it can be used as a generic argument.
/// </para>
/// </summary>
[ContextBoundDelegation( "CK.Setup.TypeScriptCrisCommandGeneratorImpl, CK.Cris.AspNet.Engine" )]
public sealed class TypeScriptCrisCommandGenerator
{
}
