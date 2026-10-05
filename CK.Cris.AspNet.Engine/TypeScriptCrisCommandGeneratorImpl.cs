using CK.CodeGen;
using CK.Core;
using CK.Cris;
using CK.Cris.AmbientValues;
using CK.Setup.Cris;
using CK.TypeScript.CodeGen;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// Implements <see cref="TypeScriptCrisCommandGenerator"/>.
/// </summary>
public sealed partial class TypeScriptCrisCommandGeneratorImpl : ITSCodeGeneratorFactory
{
    ITSCodeGenerator? ITSCodeGeneratorFactory.CreateTypeScriptGenerator( IActivityMonitor monitor, ITypeScriptContextInitializer initializer )
    {
        if( initializer.EnsureRegister( monitor, typeof( ICrisCallResult ), mustBePocoType: true )
               && initializer.EnsureRegister( monitor, typeof( ICrisResultError ), mustBePocoType: true )
               && initializer.EnsureRegister( monitor, typeof( IAmbientValues ), mustBePocoType: true )
               && initializer.EnsureRegister( monitor, typeof( IAmbientValuesCollectCommand ), mustBePocoType: true ) )
        {
            return new CodeGen();
        }
        return null;
    }

    sealed class CodeGen : ITSCodeGenerator
    {
        TypeScriptFile? _modelFile;
        // These are defined in Cris/Model.ts.
        // OnResolveType returns theses parts for the implementation (they are implemented by design).
        ITSDeclaredFileType? _crisPoco;
        ITSDeclaredFileType? _abstractCommand;
        ITSDeclaredFileType? _command;
        //
        // We want the TypeScript AmbientValues class to be implemented in CrisEndpoint.ts:
        // - As a not exported class.
        // - Without comments.
        // - That implements the IAmbientValues interface.
        // We want the IAmbientValues to be the readonly façade of the private CrisEndpoint's AmbiantValues class.
        // - With readonly properties.
        // - With the comments.
        //
        // => But this is NOT easy. Postponed for the moment: the AmbientValues is currently exposed
        // as a ___a field by the CrisEndpoint and this is bad...
        //

        bool ITSCodeGenerator.StartCodeGeneration( IActivityMonitor monitor, TypeScriptContext context )
        {
            context.PocoCodeGenerator.PrimaryPocoGenerating += OnPrimaryPocoGenerating;
            context.PocoCodeGenerator.AbstractPocoGenerating += OnAbstractPocoGenerating;
            context.AfterDeferredCodeGeneration += GenerateCrisEndPointIfNeeded;
            return true;
        }

        // We don't add anything to the default IPocoType handling.
        bool ITSCodeGenerator.OnResolveObjectKey( IActivityMonitor monitor, TypeScriptContext context, RequireTSFromObjectEventArgs e ) => true;

        bool ITSCodeGenerator.OnResolveType( IActivityMonitor monitor,
                                             TypeScriptContext context,
                                             RequireTSFromTypeEventArgs builder )
        {
            var t = builder.Type;
            // Hooks:
            //   - ICommand and ICommand<TResult>: they are both implemented by ICommand<TResult = void> in Model.ts.
            //   - IAbstractCommand and ICrisPoco.
            // 
            // Model.ts also implements CommandModel<T>, ExecutedCommand<T>, and CrisError.
            //
            if( t.Namespace == "CK.Cris" )
            {
                if( t.Name == "ICommand" || (t.IsGenericTypeDefinition && t.Name == "ICommand`1") )
                {
                    EnsureCrisCommandModel( monitor, context );
                    builder.ResolvedType = _command;
                }
                else if( t.Name == "IAbstractCommand" )
                {
                    EnsureCrisCommandModel( monitor, context );
                    builder.ResolvedType = _abstractCommand;
                }
                else if( t.Name == "ICrisPoco" )
                {
                    EnsureCrisCommandModel( monitor, context );
                    builder.ResolvedType = _crisPoco;
                }
            }
            return true;
        }

        void OnAbstractPocoGenerating( object? sender, GeneratingAbstractPocoEventArgs e )
        {
            // Filtering out redundant ICommand, ICommand<T>: in TypeScript type name is
            // unique (both are handled by ICommand<TResult = void>).
            // On the TypeScript side, we have always a ICommand<T> where T can be void.

            // By filtering out the base interface it doesn't appear in the base interfaces
            // nor in the branded type. 
            if( HasICommand( e.AbstractPocoType, e.ImplementedInterfaces, out var mustRemoveICommand ) && mustRemoveICommand )
            {
                e.ImplementedInterfaces = e.ImplementedInterfaces.Where( i => i.Type != typeof( ICommand ) );
            }
        }

