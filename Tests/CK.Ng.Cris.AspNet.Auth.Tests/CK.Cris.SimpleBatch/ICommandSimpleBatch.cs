using CK.Core;
using System.Collections.Generic;

namespace CK.Cris;

// [Abstract] -> should not be sent, must be specified
public interface ICommandSimpleBatch : ICommandPart, ICommand<ISimpleBatchCommandResultPart>
{
    public IList<(IAbstractCommand Command, string? Description)> Commands { get; }
}

[CKTypeDefiner]
// [Virtual]
public interface ISimpleBatchCommandResultPart : IStandardResultPart
{
    public IList<IPocoCommandExecutedCommandResult> Results { get; }

    //public IList<IPocoCommandExecutedPart> Results { get; }
}

// Will disappear once we have [Abstract], [Virtual], [Primary], [Secondary]
public interface IPocoCommandExecutedCommandResult : IPocoCommandExecutedPart { }
