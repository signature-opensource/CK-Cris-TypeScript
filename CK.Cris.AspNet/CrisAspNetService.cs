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
/// </summary>
[SingletonContainerConfiguredService]
[AlsoRegisterType( typeof( CrisDirectory ) )]
[AlsoRegisterType( typeof( TypeScriptCrisCommandGenerator ) )]
[AlsoRegisterType( typeof( CommonPocoJsonSupport ) )]
[AlsoRegisterType( typeof( RawCrisReceiver ) )]
[AlsoRegisterType( typeof( ICrisCallResult ) )]
[AlsoRegisterType( typeof( CrisBackgroundExecutorService ) )]
[AlsoRegisterType( typeof( IAmbientValuesCollectCommand ) )]
[AlsoRegisterType( typeof( CrisCultureService ) )]
public partial class CrisAspNetService : ISingletonAutoService
{
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
    public CrisAspNetService( PocoDirectory poco,
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
    /// <returns>The command result and the <see cref="ExchangeableRuntimeFilter.Name"/> to use to send the response.</returns>
    public Task<(ICrisCallResult Result, string TypeFilterName)> HandleRequestAsync( IActivityMonitor monitor,
                                                                                       HttpRequest request,
                                                                                       CommandRequestReader reader,
                                                                                       CurrentCultureInfo? currentCultureInfo = null )
    {
        return DoHandleAsync( monitor, request, reader, currentCultureInfo, null );
    }

    internal async Task<(ICrisCallResult Result, string TypeFilterName)> DoHandleAsync( IActivityMonitor monitor,
                                                                                          HttpRequest request,
                                                                                          CommandRequestReader reader,
                                                                                          CurrentCultureInfo? currentCultureInfo,
                                                                                          PocoJsonImportOptions? readOptions )
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
                // Incoming command validation.
                CrisValidationResult validation = await _validator.IncomingValidateAsync( monitor, requestServices, readResult.Command );
                allValidationMessages = allValidationMessages.Concat( validation.ValidationMessages );
                if( !validation.Success )
                {
                    result = CreateValidationErrorResult( allValidationMessages, validation.LogKey );
                }
                ambientServiceHub = validation.AmbientServiceHub;
            }
            // No result so far: incoming validation succeeded.
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
                using var gError = monitor.OpenError( messageCollector.UserMessages[^1].Message.CodeString, ex );
                var (body, error) = ReadBodyTextOnError( buffer.GetReadOnlySequence() );
                if( body != null )
                {
                    monitor.Trace( body );
                }
                else
                {
                    monitor.Error( "Error while tracing request body.", error );
                }
                return new ReadResult( null, CreateValidationErrorResult( messageCollector.UserMessages, gError.GetLogKeyString() ), readOptions.TypeFilterName, messageCollector );
            }
        }

        static (string? B, Exception? E) ReadBodyTextOnError( in ReadOnlySequence<byte> buffer )
        {
            try
            {
                return (Encoding.UTF8.GetString( buffer ), null);
            }
            catch( Exception ex )
            {
                return (null, ex);
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
            AlwaysExportSimpleUserMessage = true,
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