        void OnPrimaryPocoGenerating( object? sender, GeneratingPrimaryPocoEventArgs e )
        {
            if( e.PrimaryPocoType.Type == typeof( IAmbientValues ) )
            {
                // Generate the AmbientValuesOverride when generating the AmbientValues:
                // we use the AmbientValues fields.
                GenerateAmbientValuesOverride( e.PocoTypePart.File.Folder, e.Fields );
            }
            else if( HasICommand( e.PrimaryPocoType, e.ImplementedInterfaces, out var mustRemoveICommand ) )
            {
                if( mustRemoveICommand )
                {
                    e.ImplementedInterfaces = e.ImplementedInterfaces.Where( i => i.Type != typeof( ICommand ) );
                }
                e.PocoTypePart.File.Imports.ImportFromFile( EnsureCrisCommandModel( e.Monitor, e.TypeScriptContext ), "CommandModel" );

                var top = e.PocoTypePart.File.Body.CreatePart( top: true );
                top.Append( "class MyCommandModel extends CommandModel<" ).Append( e.TSGeneratedType.TypeName ).Append( "> {" ).NewLine()
                   .Append( "protected override doApplyAmbientValues( command: any, a: any, o: any ): void {" ).NewLine()
                   .InsertPart( out var applyPart )
                        .CloseBlock()
                    .CloseBlock();

                e.PocoTypePart.NewLine()
                    .Append( "get commandModel(): CommandModel<this> { return " ).Append( e.TSGeneratedType.TypeName ).Append( ".#m; }" ).NewLine()
                    .NewLine()
                    .Append( "static #m: MyCommandModel = new MyCommandModel();" )
                    .NewLine();

                // Totally horrible service locator: this is where CK.ReaDI can shine.
                var crisDirectory = e.TypeScriptContext.CodeContext.CurrentRun.ServiceContainer.GetRequiredService<ICrisDirectoryServiceEngine>();

                bool atLeastOne = false;
                foreach( var f in e.Fields )
                {
                    Throw.DebugAssert( f.TSField.PocoField is IPrimaryPocoField );
                    var pocoField = (IPrimaryPocoField)f.TSField.PocoField;
                    // No need to test non nullable properties: ubiquitous values are nullable.
                    if( pocoField.Type.IsNullable && crisDirectory.IsAmbientServiceValueField( pocoField ) )
                    {
                        // Documents it.
                        f.DocumentationExtension = b => b.AppendLine( "(This is a AmbientService Value.)", startNewLine: true );
                        // Adds the assignment: this property comes from its ambient value.
                        if( atLeastOne ) applyPart.NewLine();
                        // Generates:
                        // if( command.color === undefined ) command.color = o.color !== null ? o.color : a.color;
                        applyPart.Append( "if( command." ).Append( f.TSField.FieldName ).Append( " === undefined ) command." )
                            .Append( f.TSField.FieldName )
                            .Append( " = o." ).Append( f.TSField.FieldName ).Append( " !== null ? o." )
                            .Append( f.TSField.FieldName ).Append( " : a." ).Append( f.TSField.FieldName ).Append( ";" ).NewLine();
                        atLeastOne = true;
                    }
                }
                if( !atLeastOne ) applyPart.Append( "// This command has no AmbientValue property." ).NewLine();
            }

            static void GenerateAmbientValuesOverride( TypeScriptFolder endpointValuesFolder, ImmutableArray<TSNamedCompositeField> fields )
            {
                var b = endpointValuesFolder
                                    .FindOrCreateTypeScriptFile( "AmbientValuesOverride.ts" )
                                    .CreateType( "AmbientValuesOverride", null, null )
                                    .TypePart;
                b.Append( """
                /**
                * To manage endpoint values overrides, we use the null value to NOT override:
                *  - We decided to map C# null to undefined because working with both null
                *    and undefined is difficult.
                *  - Here, the null is used, so that undefined can be used to override with an undefined that will
                *    be a null value on the C# side.
                * All the properties are initialized to null in the constructor.
                **/

                """ )
                 .Append( "export class AmbientValuesOverride" )
                 .OpenBlock()
                 .InsertPart( out var propertiesPart )
                 .Append( "constructor()" )
                     .OpenBlock()
                     .InsertPart( out var ctorPart )
                     .CloseBlock();

                foreach( var f in fields )
                {
                    propertiesPart.Append( "public " )
                                  .Append( f.TSField.FieldName ).Append( ": " )
                                  .AppendTypeName( f.TSField.TSFieldType ).Append( "|null;" ).NewLine();
                    ctorPart.Append( "this." ).Append( f.TSField.FieldName ).Append( " = null;" ).NewLine();
                }

            }
        }

