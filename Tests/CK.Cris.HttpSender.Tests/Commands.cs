using CK.Core;
using System;
using System.Threading.Tasks;

namespace CK.Cris.HttpSender.Tests;

public interface ICommandColored : ICommandPart
{
    string Color { get; set; }
}

public interface IColoredEndpointValues : AmbientValues.IAmbientValues
{
    /// <summary>
    /// The color of <see cref="ICommandColored"/> part.
    /// </summary>
    [AmbientServiceValue]
    string? Color { get; set; }
}

/// <summary>
/// A beautiful command has a <see cref="Beauty"/> and is a <see cref="ICommandColored"/>.
/// It returns a string.
/// </summary>
public interface IBeautifulCommand : ICommandColored, ICommand<string>
{
    string Beauty { get; set; }
}

public interface INakedCommand : ICommand
{
    string Event { get; set; }
}

public class ColorAndNakedService : IAutoService
{
    [CommandPostHandler]
    public void GetColoredEndpointValues( AmbientValues.IAmbientValuesCollectCommand cmd, IColoredEndpointValues values )
    {
        values.Color = "Red";
    }

    [CommandHandler]
    public string HandleBeatifulCommand( IBeautifulCommand cmd )
    {
        return $"{cmd.Color} - {cmd.Beauty}";
    }

    [CommandHandler]
    public void HandleNakedCommand( CurrentCultureInfo culture, INakedCommand cmd )
    {
        if( cmd.Event == "Bug!" )
        {
            throw new Exception( "Outer exception.",
                        new AggregateException( culture.MCException( "Bug! (n°1)" ), culture.MCException( "Bug! (n°2)" ) ) );
        }
    }
}

public interface IBeautifulWithOptionsCommand : IBeautifulCommand
{
    /// <summary>
    /// Gets or sets the number of milliseconds that the command handling must take.
    /// </summary>
    public int WaitTime { get; set; }
}

public class WithOptionsService : IAutoService
{
    [CommandHandler]
    public async Task<string> HandleAsync( IBeautifulWithOptionsCommand cmd )
    {
        await Task.Delay( cmd.WaitTime );
        return $"{cmd.Color} - {cmd.Beauty} - {cmd.WaitTime}";
    }
}
