using CK.Core;
using CK.Cris.AmbientValues;
using CK.Poco.Exc.Json;
using CK.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CK.Cris.AspNet;

/// <summary>
/// Primary service that handles frontend commands (or commands sent on http).
/// <para>
/// A command is accepted only on its route (see <see cref="CrisRouteAttribute"/>): the routes are generated on the commands.
/// </para>
/// </summary>
[ContextBoundDelegation( "CK.Setup.Cris.CrisAspNetServiceImpl, CK.Cris.AspNet.Engine" )]
[SingletonContainerConfiguredService]
[AlsoRegisterType<CrisDirectory,
                  TypeScriptCrisCommandGenerator,
                  CommonPocoJsonSupport,
                  RawCrisReceiver,
                  ICrisCallResult,
                  CrisBackgroundExecutorService,
                  IAmbientValuesCollectCommand,
                  CrisCultureService>]
// Commands without ambient services are executed inline by the CrisExecutionContext resolved
// from the request services.
[AlsoRegisterType<CrisExecutionContext>]
public abstract partial class CrisAspNetService : ISingletonAutoService
{
    /// <summary>
    /// The default route of the commands: "/.cris".
    /// </summary>
    public static readonly PathString DefaultRoute = new PathString( "/.cris" );

    // Legacy: the HttpCrisSender route accepts the commands of the default route.
    static readonly PathString _netRoute = new PathString( "/.cris/net" );

    /// <summary>
    /// Generated code infrastructure: implemented by every command. The route is generated
    /// on the commands that have a declared route (see <see cref="CrisRouteAttribute"/>).
    /// </summary>
    public interface ICrisRouteImpl
    {
        /// <summary>
        /// Gets the declared route of the command. Null for the <see cref="DefaultRoute"/>.
        /// </summary>
        string? CrisRoute => null;
    }

    readonly RawCrisReceiver _validator;
    readonly CrisBackgroundExecutorService _backgroundExecutor;
    internal readonly PocoDirectory _pocoDirectory;
    readonly IPocoFactory<ICrisCallResult> _resultFactory;
    readonly IPocoFactory<ICrisResultError> _crisErrorResultFactory;

    /// <summary>
    /// Initializes a new <see cref="CrisAspNetService"/>.
    /// </summary>
    /// <param name="poco">The Poco directory.</param>
    /// <param name="validator">The raw validator service.</param>
    /// <param name="backgroundExecutor">The background executor service.</param>
    /// <param name="resultFactory">The AspNet result factory.</param>
    /// <param name="errorResultFactory">The Cris error factory.</param>
    protected CrisAspNetService( PocoDirectory poco,
                                 RawCrisReceiver validator,
                                 CrisBackgroundExecutorService backgroundExecutor,
                                 IPocoFactory<ICrisCallResult> resultFactory,
                                 IPocoFactory<ICrisResultError> errorResultFactory )
    {
        _pocoDirectory = poco;
        _validator = validator;
        _backgroundExecutor = backgroundExecutor;
        _resultFactory = resultFactory;
        _crisErrorResultFactory = errorResultFactory;
    }

    /// <summary>
    /// Gets the route of a command: its declared route (see <see cref="CrisRouteAttribute"/>) or the <see cref="DefaultRoute"/>.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The route of the command.</returns>
    public static PathString GetRoute( IAbstractCommand command )
    {
        return ((ICrisRouteImpl)command).CrisRoute is string r ? new PathString( r ) : DefaultRoute;
    }

