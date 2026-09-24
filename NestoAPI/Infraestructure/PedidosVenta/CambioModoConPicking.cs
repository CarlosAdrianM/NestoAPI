using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#533: el modo de entrega no se cambia cuando el pedido ya está saliendo. Caso 926879
    /// (23/09/26): se pasó a «Todo junto» con el picking hecho; el cambio se guardó, el picking no se
    /// deshizo y almacén no se enteró.
    /// <list type="bullet">
    /// <item>Con picking en alguna línea viva, o con albarán de HOY, se rechaza.</item>
    /// <item>Con una parte entregada días antes y sin picking ahora, sí se puede (que el resto salga junto).</item>
    /// </list>
    /// Al rechazarlo, el cliente ofrece pedírselo a almacén (<see cref="CorreoSolicitud"/>).
    /// </summary>
    public static class CambioModoConPicking
    {
        /// <summary>Por qué no se puede cambiar el modo, o null si se puede. Pura para testear sin BD.</summary>
        internal static string Motivo(IEnumerable<LinPedidoVta> lineas, DateTime hoy)
        {
            List<LinPedidoVta> todas = lineas?.ToList() ?? new List<LinPedidoVta>();
            bool conPicking = todas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE
                && l.Estado < Constantes.EstadosLineaVenta.ALBARAN
                && (l.Picking ?? 0) != 0);
            if (conPicking)
            {
                return "Este pedido ya está en preparación (tiene picking) y el modo de entrega ya no se puede cambiar desde aquí. " +
                    "Si hace falta, se lo podemos pedir a almacén: lo intentarán, pero puede que ya no llegue a tiempo.";
            }
            bool albaranDeHoy = todas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN
                && l.Fecha_Albarán.HasValue && l.Fecha_Albarán.Value.Date == hoy.Date);
            if (albaranDeHoy)
            {
                return "Parte de este pedido ha salido hoy, así que el modo de entrega ya no se puede cambiar desde aquí. " +
                    "Si hace falta, se lo podemos pedir a almacén: lo intentarán, pero puede que ya no llegue a tiempo.";
            }
            return null;
        }

        /// <summary>El correo a almacén pidiendo el cambio (sin prometer nada). Puro para testear.</summary>
        internal static MailMessage CorreoSolicitud(string empresa, int pedido, string cliente, byte modoActual, byte modoDeseado,
            string usuario, string comentario)
        {
            string actual = Constantes.Pedidos.ModosServicio.Nombre(modoActual);
            string deseado = Constantes.Pedidos.ModosServicio.Nombre(modoDeseado);
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es"),
                Subject = $"Cambiar el modo de entrega del pedido {pedido} a «{deseado}»",
                IsBodyHtml = true,
                Body = $"<p>{WebUtility.HtmlEncode(usuario)} pide cambiar el modo de entrega del pedido <b>{pedido}</b> " +
                    $"(cliente {WebUtility.HtmlEncode(cliente?.Trim())}, empresa {WebUtility.HtmlEncode(empresa?.Trim())}):</p>" +
                    $"<ul><li>Ahora: <b>{WebUtility.HtmlEncode(actual)}</b></li><li>Quiere: <b>{WebUtility.HtmlEncode(deseado)}</b></li></ul>" +
                    (string.IsNullOrWhiteSpace(comentario) ? string.Empty : $"<p>Comentario: {WebUtility.HtmlEncode(comentario.Trim())}</p>") +
                    "<p>Nesto no ha dejado cambiarlo porque el pedido ya tiene picking (o ha salido parte hoy). " +
                    $"Si todavía estáis a tiempo, cambiadlo vosotros; si no, avisad a {WebUtility.HtmlEncode(usuario)} de que ya no se puede.</p>"
            };
            mail.To.Add(new MailAddress(Constantes.Correos.ALMACEN));
            return mail;
        }
    }

    /// <summary>
    /// NestoAPI#533. JSON (GlobalExceptionFilter): error.code = "MODO_CON_PICKING" y error.message; con
    /// ese código Nesto y NestoApp ofrecen «Pedir el cambio a almacén».
    /// </summary>
    public class ModoConPickingException : NestoBusinessException
    {
        public const string CODIGO = "MODO_CON_PICKING";

        public ModoConPickingException(string mensaje, string empresa, int pedido)
            : base(mensaje, new ErrorContext { ErrorCode = CODIGO, Empresa = empresa, Pedido = pedido })
        {
        }
    }
}
