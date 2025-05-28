using CK.Core;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace CK.Cris.Tests;

public interface IResult : IPoco
{
    int Val { get; set; }
}

/// <summary>
/// Extends the basic result with a <see cref="MoreVal"/>.
/// </summary>
public interface IMoreResult : IResult
{
    /// <summary>
    /// Gets or sets the More value.
    /// </summary>
    int MoreVal { get; set; }
}

public interface IAnotherResult : IResult
{
    int AnotherVal { get; set; }
}

public interface IUnifiedResult : IMoreResult, IAnotherResult { }

public interface IWithPocoResultCommand : ICommand<IResult> { }

public interface IWithMorePocoResultCommand : IWithPocoResultCommand, ICommand<IMoreResult> { }

public interface IWithAnotherPocoResultCommand : IWithPocoResultCommand, ICommand<IAnotherResult> { }

public interface IWithTheResultUnifiedCommand : IWithMorePocoResultCommand, IWithAnotherPocoResultCommand, ICommand<IUnifiedResult> { }


// Cannot work: the results are NOT unified in a final type.
public interface IUnifiedButNotTheResultCommand : IWithMorePocoResultCommand, IWithAnotherPocoResultCommand { }
