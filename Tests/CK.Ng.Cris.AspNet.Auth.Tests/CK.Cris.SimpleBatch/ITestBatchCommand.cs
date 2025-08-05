using CK.Auth;
using CK.Cris;
using System.Collections.Generic;

namespace CK.Ng.Cris.AspNet.Auth.Tests;

public interface ITestBatchCommand : ICommand, ICommandSimpleBatch, ICommandAuthNormal, ICommandCurrentCulture
{
    public IDictionary<string, IAbstractCommand> Test1 { get; }
    public IList<IAbstractCommand> Test2 { get; }
    public IBasicTestCommand Test3 { get; set; }
    public ITestCommand Test4 { get; set; }
}

public interface IBasicTestCommand : ICommand<ICrisBasicCommandResult>, ICommandAuthNormal, ICommandCurrentCulture
{
}

public interface ITestCommand : ICommand, ICommandSimpleBatch, ICommandAuthNormal, ICommandCurrentCulture { }
