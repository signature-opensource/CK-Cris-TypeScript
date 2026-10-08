create <ts> transformer on "CK/Angular/routes.ts"
begin
    ensure import { AuthService } from "@local/ck-gen/CK/Cris/AspNet/Authentication/AuthService";

    insert """

           let authService: AuthService | null = null;
           """
        before single "export default";

    insert """
           ,
           canMatch: [() => (authService ??= inject( AuthService )).actor.actorId !== 0]

           """
        after last "component: PrivatePage";

    replace last "targetUrl = router.parseUrl( '/' );" with "targetUrl = router.parseUrl( '/auth' );";
    replace last "targetUrl.queryParams[ 'notFound' ] = currentRoute" with "targetUrl.queryParams[ 'redirectTo' ] = currentRoute";
end