    /// <summary>
    /// Handles a command request parsed with the provided <paramref name="reader"/>: the command is
    /// validated and executed by the <see cref="CrisBackgroundExecutorService"/> or inline if it can.
    /// <para>
    /// Any specific input processing or pre processing can be done by the <paramref name="reader"/> function.
    /// Output of the <see cref="ICrisCallResult"/> MUST use the returned TypeFilterName.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="request">The http request.</param>
    /// <param name="reader">The payload reader.</param>
    /// <param name="currentCultureInfo">Optional current culture.</param>
    /// <param name="readOptions">Optional read options.</param>
    /// <param name="executeOverride">
    /// Optional execution override, called once the command is successfully validated: when it returns a task, the command
    /// is not executed here and the task's result is the command result. It returns null to let the command be executed here.
    /// (The authentication relays the session commands to a remote authentication center this way.)
    /// </param>
    /// <returns>The command result and the <see cref="ExchangeableRuntimeFilter.Name"/> to use to send the response.</returns>
    public async Task<(ICrisCallResult Result, string TypeFilterName)> HandleRequestAsync( IActivityMonitor monitor,
                                                                                           HttpRequest request,
                                                                                           CommandRequestReader reader,
                                                                                           CurrentCultureInfo? currentCultureInfo = null,
                                                                                           PocoJsonImportOptions? readOptions = null,
                                                                                           Func<IAbstractCommand, Task<object?>?>? executeOverride = null )
    {
        // There is no try catch here and this is intended. An unhandled exception here
        // is an Internal Server Error that should bubble up.
        var requestServices = request.HttpContext.RequestServices;
        using( HandleIncomingCKDepToken( monitor, request, out var depToken ) )
        {
            // If we cannot read the command, it is considered as a Validation error.
            var readResult = await ReadCommandAsync( monitor,
                                                     request,
                                                     reader,
                                                     currentCultureInfo,
                                                     readOptions );
            Throw.DebugAssert( readResult.TypeFilterName != null );
            AmbientServiceHub? ambientServiceHub = null;
            IEnumerable<UserMessage> allValidationMessages = readResult.ValidationMessages.UserMessages;
            ICrisCallResult? result = readResult.ReadResultError;
            if( result == null )
            {
                // No read error => no validation error (and we have a command).
                Throw.DebugAssert( readResult.Command != null );
                // A command is accepted only on its route, and a route accepts only its commands.
                var expected = GetRoute( readResult.Command );
                var path = request.Path == _netRoute ? DefaultRoute : request.Path;
                if( path != expected )
                {
                    var refused = $"Command '{readResult.Command.CrisPocoModel.PocoName}' must be sent to '{expected}'.";
                    monitor.Warn( $"{refused} It has been sent to '{request.Path}'." );
                    readResult.ValidationMessages.Error( refused, "Cris.AspNet.RouteRefused" );
                    result = CreateValidationErrorResult( readResult.ValidationMessages.UserMessages, null );
                }
            }
            if( result == null )
            {
                Throw.DebugAssert( readResult.Command != null );
                // Incoming command validation.
                CrisValidationResult validation = await _validator.IncomingValidateAsync( monitor, requestServices, readResult.Command );
                allValidationMessages = allValidationMessages.Concat( validation.ValidationMessages );
                if( !validation.Success )
                {
                    result = CreateValidationErrorResult( allValidationMessages, validation.LogKey );
                }
                ambientServiceHub = validation.AmbientServiceHub;
            }
            // No result so far: incoming validation succeeded. The execution may be overridden.
            if( result == null && executeOverride?.Invoke( readResult.Command! ) is Task<object?> overridden )
            {
                result = _resultFactory.Create();
                result.Result = await overridden;
                if( allValidationMessages.Any() ) result.ValidationMessages = allValidationMessages.ToList();
            }
            if( result == null )
            {
                Throw.DebugAssert( readResult.Command != null && readResult.TypeFilterName != null );
                // If we have a AmbientServiceHub, then we must use the background executor.
                IExecutedCommand executedCommand;
                if( ambientServiceHub != null )
                {
                    var executing = _backgroundExecutor.Submit( monitor, readResult.Command, ambientServiceHub, issuerToken: depToken, incomingValidationCheck: false );
                    executedCommand = await executing.ExecutedCommand;
                    Throw.DebugAssert( executedCommand.DeferredExecutionContext == executing );
                }
                else
                {
                    var execContext = requestServices.GetRequiredService<CrisExecutionContext>();
                    // Don't combine incoming validation message here as we don't expose the executed command: the messages
                    // are built below for the IAspNetCrisResult.
                    executedCommand = await execContext.ExecuteRootCommandAsync( readResult.Command );
                    Throw.DebugAssert( executedCommand.DeferredExecutionContext == null );
                }
                // Build the IAspNetCrisResult.
                result = _resultFactory.Create();
                result.Result = executedCommand.Result;
                if( allValidationMessages.Any() || executedCommand.ValidationMessages.Length > 0 )
                {
                    result.ValidationMessages = allValidationMessages
                                                    .Concat( executedCommand.ValidationMessages )
                                                    .ToList();
                }
            }
            var correlationToken = monitor.CreateToken();
            result.CorrelationId = correlationToken.ToString();
            // If its an error without LogKey, use the one of the correlation token.
            if( result.Result is ICrisResultError e && e.LogKey == null )
            {
                e.LogKey = correlationToken.Key;
            }
            // A Cris result HTTP status code must always be 200 OK (except on Internal Server Error).
            request.HttpContext.Response.StatusCode = 200;
            return (result, readResult.TypeFilterName);
        }
    }