        static bool HasICommand( IPocoType t, IEnumerable<IAbstractPocoType> implementedInterfaces, out bool mustRemoveICommand )
        {
            IPocoType? typedResult = null;
            bool hasICommand = false;
            foreach( var i in implementedInterfaces )
            {
                if( i.GenericTypeDefinition?.Type == typeof( ICommand<> ) )
                {
                    var tResult = i.GenericArguments[0].Type;
                    if( typedResult != null )
                    {
                        // This has been already checked.
                        throw new CKException( $"{t} returns both '{typedResult}' and '{tResult}'." );
                    }
                    typedResult = tResult;
                }
                if( i.Type == typeof( ICommand ) )
                {
                    hasICommand = true;
                }
            }
            mustRemoveICommand = hasICommand && typedResult != null;
            return hasICommand || typedResult != null;
        }

        [MemberNotNull( nameof( _modelFile ), nameof( _command ), nameof( _abstractCommand ), nameof( _crisPoco ) )]
        TypeScriptFile EnsureCrisCommandModel( IActivityMonitor monitor, TypeScriptContext context )
        {
            if( _modelFile == null )
            {
                _modelFile = context.Root.Root.FindOrCreateTypeScriptFile( "CK/Cris/Model.ts" );
                GenerateCrisModelFile( monitor, context, _modelFile );
                _crisPoco = _modelFile.DeclareType( "ICrisPoco" );
                _abstractCommand = _modelFile.DeclareType( "IAbstractCommand" );
                _command = _modelFile.DeclareType( "ICommand" );
                // No need to keep this one: it is only here to be automatically
                // resolved from @local/ck-gen.
                _modelFile.DeclareType( "CrisError" );
            }
            Throw.DebugAssert( _command != null && _abstractCommand != null && _crisPoco != null );
            return _modelFile;

            static void GenerateCrisModelFile( IActivityMonitor monitor, TypeScriptContext context, TypeScriptFile fModel )
            {
                fModel.Imports.EnsureImport( monitor, typeof( SimpleUserMessage ) );
                fModel.Imports.EnsureImport( monitor, typeof( UserMessageLevel ) );
                fModel.Imports.ImportFromLocalCKGen( "CrisEndpoint" );

                var pocoType = context.Root.TSTypes.ResolveTSType( monitor, typeof( IPoco ) );
                // Imports the IPoco itself...
                pocoType.EnsureRequiredImports( fModel.Imports );

                fModel.Body.Append( """
                            export abstract class CommandModel<T extends IAbstractCommand> {
                                applyAmbientValues( command: T, endpoint: CrisEndpoint ): void {
                                    const visited = new Set<{}>();
                                    this.#doApply( command, endpoint, visited );
                                };

                                #doApply( command: IAbstractCommand, endpoint: CrisEndpoint, visited: Set<{}> ): void {
                                    this.doApplyAmbientValues( command, endpoint.___a, endpoint.ambientValuesOverride );
                                    for( const key in command ) {
                                        if( command.hasOwnProperty( key ) ) {
                                            this.#doApplyAny( (<any>command)[key], endpoint, visited );
                                        }
                                    }
                                }

                                #doApplyAny( obj: any, endpoint: CrisEndpoint, visited: Set<{}> ): void {
                                    if ( !obj || typeof obj !== 'object' || visited.has( obj ) ) {
                                        return;
                                    }
                                    visited.add( obj );
                                    if( this.#isCommand( obj ) ) {
                                        this.#doApply( <IAbstractCommand>obj, endpoint, visited );
                                    }
                                    else if( Array.isArray( obj ) ) {
                                        obj.forEach( item => this.#doApplyAny( item, endpoint, visited ) );
                                    }
                                    else if ( obj instanceof Map ) {
                                        for( const [key, value] of obj ) {
                                            this.#doApplyAny( value, endpoint, visited );
                                        }
                                    }
                                    else {
                                        for ( const key in obj ) {
                                            if ( obj.hasOwnProperty( key ) ) {
                                                this.#doApplyAny( obj[key], endpoint, visited );
                                            }
                                        }
                                    }
                                }

                                #isCommand(obj: any): obj is IAbstractCommand {
                                    return obj && obj.commandModel && typeof obj.commandModel.applyAmbientValues === 'function';
                                }
                                
                                protected abstract doApplyAmbientValues( command: any, a: any, o: any ): void;
                            }

                            /** 
                             * Abstraction of any Cris objects (currently only commands).
                             **/
                            export interface ICrisPoco extends IPoco
                            {
                                readonly _brand: IPoco["_brand"] & {"ICrisPoco": any};
                            }

                            /** 
                             * Command abstraction.
                             **/
                            export interface IAbstractCommand extends ICrisPoco
                            {
                                /** 
                                 * Gets the command model.
                                 **/
                                get commandModel(): CommandModel<this>;

                                readonly _brand: ICrisPoco["_brand"] & {"ICommand": any};
                            }

                            /** 
                             * Command with or without a result.
                             * The C# ICommand (without result) is the TypeScript ICommand<void>.
                             **/
                            export interface ICommand<out TResult = void> extends IAbstractCommand {
                                readonly _brand: IAbstractCommand["_brand"] & {"ICommandResult": void extends TResult ? any : TResult};
                            }
                            
                            /** 
                             * Captures the result of a command execution.
                             **/
                            export type ExecutedCommand<T> = {
                                /** The executed command. **/
                                readonly command: ICommand<T>,
                                /** The execution result. **/
                                readonly result: CrisError | T,
                                /**
                                 * An optional list of UserMessageLevel.info, UserMessageLevel.warn or UserMessageLevel.error
                                 * messages issued by the validation of the command: there can be info or warn messages even if the 
                                 * command has been succesfully executed. 
                                 * Validation error messages also appear in the CrisError.messages.
                                 **/
                                readonly validationMessages?: Array<SimpleUserMessage>;
                                /** Optional correlation identifier. **/
                                readonly correlationId?: string
                            };
                            
                            /**
                             * Captures communication, validation or execution error.
                             **/
                            export class CrisError extends Error {
                               /**
                                * Get this error type.
                                */
                                public readonly errorType : "CommunicationError"|"ValidationError"|"ExecutionError";
                                /**
                                 * Gets the errors. At least one error is guaranteed to exist.
                                 */
                                public readonly errors: ReadonlyArray<string>; 
                                /**
                                 * Gets the validationMessages if any.
                                 */
                                public readonly validationMessages?: ReadonlyArray<SimpleUserMessage>; 
                                /**
                                 * The Error.cause support is a mess. This replaces it at this level. 
                                 */
                                public readonly innerError?: Error; 
                                /**
                                 * When defined, enables to find the backend log entry.
                                 */
                                public readonly logKey?: string; 
                                /**
                                 * Gets the command that failed.
                                 */
                                public readonly command: ICommand<unknown>;

                                /**
                                 * Initializes a new CrisError.
                                 * @param command The command that failed.
                                 * @param isValidationError True for a ValidationError.
                                 * @param errors At least one error message.
                                 * @param innerError Optional inner error.
                                 * @param validationMessages Optional validation messages.
                                 * @param logKey Optional backend log key.
                                 * @param isCommunicationError Whether this is a CommunicationError (the server could not be reached).
                                 * Defaults to true when an innerError is provided: an error with an innerError that the server
                                 * answered (like an HTTP 500) must set it to false to be an ExecutionError.
                                 */
                                constructor( command: ICommand<unknown>,
                                             isValidationError: boolean,
                                             errors: ReadonlyArray<string>,
                                             innerError?: Error,
                                             validationMessages?: ReadonlyArray<SimpleUserMessage>,
                                             logKey?: string,
                                             isCommunicationError?: boolean )
                                {
                                    super( errors[0] );
                                    this.command = command;
                                    this.errorType = isValidationError
                                                        ? "ValidationError"
                                                        : (isCommunicationError ?? !!innerError) ? "CommunicationError" : "ExecutionError";
                                    this.innerError = innerError;
                                    this.errors = errors;
                                    this.validationMessages = validationMessages;
                                    this.logKey = logKey;
                                }
                            }
                            
                            """ );
            }
        }

