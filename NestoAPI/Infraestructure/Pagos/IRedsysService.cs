using NestoAPI.Models.Pagos;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    public interface IRedsysService
    {
        ParametrosRedsysFirmados CrearParametrosP2F(decimal importe, string correo,
            string movil, string textoSMS, string cliente, FormatoCorreoReclamacion datosCorreo);
        ParametrosRedsysFirmados CrearParametrosTPVVirtual(decimal importe, string descripcion,
            string correo, string urlNotificacion, string urlOk, string urlKo);
        Task<RespuestaRedsys> EnviarPeticionREST(ParametrosRedsysFirmados parametros);
        RespuestaRedsys DecodificarParametros(string merchantParametersBase64);
        ResultadoValidacionNotificacion ValidarNotificacion(NotificacionRedsys notificacion);
        string GenerarNumeroPedido(string sufijo = null);
        string UrlFormularioRedsys { get; }
    }
}
