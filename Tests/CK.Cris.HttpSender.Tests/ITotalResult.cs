namespace CK.Cris.HttpSender.Tests;

public interface ITotalResult : IStandardResultPart
{
    int ActorId { get; set; }
    string CultureName { get; set; }
}