        void GenerateCrisEndPointIfNeeded( object? sender, EventMonitoredArgs e )
        {
            Throw.DebugAssert( sender is TypeScriptContext );
            // If model has not been created, skip everything.
            if( _modelFile != null )
            {
                TypeScriptContext context = (TypeScriptContext)sender;
                var crisEndpoint = GenerateCrisEndpoint( e.Monitor, context, _modelFile );
                // If there is no Json serialization, we skip the HttpEndpoint as it uses the CTSType.
                if( context.PocoCodeGenerator.CTSTypeSystem != null )
                {
                    GenerateCrisHttpEndpoint( e.Monitor, _modelFile, crisEndpoint, context.PocoCodeGenerator.CTSTypeSystem.CTSType, context.BinPathConfiguration.TypeFilterName );
                }
            }
        }

        static ITSFileType GenerateCrisEndpoint( IActivityMonitor monitor,
                                                 TypeScriptContext context,
                                                 TypeScriptFile modelFile )
        {
            TypeScriptFile fEndpoint = modelFile.Folder.FindOrCreateTypeScriptFile( "CrisEndpoint.ts" );

            // AmbientValuesOverride is in the same folder as AmbienValues.ts.
            var ambientValuesOverride = context.Root.TSTypes.FindByTypeName( "AmbientValuesOverride" );
            Throw.CheckState( "AmbientValuesOverride is automatically created in the same folder as AmbientValues.ts and IAmbientValues is a registered type.",
                                ambientValuesOverride != null );
            // Importing:
            // - the Model objects ICommand, ExecutedCommand and CrisError.
            // - The IAmbientValues and IAmbientValuesCollectCommand.
            // - The AmbientValuesOverride.
            fEndpoint.Imports.ImportFromFile( modelFile, "ICommand, ExecutedCommand, CrisError" );
            fEndpoint.Imports.EnsureImport( monitor, typeof( IAmbientValues ) );
            fEndpoint.Imports.EnsureImport( monitor, typeof( IAmbientValuesCollectCommand ) );
            fEndpoint.Imports.Import( ambientValuesOverride );

            var crisEndPoint = fEndpoint.CreateType( "CrisEndpoint", null, null );
            // Letf opened (closer on the part).
            crisEndPoint.TypePart.Append( """
                    /**
                    * Abstract Cris endpoint. 
                    * The doSendAsync protected method must be implemented.
                    */
                    export abstract class CrisEndpoint
                    {
                        #ambientValuesRequest: Promise<AmbientValues>|undefined;
                        // This should not appear here. This will be fixed in a future release.
                        ___a: AmbientValues|undefined;
                        #subscribers: Set<( eventSource: CrisEndpoint ) => void>;
                        #isConnected: boolean;
                        // Current retry delay: 0 until a CommunicationError occurs.
                        #retryDelay: number;
                        // Optional server provided delay (Retry-After) for the next retry.
                        #retryAfter: number|undefined;

                        constructor()
                        {
                            this.ambientValuesOverride = new AmbientValuesOverride();
                            this.#isConnected = false;
                            this.#subscribers = new Set<() => void>();
                            this.#retryDelay = 0;
                        }

                        /**
                        * Gets or sets the delay in milliseconds before the first retry when the server cannot be reached.
                        * The delay doubles on each failed attempt up to retryMaxDelay. Defaults to 500 ms.
                        **/
                        public retryMinDelay: number = 500;

                        /**
                        * Gets or sets the maximal delay in milliseconds between two retries. Defaults to 30 seconds.
                        **/
                        public retryMaxDelay: number = 30000;

                        /**
                        * Sets a server provided delay (typically from a Retry-After header) for the next retry.
                        * The next retry waits at least this delay (capped by retryMaxDelay).
                        * @param milliseconds The delay in milliseconds.
                        */
                        protected setRetryAfter( milliseconds: number ): void
                        {
                            this.#retryAfter = milliseconds;
                        }

                        /**
                        * Enables ambient values to be overridden.
                        * Sensible ambient values (like the actorId when CK.IO.Auth is used) are checked against
                        * secured contextual values: overriding them will trigger a ValidationError. 
                        **/    
                        public readonly ambientValuesOverride: AmbientValuesOverride;


                        //#region isConnected
                        /** 
                        * Gets whether this HttpEndpointService is connected: the last command sent
                        * has been handled by the server. 
                        **/
                        public get isConnected(): boolean { return this.#isConnected; }

                        /**
                        * Registers a callback function that will be called when isConnected changed.
                        * @param func A callback function.
                        */
                        public addOnIsConnectedChanged( func: ( eventSource: CrisEndpoint ) => void ): void 
                        {
                            if( func ) this.#subscribers.add( func );
                        }

                        /**
                        * Unregister a previously registered callback.
                        * @param func The callback function to remove.
                        * @returns True if the callback has been found and removed, false otherwise.
                        */
                        public removeOnIsConnectedChange( func: ( eventSource: CrisEndpoint ) => void ): boolean {
                            return this.#subscribers.delete( func );
                        }

                        /**
                        * Sets whether this endpoint is connected or not. When setting false, this triggers
                        * an update of the endpoint values that retries (with a backoff) until the server answers
                        * and eventually sets a true isConnected back.
                        * This must be called only when the server cannot be reached: a server that answers
                        * with an error is connected.
                        * @param value Whether the connection must be considered available or not.
                        */
                        protected setIsConnected( value: boolean ): void 
                        {
                            if( this.#isConnected !== value )
                            {
                                this.#isConnected = value;
                                if( !value )
                                {
                                    // A server error ends the update: it is returned to the waiting commands.
                                    this.updateAmbientValuesAsync().catch( () => {} );
                                }
                                this.#subscribers.forEach( func => func( this ) );
                            }
                        }

                        //#endregion

                        /**
                        * Sends a AmbientValuesCollectCommand and waits for its return.
                        * Next commands will wait for the ubiquitous values to be received before being sent.
                        * <para>
                        * While the server cannot be reached (CommunicationError), this retries with an exponential
                        * backoff (see retryMinDelay and retryMaxDelay). When the server answers with an error, this
                        * rejects with the CrisError: the next call (or the next sent command) tries again.
                        * </para>
                        **/
                        public updateAmbientValuesAsync() : Promise<AmbientValues>
                        {
                            if( this.#ambientValuesRequest ) return this.#ambientValuesRequest;
                            this.___a = undefined;
                            return this.#ambientValuesRequest = this.waitForAmbientValuesAsync();
                        }

                        /**
                        * Sends a command and returns an ExecutedCommand with the command's result or a CrisError.
                        * This waits for the ambient values: if they cannot be obtained because the server answered with
                        * an error, the result is an ExecutionError.
                        **/
                        public async sendAsync<T>(command: ICommand<T>): Promise<ExecutedCommand<T>>
                        {
                            // Don't use coalesce here since there may be no ambient values (an empty object is truthy).
                            if( this.___a === undefined )
                            {
                                try
                                {
                                    await this.updateAmbientValuesAsync();
                                }
                                catch( e )
                                {
                                    const inner = e instanceof Error ? e : new Error( `Unhandled error ${e}.` );
                                    const logKey = e instanceof CrisError ? e.logKey : undefined;
                                    return {command, result: new CrisError( command, false, [`Unable to obtain the ambient values: ${inner.message}`], inner, undefined, logKey, false )};
                                }
                            }
                            command.commandModel.applyAmbientValues( command, this );
                            return await this.doSendAsync( command );
                        }

                        /**
                        * Sends a command and returns the command's result or throws a CrisError.
                        **/    
                        public async sendOrThrowAsync<T>( command: ICommand<T> ): Promise<T>
                        {
                            const r = await this.sendAsync( command );
                            if( r.result instanceof CrisError ) throw r.result;
                            return r.result;
                        }

                        /**
                        * Core method to implement. Can use the handleJsonResponse helper to create 
                        * the final ExecutedCommand<T> from a Json object response. 
                        * @param command The command to send.
                        * @returns The resulting ExecutedCommand<T>.
                        */
                        protected abstract doSendAsync<T>(command: ICommand<T>): Promise<ExecutedCommand<T>>;

                        private async waitForAmbientValuesAsync() : Promise<AmbientValues>
                        {
                            try
                            {
                                while(true)
                                {
                                    const e = await this.doSendAsync( new AmbientValuesCollectCommand() );
                                    if( !(e.result instanceof CrisError) )
                                    {
                                        this.#retryDelay = 0;
                                        this.#ambientValuesRequest = undefined;
                                        this.___a = <AmbientValues>e.result;
                                        this.setIsConnected( true );
                                        return this.___a;
                                    }
                                    if( e.result.errorType !== "CommunicationError" )
                                    {
                                        // The server answered: retrying won't help.
                                        console.error( "Error while getting AmbientValues.", e.result );
                                        throw e.result;
                                    }
                                    this.#retryDelay = this.#retryDelay === 0
                                                        ? this.retryMinDelay
                                                        : Math.min( this.#retryDelay * 2, this.retryMaxDelay );
                                    const delay = Math.max( this.#retryDelay, Math.min( this.#retryAfter ?? 0, this.retryMaxDelay ) );
                                    this.#retryAfter = undefined;
                                    console.error( `Error while getting AmbientValues. Retrying in ${delay} ms.`, e.result );
                                    this.setIsConnected( false );
                                    await new Promise( resolve => setTimeout( resolve, delay ) );
                                }
                            }
                            finally
                            {
                                this.#ambientValuesRequest = undefined;
                            }
                        }
                    """ );
            return crisEndPoint;
        }

