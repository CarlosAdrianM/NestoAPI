using NestoAPI.Models.PedidosVenta;
using System.Net;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Excepción lanzada cuando un pedido no pasa las validaciones de negocio.
    ///
    /// Usado específicamente para errores de validación de:
    /// - Precios y descuentos no autorizados
    /// - Ofertas no permitidas
    /// - Validaciones del GestorPrecios
    /// - Validaciones del GestorPedidosVenta
    ///
    /// IMPORTANTE:
    /// Esta excepción devuelve StatusCode 400 (BadRequest) y se identifica con el código
    /// "PEDIDO_VALIDACION_FALLO" para que el frontend pueda capturarla y:
    /// 1. Preguntar al usuario si quiere crear el pedido sin pasar validación
    /// 2. Mostrar mensaje específico de error de validación
    ///
    /// Flujo Frontend (Nesto):
    /// - PedidoVentaService / PlantillaVentaService detectan código "PEDIDO_VALIDACION_FALLO"
    /// - Lanzan System.ComponentModel.DataAnnotations.ValidationException
    /// - DetallePedidoViewModel / PlantillaVentaViewModel capturan ValidationException
    /// - Preguntan al usuario: "¿Crear sin pasar validación?"
    ///
    /// Ejemplo de uso:
    /// <code>
    /// if (!respuestaValidacion.ValidacionSuperada)
    /// {
    ///     throw new PedidoValidacionException(
    ///         respuestaValidacion.Motivo,
    ///         respuestaValidacion,
    ///         empresa: pedido.Empresa,
    ///         pedido: pedido.Numero,
    ///         cliente: pedido.Cliente,
    ///         usuario: usuario);
    /// }
    /// </code>
    /// </summary>
    public class PedidoValidacionException : NestoBusinessException
    {
        /// <summary>
        /// Respuesta de validación completa con todos los motivos y errores
        /// </summary>
        public RespuestaValidacion RespuestaValidacion { get; }

        /// <summary>
        /// Constructor principal para errores de validación de pedidos
        /// </summary>
        /// <param name="mensaje">Mensaje descriptivo del error de validación (normalmente RespuestaValidacion.Motivo)</param>
        /// <param name="respuestaValidacion">Objeto completo con todos los detalles de validación</param>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedido">Número de pedido</param>
        /// <param name="cliente">Código de cliente</param>
        /// <param name="usuario">Usuario que ejecutó la operación</param>
        public PedidoValidacionException(
            string mensaje,
            RespuestaValidacion respuestaValidacion,
            string empresa = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : base(mensaje, new ErrorContext
            {
                ErrorCode = "PEDIDO_VALIDACION_FALLO",
                Empresa = empresa,
                Pedido = pedido,
                Cliente = cliente,
                Usuario = usuario
            })
        {
            RespuestaValidacion = respuestaValidacion;

            // Usar BadRequest (400) en lugar de InternalServerError (500)
            // porque es un error de validación, no un error del servidor
            StatusCode = HttpStatusCode.BadRequest;

            // Agregar detalles de la validación al contexto
            if (respuestaValidacion != null)
            {
                Context.WithData("ValidacionSuperada", respuestaValidacion.ValidacionSuperada);
                Context.WithData("AutorizadaDenegadaExpresamente", respuestaValidacion.AutorizadaDenegadaExpresamente);

                if (respuestaValidacion.Motivos != null && respuestaValidacion.Motivos.Count > 0)
                {
                    Context.WithData("Motivos", respuestaValidacion.Motivos);
                }

                if (respuestaValidacion.Errores != null && respuestaValidacion.Errores.Count > 0)
                {
                    Context.WithData("Errores", respuestaValidacion.Errores);
                }
            }
        }

        /// <summary>
        /// Constructor simplificado cuando no se tiene RespuestaValidacion completa
        /// </summary>
        /// <param name="mensaje">Mensaje descriptivo del error de validación</param>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedido">Número de pedido</param>
        /// <param name="cliente">Código de cliente</param>
        /// <param name="usuario">Usuario que ejecutó la operación</param>
        public PedidoValidacionException(
            string mensaje,
            string empresa = null,
            int? pedido = null,
            string cliente = null,
            string usuario = null)
            : this(mensaje, null, empresa, pedido, cliente, usuario)
        {
        }
    }
}
