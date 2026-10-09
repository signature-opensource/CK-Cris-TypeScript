create <html> transformer
begin
    inject """

           <ck-weak-login-form />

           """ into <LoginProviders>;
end

create <ts> transformer
begin
    ensure import { WeakLoginForm } from '../Weak/weak-login-form/weak-login-form';

    in after "@Component" 
        in first {^braces}
            in after "imports:"
                in first {^[]}
                    replace "TranslateModule" with "TranslateModule, WeakLoginForm";
end
