create <html> transformer
begin
    inject """

           @if ( canShow( 'Passkey' ) ) {
               <ck-passkey-login-button />
           }

           """ into <LoginProviders>;
end

create <ts> transformer
begin
    ensure import { PasskeyLoginButton } from '../Passkey/passkey-login-button/passkey-login-button';

    in after "@Component"
        in first {^braces}
            in after "imports:"
                in first {^[]}
                    replace "TranslateModule" with "TranslateModule, PasskeyLoginButton";
end
