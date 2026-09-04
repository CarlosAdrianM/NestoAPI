using NestoAPI.Models.Pagos;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    public interface IRedsysService
    {
        ParametrosRedsysFirmados CrearParametrosP2F(decimal importe, string correo,
            string movil, string textoSMS, string cliente, FormatoCorreoReclamacion datosCorreo);
        ParametrosRedsysFirmados CrearParametrosTPVVirtual(decimal importe, string descripcion,
            string correo, string cliente, string urlNotificacion, string urlOk, string urlKo,
            string metodoPago = null, string numeroOrdenExistente = null, bool solicitarToken = false,
            string tokenTarjeta = null, string cofTxnId = null);
        ParametrosRedsysFirmados CrearParametrosCobroConToken(decimal importe,
            string descripcion, string cliente, string tokenTarjeta, string cofTxnId);
        ParametrosRedsysFirmados CrearParametrosDevolucion(decimal importe, string numeroOrden);

        // NestoAPI#181: los tres pasos de EMV 3DS 2 para cobrar con tarjeta guardada
        // autenticando al cliente (CIT), que es lo que conserva el traslado de responsabilidad.
        ParametrosRedsysFirmados CrearParametrosInicio3DS(decimal importe, string descripcion,
            string cliente, string tokenTarjeta, string cofTxnId);
        ParametrosRedsysFirmados CrearParametrosAutenticacion3DS(decimal importe, string numeroOrden,
            string descripcion, string tokenTarjeta, string cofTxnId, PeticionAutenticacion3DS peticion);
        ParametrosRedsysFirmados CrearParametrosRespuestaReto3DS(decimal importe, string numeroOrden,
            string descripcion, string tokenTarjeta, string cofTxnId, string protocolVersion, string cres);
        Task<RespuestaRedsys> EnviarPeticionREST(ParametrosRedsysFirmados parametros);
        RespuestaRedsys DecodificarParametros(string merchantParametersBase64);
        ResultadoValidacionNotificacion ValidarNotificacion(NotificacionRedsys notificacion);
        string GenerarNumeroPedido(string sufijo = null);
        string UrlFormularioRedsys { get; }
    }
}