    static IDisposable? HandleIncomingCKDepToken( IActivityMonitor monitor, HttpRequest request, out ActivityMonitor.Token? token )
    {
        // This handles the first valid token if multiple tokens are provided (and StringValues enumerator is fast).
        // Multiple tokens makes no real sense.
        foreach( var t in request.Headers["CKDepToken"] )
        {
            if( ActivityMonitor.Token.TryParse( t, out token ) )
            {
                return monitor.StartDependentActivity( token );
            }
            monitor.Warn( $"Invalid request CKDepToken header value: '{t}'. Ignored." );
        }
        token = null;
        return null;
    }

    record struct ReadResult( IAbstractCommand? Command, ICrisCallResult? ReadResultError, string TypeFilterName, UserMessageCollector ValidationMessages );

    async Task<ReadResult> ReadCommandAsync( IActivityMonitor monitor,
                                             HttpRequest request,
                                             CommandRequestReader reader,
                                             CurrentCultureInfo? currentCultureInfo,
                                             PocoJsonImportOptions? readOptions )
    {
        currentCultureInfo ??= request.HttpContext.RequestServices.GetRequiredService<CurrentCultureInfo>();
        var messageCollector = new UserMessageCollector( currentCultureInfo );
        if( readOptions == null && !TryCreateJsonImportOptions( request, messageCollector, out readOptions ) )
        {
            // If we cannot read the TypeFilterName then we use the "AllExchangeable" type set to return the validation error message.
            return new ReadResult( null, CreateValidationErrorResult( messageCollector.UserMessages, null ), "AllExchangeable", messageCollector );
        }
        Throw.DebugAssert( "We now have a TypeFilterName.", readOptions.TypeFilterName != null );
        int length = -1;
        using( var buffer = Util.RecyclableStreamManager.GetStream() )
        {
            try
            {
                await request.Body.CopyToAsync( buffer );
                length = (int)buffer.Position;
                if( length > 0 )
                {
                    var cmd = await reader( monitor, request, _pocoDirectory, messageCollector, buffer.GetReadOnlySequence(), readOptions );
                    if( cmd != null )
                    {
                        if( messageCollector.ErrorCount > 0 )
                        {
                            using( monitor.OpenWarn( $"Command '{cmd}' has been successfuly read but {messageCollector.ErrorCount} error messages have been emitted." ) )
                            {
                                foreach( var e in messageCollector.UserMessages.Where( m => m.Level == UserMessageLevel.Error ) )
                                {
                                    monitor.Warn( e.Text );
                                }
                            }
                        }
                        return new ReadResult( cmd, null, readOptions.TypeFilterName, messageCollector );
                    }
                    else
                    {
                        if( messageCollector.ErrorCount == 0 )
                        {
                            monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, "The command reader returned null but no error has been emitted." );
                            messageCollector.Error( "Request failed to be read without explicit error.", "Cris.AspNet.ReadNullCommandMissingError" );
                        }
                    }
                }
                else
                {
                    messageCollector.Error( "Unable to read Command Poco from empty request body.", "Cris.AspNet.EmptyBody" );
                }
                return new ReadResult( null, CreateValidationErrorResult( messageCollector.UserMessages, null ), readOptions.TypeFilterName, messageCollector );
            }
            catch( Exception ex )
            {
                messageCollector.Error( $"Unable to read Command Poco from request body (byte length = {length}).", "Cris.AspNet.ReadCommandFailed" );
                // The body is never logged: it may contain sensitive data (a login password for instance).
                using var gError = monitor.OpenError( messageCollector.UserMessages[^1].Message.CodeString, ex );
                return new ReadResult( null, CreateValidationErrorResult( messageCollector.UserMessages, gError.GetLogKeyString() ), readOptions.TypeFilterName, messageCollector );
            }
        }
    }

    /// <summary>
    /// Helper that reads optional "UsePascalCase", "Indented", "TypeLess", "UnsafeRelaxedJsonEscaping"
    /// and optionally "SkipValidation" keys from request's query to build the options that can be used
    /// to write the json result.
    /// <para>
    /// This always sets the <see cref="PocoJsonExportOptions.AlwaysExportSimpleUserMessage"/> to true.
    /// </para>
    /// <para>
    /// Using the <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> is not recommended. 
    /// </para>
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="typeFilterName">
    /// The <see cref="ExchangeableRuntimeFilter.Name"/> to use.
    /// Must be or start with "TypeScript" otherwise an <see cref="ArgumentException"/> is thrown.
    /// </param>
    /// <param name="skipValidation">
    /// It is safer for <see cref="JsonWriterOptions.SkipValidation"/> to be false except when using generated serialization code.
    /// Since this helper doesn't know how the Json will actually be serialized, this defaults to false.
    /// By setting this to null, it is up to the caller to decide whether json validation must be skipped or not. 
    /// </param>
    /// <returns>The export options to use.</returns>
    public virtual PocoJsonExportOptions CreateJsonExportOptions( HttpRequest request,
                                                                  string typeFilterName,
                                                                  bool? skipValidation = false )
    {
        Throw.CheckArgument( typeFilterName.StartsWith( "TypeScript" ) );
        bool usePascalCase = request.Query["UsePascalCase"].Count != 0;
        bool indented = request.Query["Indented"].Count != 0;
        bool typeLess = request.Query["TypeLess"].Count != 0;
        if( !skipValidation.HasValue ) skipValidation = request.Query["SkipValidation"].Count != 0;
        var encoder = request.Query["UnsafeRelaxedJsonEscaping"].Count != 0
                        ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        : System.Text.Encodings.Web.JavaScriptEncoder.Default;
        var o = new PocoJsonExportOptions()
        {
            UserMessageFormat = UserMessageSimplifiedFormat.Simple,
            TypeFilterName = typeFilterName,
            UseCamelCase = !usePascalCase,
            TypeLess = typeLess,
            WriterOptions = new JsonWriterOptions()
            {
                Encoder = encoder,
                Indented = indented,
                SkipValidation = skipValidation.Value
            }
        };
        return o;
    }

    /// <summary>
    /// Helper that handles optional "TypeFilterName" and "AllowTrailingCommas" query string arguments
    /// to initialize a new <see cref="PocoJsonImportOptions"/>.
    /// The <see cref="PocoJsonImportOptions.TypeFilterName"/> must start with "TypeScript".
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="messageCollector">The user message collector.</param>
    /// <param name="importOptions">The resulting import options.</param>
    /// <returns>True on success, false on error.</returns>
    static bool TryCreateJsonImportOptions( HttpRequest request,
                                            UserMessageCollector messageCollector,
                                            [NotNullWhen( true )] out PocoJsonImportOptions? importOptions )
    {
        var typeFilterName = (string?)request.Query["TypeFilterName"];
        if( typeFilterName == null ) typeFilterName = "TypeScript";
        else
        {
            if( !typeFilterName.StartsWith( "TypeScript" ) )
            {
                messageCollector.Error( $"Invalid TypeFilterName '{typeFilterName}'.", "Cris.AspNet.InvalidTypeFilterName" );
                importOptions = null;
                return false;
            }
        }
        bool allowTrailingCommas = request.Query["AllowTrailingCommas"].Count != 0;
        importOptions = new PocoJsonImportOptions()
        {
            ReaderOptions = new JsonReaderOptions() { AllowTrailingCommas = allowTrailingCommas },
            TypeFilterName = typeFilterName
        };
        return true;
    }

    /// <summary>
    /// Standard <see cref="CommandRequestReader"/> of a payload that is a <see cref="IAbstractCommand"/> poco in JSON format.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="request">The http request.</param>
    /// <param name="pocoDirectory">The poco directory.</param>
    /// <param name="messageCollector">The message collector to use for errors, warnings and logs.</param>
    /// <param name="payload">The request payload.</param>
    /// <param name="readOptions">
    /// The reader options. <see cref="PocoJsonImportOptions.TypeFilterName"/> must start with "TypeScript" otherwise
    /// an <see cref="ArgumentException"/> is thrown.
    /// </param>
    /// <returns>
    /// A non null command on success. When null, at least one <see cref="UserMessageLevel.Error"/> message should be in the collector.
    /// </returns>
    public static ValueTask<IAbstractCommand?> StandardReadCommandAsync( IActivityMonitor monitor,
                                                                         HttpRequest request,
                                                                         PocoDirectory pocoDirectory,
                                                                         UserMessageCollector messageCollector,
                                                                         ReadOnlySequence<byte> payload,
                                                                         PocoJsonImportOptions readOptions )
    {
        Throw.CheckArgument( readOptions.TypeFilterName.StartsWith( "TypeScript" ) );
        return DoStandardReadAsync( monitor, request, pocoDirectory, messageCollector, payload, readOptions );
    }

    internal static ValueTask<IAbstractCommand?> DoStandardReadAsync( IActivityMonitor monitor,
                                                                      HttpRequest request,
                                                                      PocoDirectory pocoDirectory,
                                                                      UserMessageCollector messageCollector,
                                                                      ReadOnlySequence<byte> payload,
                                                                      PocoJsonImportOptions readOptions )
    {
        var poco = pocoDirectory.ReadJson( payload, readOptions );
        if( poco == null )
        {
            messageCollector.Error( "Received a null Poco.", "Cris.AspNet.ReceiveNullPoco" );
            return ValueTask.FromResult<IAbstractCommand?>( null );
        }
        if( poco is not IAbstractCommand c )
        {
            messageCollector.Error( $"Received Poco is not a Command but a '{((IPocoGeneratedClass)poco).Factory.Name}'.", "Cris.AspNet.NotACommand" );
            return ValueTask.FromResult<IAbstractCommand?>( null );
        }
        return ValueTask.FromResult<IAbstractCommand?>( c );
    }

    ICrisCallResult CreateValidationErrorResult( IEnumerable<UserMessage> messages, string? logKey )
    {
        ICrisCallResult result = _resultFactory.Create();
        ICrisResultError? error = _crisErrorResultFactory.Create();
        error.IsValidationError = true;
        error.LogKey = logKey;
        result.Result = error;

        var validationMessages = new List<UserMessage>();
        foreach( var message in messages )
        {
            if( message.Level == UserMessageLevel.Error )
            {
                error.Errors.Add( message );
            }
            validationMessages.Add( message );
        }
        result.ValidationMessages = validationMessages;
        return result;
    }

}
