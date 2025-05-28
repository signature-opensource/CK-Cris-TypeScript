namespace CK.Cris.HttpSender.Tests;

public interface ITotalCommand : ICommand<ITotalResult>, ICommandCurrentCulture, CK.Auth.ICommandAuthNormal
{
    string? Action { get; set; }
}
