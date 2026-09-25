using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#541: cambiar en la ficha los días que el cliente cierra (Clientes.DiasEnServir) cuando ya hay
    /// pedidos suyos con picking. Caso real (pedido 925633, 15/09/26): el vendedor puso «cierra los lunes» con
    /// el picking ya sacado y tuvo que avisar al almacén por correo. El picking solo mira los días ANTES de
    /// asignarse (#362), así que un pedido en preparación sale igual y la agencia no podrá entregar hasta que
    /// el cliente abra.
    ///
    /// <para>Decisión de Carlos (25/09/26), opción B: la ficha NO se guarda a la primera. La API devuelve
    /// <see cref="DiasEnServirConPickingException"/> (código DIAS_CON_PICKING) con los pedidos afectados; el
    /// cliente pregunta «¿Avisamos a almacén?». Sí → repite el PUT con <c>ConfirmarDiasEnServirConPicking</c>
    /// y la API guarda y manda el correo, solo a almacén. No → se cancela el cambio. Solo cuenta cuando el
    /// cambio CIERRA un día que estaba abierto; abrir días nunca molesta.</para>
    /// </summary>
    public static class CambioDiasEnServirConPicking
    {
        public const string CODIGO = "DIAS_CON_PICKING";
        internal static readonly string[] NOMBRES_DIAS = { "lunes", "martes", "miércoles", "jueves", "viernes" };

        /// <summary>
        /// El núcleo, llamado al preparar la modificación. Devuelve el correo que hay que mandar a almacén DESPUÉS
        /// de guardar (o null si no hay nada que avisar). Lanza si hay conflicto y el cambio no viene confirmado.
        /// Las líneas solo se consultan si el cambio cierra algún día.
        /// </summary>
        internal static MailMessage Comprobar(Cliente clienteDB, string diasDespues, Func<IEnumerable<LinPedidoVta>> lineasDelContacto,
            bool confirmado, string usuario)
        {
            List<string> cerrados = DiasQueSeCierran(clienteDB?.DiasEnServir, diasDespues);
            if (!cerrados.Any())
            {
                return null;
            }
            List<int> pedidos = PedidosConPickingVivo(lineasDelContacto());
            if (!pedidos.Any())
            {
                return null;
            }
            if (!confirmado)
            {
                throw new DiasEnServirConPickingException(Motivo(cerrados, pedidos), clienteDB.Empresa, clienteDB.Nº_Cliente, clienteDB.Contacto, pedidos);
            }
            return CorreoAviso(clienteDB, diasDespues, cerrados, pedidos, usuario);
        }

        /// <summary>Los días (lunes..viernes) que estaban abiertos ('1') y pasan a cerrados ('0').</summary>
        internal static List<string> DiasQueSeCierran(string antes, string despues)
        {
            string a = antes?.Trim();
            string d = despues?.Trim();
            var cerrados = new List<string>();
            if (d == null || d.Length != 5)
            {
                return cerrados;
            }
            for (int i = 0; i < 5; i++)
            {
                // Sin valor anterior (o roto) se considera que abría: un dato defectuoso no debe callar el aviso
                bool abria = a == null || a.Length != 5 || a[i] != '0';
                if (abria && d[i] == '0')
                {
                    cerrados.Add(NOMBRES_DIAS[i]);
                }
            }
            return cerrados;
        }

        /// <summary>Los pedidos con alguna línea viva (pendiente o en curso) que ya tiene picking, como en #533.</summary>
        internal static List<int> PedidosConPickingVivo(IEnumerable<LinPedidoVta> lineas)
        {
            return (lineas ?? Enumerable.Empty<LinPedidoVta>())
                .Where(l => l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE
                    && l.Estado < Constantes.EstadosLineaVenta.ALBARAN
                    && (l.Picking ?? 0) != 0)
                .Select(l => l.Número)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        internal static string Motivo(List<string> cerrados, List<int> pedidos)
        {
            string dias = string.Join(", ", cerrados);
            string lista = string.Join(", ", pedidos);
            return pedidos.Count == 1
                ? $"El pedido {lista} de este cliente ya está en preparación (tiene picking) y va a salir igual aunque ahora cierre los {dias}. " +
                  "¿Avisamos a almacén? Si no, el cambio de días no se guarda."
                : $"Los pedidos {lista} de este cliente ya están en preparación (tienen picking) y van a salir igual aunque ahora cierre los {dias}. " +
                  "¿Avisamos a almacén? Si no, el cambio de días no se guarda.";
        }

        /// <summary>El correo a almacén, y solo a almacén (Carlos, 25/09/26). Puro para testear.</summary>
        internal static MailMessage CorreoAviso(Cliente cliente, string diasDespues, List<string> cerrados, List<int> pedidos, string usuario)
        {
            string quien = string.IsNullOrWhiteSpace(usuario) ? "Un usuario" : usuario.Trim();
            string dias = string.Join(", ", cerrados);
            string lista = string.Join(", ", pedidos.Select(p => $"<b>{p}</b>"));
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es"),
                Subject = $"El cliente {cliente.Nº_Cliente?.Trim()}/{cliente.Contacto?.Trim()} cierra ahora los {dias} y tiene picking: pedido(s) {string.Join(", ", pedidos)}",
                IsBodyHtml = true,
                Body = $"<p>{WebUtility.HtmlEncode(quien)} ha cambiado en la ficha del cliente <b>{WebUtility.HtmlEncode(cliente.Nº_Cliente?.Trim())}/{WebUtility.HtmlEncode(cliente.Contacto?.Trim())}</b> " +
                    $"({WebUtility.HtmlEncode(cliente.Nombre?.Trim())}) los días que abre: ahora <b>cierra los {WebUtility.HtmlEncode(dias)}</b> " +
                    $"(días de servir: {WebUtility.HtmlEncode(cliente.DiasEnServir?.Trim())} → {WebUtility.HtmlEncode(diasDespues?.Trim())}, de lunes a viernes, 1 abre y 0 cierra).</p>" +
                    $"<p>Ya tenía picking: {lista}. Nesto ha guardado el cambio, pero esos pedidos van a salir igual: " +
                    "si la entrega cae en un día que ahora cierra, avisad a la agencia o retened el envío.</p>"
            };
            mail.To.Add(new MailAddress(Constantes.Correos.ALMACEN));
            return mail;
        }
    }

    /// <summary>
    /// NestoAPI#541. JSON (GlobalExceptionFilter): error.code = "DIAS_CON_PICKING", error.message y en details
    /// empresa, cliente, contacto y pedidos; con ese código Nesto y NestoApp preguntan «¿Avisamos a almacén?».
    /// </summary>
    public class DiasEnServirConPickingException : NestoBusinessException
    {
        public DiasEnServirConPickingException(string mensaje, string empresa, string cliente, string contacto, List<int> pedidos)
            : base(mensaje, new ErrorContext { ErrorCode = CambioDiasEnServirConPicking.CODIGO, Empresa = empresa?.Trim(), Cliente = cliente?.Trim() })
        {
            Context.AdditionalData["contacto"] = contacto?.Trim();
            Context.AdditionalData["pedidos"] = pedidos;
        }
    }
}
