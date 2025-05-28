using CK.Core;
using CK.Poco.Exc.Json;
using Microsoft.AspNetCore.Http;
using System.Buffers;
using System.Threading.Tasks;

namespace CK.Cris.AspNet;

/// <summary>
/// Request payload reader function that <see cref="CrisAspNetService.HandleRequestAsync(IActivityMonitor, HttpRequest, CommandRequestReader, bool, CurrentCultureInfo?)"/>
/// uses.
/// </summary>
/// <param name="monitor">The monitor to use.</param>
/// <param name="request">The http request.</param>
/// <param name="pocoDirectory">The poco directory.</param>
/// <param name="messageCollector">The message collector to use for errors, warnings and logs.</param>
/// <param name="payload">The request payload.</param>
/// <param name="readOptions">The <see cref="PocoJsonImportOptions"/> to use.</param>
/// <returns>
/// A non null command on success. When null, at least one <see cref="UserMessageLevel.Error"/> message should be in the collector.
/// </returns>
public delegate ValueTask<IAbstractCommand?> CommandRequestReader( IActivityMonitor monitor,
                                                                   HttpRequest request,
                                                                   PocoDirectory pocoDirectory,
                                                                   UserMessageCollector messageCollector,
                                                                   ReadOnlySequence<byte> payload,
                                                                   PocoJsonImportOptions readOptions );
