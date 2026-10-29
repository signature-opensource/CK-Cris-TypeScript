// The engine must see the command handlers of the scheme: no type of their assembly is used here, the compiler would
// drop the reference.
[assembly: CK.Setup.PreserveAssemblyReference( typeof( CK.Auth.PasskeyHandlers ) )]