        static ITSFileType? GenerateCrisHttpEndpoint( IActivityMonitor monitor,
                                                      TypeScriptFile modelFile,
                                                      ITSFileType crisEndpoint,
                                                      ITSType ctsType,
                                                      string typeFilterName )
        {
            TypeScriptFile fHttpEndpoint = modelFile.Folder.FindOrCreateTypeScriptFile( "HttpCrisEndpoint.ts" );
            // Importing:
            // - the Model objects ICommand, ExecutedCommand and CrisError.
            // - The base CrisEndPoint.
            // - The IAspNetCrisResult server result model.
            // - Axios to send/receive the POST.
            // - The CTSType to serialize/deserialize.
            // - The IAspNetCrisResultError that must be transformed into a CrisError.

            // The AfterCodeGeneration detect monitor error or fatal.
            var axios = modelFile.Root.LibraryManager.RegisterLibrary( monitor, "axios", DependencyKind.PeerDependency );
            if( axios == null ) return null;
            axios.IsUsed = true;

            fHttpEndpoint.Imports.ImportFromFile( modelFile, "ICommand, ExecutedCommand, CrisError" );
            fHttpEndpoint.Imports.Import( crisEndpoint );
            fHttpEndpoint.Imports.EnsureImport( monitor, typeof( ICrisCallResult ) );
            fHttpEndpoint.Imports.ImportFromLibrary( axios, "AxiosInstance, AxiosHeaders, RawAxiosRequestConfig, isAxiosError" );
            fHttpEndpoint.Imports.Import( ctsType );
            fHttpEndpoint.Imports.EnsureImport( monitor, typeof( ICrisResultError ) );
            fHttpEndpoint.Imports.EnsureImport( monitor, typeof( UserMessageLevel ) );

            fHttpEndpoint.Body.Append( """
                                const defaultCrisAxiosConfig: RawAxiosRequestConfig = {
                                    responseType: 'text',
                                    headers: {
                                        common: new AxiosHeaders({
                                        'Content-Type': 'application/json'
                                        })
                                    }
                                };

                                """ );

            var httpCrisEndPoint = fHttpEndpoint.CreateType( "HttpCrisEndpoint", null, null );
            // Letf opened (closer on the part).
            httpCrisEndPoint.TypePart.Append( $$$"""
                                /**
                                * Http Cris Command endpoint. 
                                **/
                                export class HttpCrisEndpoint extends CrisEndpoint
                                {
                                    #axios: AxiosInstance;
                                    #typeFilterName : string;
                                    #crisEndpointUrl : string;

                                    /**
                                     * Gets the TypeFilterName that defines the exchangeable set of objects
                                     * with the backend.
                                     * Defaults to CTSType.typeFilterName = '{{{typeFilterName}}}' (the TypeFilterName for this ck-gen folder).
                                     */
                                    public get typeFilterName() { return this.#typeFilterName; }

                                    /**
                                     * Replaceable axios configuration.
                                     */
                                    public axiosConfig: RawAxiosRequestConfig; 

                                    /**
                                     * Initializes a new HttpCrisEndpoint.  
                                     * @param axios The axios instance.
                                     * @param crisEndpointUrl The Cris endpoint url to use.
                                     * @param typeFilterName The TypeFilterName that defines the set of objects that can be exchanged with the backend.
                                     */
                                    constructor( axios: AxiosInstance, crisEndpointUrl?: string, typeFilterName: string = CTSType.typeFilterName)
                                    {
                                        super();
                                        if( !crisEndpointUrl ) crisEndpointUrl = window.location.origin + '/.cris';
                                        this.#axios = axios;
                                        this.#typeFilterName = typeFilterName;
                                        this.#crisEndpointUrl = crisEndpointUrl + (crisEndpointUrl.indexOf('?') < 0 ? "?" : "&" ) + "TypeFilterName=" + typeFilterName;
                                        this.axiosConfig = defaultCrisAxiosConfig;
                                    }

                                    protected override async doSendAsync<T>(command: ICommand<T>): Promise<ExecutedCommand<T>>
                                    {
                                        try
                                        {
                                            const req = JSON.stringify(CTSType.toTypedJson(command));
                                            const resp = await this.#axios.post(this.#crisEndpointUrl, req, this.axiosConfig);
                                            const netResult = <CrisCallResult>CTSType["CrisCallResult"].nosj( JSON.parse(resp.data) );
                                            let r = netResult.result;
                                            if( r instanceof CrisResultError ) 
                                            {
                                                const errors = r.errors.filter( m => m.level === UserMessageLevel.Error ).map( m => m.message );
                                                r = new CrisError(command, r.isValidationError, errors, undefined, netResult.validationMessages, r.logKey);
                                            }
                                            return {command: command, result: <T|CrisError>r, validationMessages: netResult.validationMessages, correlationId: netResult.correlationId };
                                        }
                                        catch( e )
                                        {
                                            var error : Error;
                                            if( e instanceof Error)
                                            {
                                                error = e;
                                            }
                                            else
                                            {
                                                // Error.cause is a mess. Log it.
                                                console.error( e );
                                                error = new Error(`Unhandled error ${e}.`);
                                            }
                                            if( !isAxiosError( e ) )
                                            {
                                                // The server answered but its response cannot be read.
                                                return {command, result: new CrisError(command, false, ["Protocol error"], error, undefined, undefined, false )};
                                            }
                                            const status = e.response?.status;
                                            if( status !== undefined && !HttpCrisEndpoint.isTransientStatus( status ) )
                                            {
                                                // The server answered with an error: the connection is fine and retrying won't help.
                                                return {command, result: new CrisError(command, false, [`Server error (HTTP ${status}).`], error, undefined, undefined, false )};
                                            }
                                            // No response (network error, timeout) or a transient status.
                                            const retryAfter = HttpCrisEndpoint.parseRetryAfter( e.response?.headers?.["retry-after"] );
                                            if( retryAfter !== undefined ) this.setRetryAfter( retryAfter );
                                            this.setIsConnected(false);
                                            return {command, result: new CrisError(command, false, ["Communication error"], error )};
                                        }
                                    }

                                    /**
                                     * Gets whether an HTTP status is transient: the request can be retried later.
                                     * These are 408 (Request Timeout), 429 (Too Many Requests), 502 (Bad Gateway),
                                     * 503 (Service Unavailable) and 504 (Gateway Timeout).
                                     * @param status The HTTP status.
                                     * @returns True if the status is transient.
                                     */
                                    public static isTransientStatus( status: number ): boolean
                                    {
                                        return status === 408 || status === 429 || status === 502 || status === 503 || status === 504;
                                    }

                                    /**
                                     * Parses a Retry-After header value (delay in seconds or HTTP date).
                                     * @param value The header value.
                                     * @returns The delay in milliseconds or undefined.
                                     */
                                    static parseRetryAfter( value: unknown ): number|undefined
                                    {
                                        if( typeof value !== "string" || value.length === 0 ) return undefined;
                                        const seconds = Number( value );
                                        if( !Number.isNaN( seconds ) ) return seconds >= 0 ? seconds * 1000 : undefined;
                                        const date = Date.parse( value );
                                        return Number.isNaN( date ) ? undefined : Math.max( 0, date - Date.now() );
                                    }
                                """ );
            return httpCrisEndPoint;
        }
    }

}
