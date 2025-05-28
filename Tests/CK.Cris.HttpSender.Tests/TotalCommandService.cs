using CK.Auth;
using CK.Core;

namespace CK.Cris.HttpSender.Tests;

public sealed class TotalCommandService : ISingletonAutoService
{
    [CommandHandlingValidator]
    public void Validate( UserMessageCollector validator, ITotalCommand cmd )
    {
        if( !string.IsNullOrEmpty( cmd.Action )
            && (cmd.Action != "Bug!" || cmd.Action != "Error!" || cmd.Action != "Warn!") )
        {
            validator.Error( $"The Action must be Bug!, Error!, Warn! or empty. Not '{cmd.Action}'." );
        }
    }

    [CommandHandler]
    public ITotalResult Handle( CurrentCultureInfo culture, IAuthenticationInfo authInfo, ITotalCommand cmd )
    {
        var messages = new UserMessageCollector( culture );
        using( messages.OpenInfo( $"Handling TotalCommand for user '{authInfo.User.UserName}' in culture '{culture.CurrentCulture.Name}'." ) )
        {
            if( cmd.Action == "Bug!" ) throw culture.MCException( "You asked to Bug!" );
            else if( cmd.Action == "Error!" ) messages.Error( "You asked for an Error!" );
            else if( cmd.Action == "Warn!" ) messages.Warn( "You asked for a Warning!" );

            return cmd.CreateResult( r =>
            {
                r.SetUserMessages( messages );
                r.ActorId = authInfo.User.UserId;
                r.CultureName = culture.CurrentCulture.Name;
            } );
        }
    }
}
