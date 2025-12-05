using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// DelegatingHandler que sincroniza el usuario autenticado de Web API con HttpContext.Current.User.
    /// Esto es necesario porque OWIN/JWT no sincroniza automáticamente el User con HttpContext.Current,
    /// y ELMAH (y otros componentes legacy) leen el usuario de HttpContext.Current.User.
    /// </summary>
    public class UserSyncHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // En este punto, el JWT ya ha sido validado por el middleware de OWIN
            // y request.GetRequestContext().Principal contiene el usuario autenticado
            var principal = request.GetRequestContext()?.Principal;

            if (principal?.Identity?.IsAuthenticated == true && HttpContext.Current != null)
            {
                HttpContext.Current.User = principal;
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
