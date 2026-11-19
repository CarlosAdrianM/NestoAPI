using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857f (nivel 1): lo que se ha sacado de un aviso de salida con un formato.</summary>
    public class AvisoSalidaLeido
    {
        /// <summary>El formato que lo ha leído (<see cref="IFormatoAvisoSalida.Nombre"/>).</summary>
        public string Formato { get; set; }
        /// <summary>La agencia por la que viene, o null.</summary>
        public IAgenciaEntrada Agencia { get; set; }
        /// <summary>Los números de envío (seguimiento) que da, sin repetir.</summary>
        public List<string> Codigos { get; set; } = new List<string>();
        /// <summary>La entrega que anuncia el propio aviso (NO la de la agencia), o null.</summary>
        public EntregaEstimada Entrega { get; set; }
    }

    /// <summary>
    /// NestoAPI#857g: la CONFIRMACIÓN de un pedido que manda el proveedor al recibirlo (antes de que salga). No es un envío: no pone fecha ni
    /// se guarda como correo de seguimiento; enlaza su número de pedido con el nuestro (dbo.ComprasConfirmacionesProveedor).
    /// </summary>
    public class ConfirmacionPedidoProveedor
    {
        /// <summary>El formato que la ha leído.</summary>
        public string Formato { get; set; }
        /// <summary>El número de pedido del proveedor («SO253360»).</summary>
        public string PedidoProveedor { get; set; }
        /// <summary>La referencia que le dimos (si la da): puede ser nuestro número de pedido.</summary>
        public string ReferenciaCliente { get; set; }
        /// <summary>La agencia que anuncia («El pedido será enviado por: MAILBOXES»), o null.</summary>
        public IAgenciaEntrada Agencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#857f (nivel 1, Carlos 19/11/26): un FORMATO de aviso de salida de un proveedor (en la práctica, el de su ERP: los proveedores
    /// que comparten ERP comparten formato). Sabe reconocer su aviso por el texto (cuerpo o PDF) y sacar, como mínimo, la agencia y el número
    /// de envío y, si puede, nuestro pedido y la entrega que anuncia. Nada propio de una agencia: la agencia la busca por los nombres que da
    /// cada clase de agencia (<see cref="AgenciaEntradaPorPatrones.NombresEnTexto"/>).
    /// Formato nuevo = una clase que herede de <see cref="FormatoAvisoSalidaBase"/> + una línea en <see cref="FormatosAvisoSalida.Registrados"/>.
    /// </summary>
    public interface IFormatoAvisoSalida
    {
        string Nombre { get; }
        /// <summary>¿Es un aviso con este formato? (asunto + cuerpo del correo, o el texto de su PDF)</summary>
        bool Reconoce(string texto);
        IAgenciaEntrada Agencia(string texto, IEnumerable<IAgenciaEntrada> agencias);
        List<string> Codigos(string texto);
        EntregaEstimada Entrega(string texto, DateTime recibido);
        PedidoCompraEnAire Pedido(string texto, IEnumerable<PedidoCompraEnAire> delProveedor, IEnumerable<PedidoCompraEnAire> enAire);
        /// <summary>NestoAPI#857g: ¿es la CONFIRMACIÓN de un pedido (no una salida) con este formato? Null si no.</summary>
        ConfirmacionPedidoProveedor Confirmacion(string asunto, string texto, IEnumerable<IAgenciaEntrada> agencias);
    }

    /// <summary>
    /// El núcleo común de los formatos (y el formato genérico tal cual): la agencia, la que el texto nombre tras una etiqueta
    /// (<see cref="AgenciaPorNombreEnTexto"/>); los códigos, los que lleven etiqueta («Nº de seguimiento: …»); la entrega, el extractor común
    /// (<see cref="ExtractorEntregaEstimada"/>); el pedido, <see cref="ExtractorAvisoSalida.Pedido"/>.
    /// </summary>
    public abstract class FormatoAvisoSalidaBase : IFormatoAvisoSalida
    {
        public abstract string Nombre { get; }
        public abstract bool Reconoce(string texto);

        public virtual IAgenciaEntrada Agencia(string texto, IEnumerable<IAgenciaEntrada> agencias) =>
            AgenciaPorNombreEnTexto.Buscar(texto, agencias ?? AgenciasEntradaRegistradas.Todas);

        public virtual List<string> Codigos(string texto)
        {
            var codigos = new List<string>();
            foreach (Match m in AgenciaEntradaPorPatrones.CodigoConEtiqueta.Matches(texto ?? string.Empty))
            {
                Anadir(codigos, m.Groups[1].Value);
            }
            return codigos;
        }

        public virtual EntregaEstimada Entrega(string texto, DateTime recibido) => ExtractorEntregaEstimada.Extraer(texto, recibido);

        public virtual PedidoCompraEnAire Pedido(string texto, IEnumerable<PedidoCompraEnAire> delProveedor, IEnumerable<PedidoCompraEnAire> enAire) =>
            ExtractorAvisoSalida.Pedido(texto, delProveedor, enAire);

        public virtual ConfirmacionPedidoProveedor Confirmacion(string asunto, string texto, IEnumerable<IAgenciaEntrada> agencias) => null;

        protected static void Anadir(List<string> codigos, string codigo)
        {
            string limpio = codigo?.Trim();
            if (AgenciaEntradaPorPatrones.EsCodigo(limpio) && !codigos.Contains(limpio, StringComparer.OrdinalIgnoreCase))
            {
                codigos.Add(limpio);
            }
        }
    }

    /// <summary>El respaldo: cualquier aviso que no reconozca un formato concreto (lo de antes de #857f).</summary>
    public class FormatoAvisoSalidaGenerico : FormatoAvisoSalidaBase
    {
        public const string NOMBRE = "(genérico)";
        public override string Nombre => NOMBRE;
        public override bool Reconoce(string texto) => true;
    }

    /// <summary>
    /// El aviso de DDUUEETT (18/11/26, logistics@dduueett.com): «Transportista: SCHENKER STANDARD» + «Número de seguimiento: ESMNR264773770»
    /// en el cuerpo; y su packing list en PDF: «Compañía de transporte: …» + «REF 220923». La agencia, solo la de esa línea; el número de envío,
    /// el de «Número de seguimiento:». Si otro proveedor usa el mismo ERP, usa esta misma clase.
    /// </summary>
    public class FormatoAvisoSalidaDduueett : FormatoAvisoSalidaBase
    {
        public const string NOMBRE = "DDUUEETT";
        private static readonly Regex LineaTransportista = new Regex(@"^[ \t*]*(?:transportista|compa[nñ][ií]a\s+de\s+transporte)[ \t*]*:[^\n]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex NumeroSeguimiento = new Regex(@"^[ \t*]*n[uú]mero\s+de\s+seguimiento[ \t*]*:[ \t]*([A-Za-z0-9][A-Za-z0-9\-]{5,39})\b",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex PackingList = new Regex(@"(?<![\p{L}])packing\s*\n?\s*list(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override string Nombre => NOMBRE;

        public override bool Reconoce(string texto)
        {
            string t = Limpio(texto);
            return LineaTransportista.IsMatch(t) && (NumeroSeguimiento.IsMatch(t) || PackingList.IsMatch(t));
        }

        public override IAgenciaEntrada Agencia(string texto, IEnumerable<IAgenciaEntrada> agencias)
        {
            foreach (Match linea in LineaTransportista.Matches(Limpio(texto)))
            {
                IAgenciaEntrada agencia = AgenciaPorNombreEnTexto.Buscar(linea.Value, agencias ?? AgenciasEntradaRegistradas.Todas);
                if (agencia != null)
                {
                    return agencia;
                }
            }
            return null;
        }

        public override List<string> Codigos(string texto)
        {
            var codigos = new List<string>();
            foreach (Match m in NumeroSeguimiento.Matches(Limpio(texto)))
            {
                Anadir(codigos, m.Groups[1].Value);
            }
            return codigos;
        }

        private static string Limpio(string texto) => (texto ?? string.Empty).Replace("\r", string.Empty);
    }

    /// <summary>
    /// NestoAPI#857g: Weelko (noreply@weelko.com; su tienda parece Odoo, pero el texto es propio: no se ha visto en otro proveedor). Hoy solo su
    /// CONFIRMACIÓN de pedido («Pedido SO253360»: «Hemos recibido su pedido correctamente y lo estamos tramitando… Número del pedidor / Referencia
    /// del cliente … SO253360 21325 … El pedido será enviado por: MAILBOXES … Resumen del pedido»): el pedido del proveedor (SO…), nuestra
    /// referencia (si la da) y la agencia que anuncia. Su aviso de «ha salido» aún no se ha visto: no lo reconoce (lo lee el genérico). Ni su
    /// factura («Factura BCN/2026/27757 (con referencia SO252231)») ni su publicidad (info@weelko.com) son confirmaciones.
    /// </summary>
    public class FormatoAvisoSalidaWeelko : FormatoAvisoSalidaBase
    {
        public const string NOMBRE = "Weelko";
        private static readonly Regex Recibido = new Regex(@"hemos\s+recibido\s+su\s+pedido", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Resumen = new Regex(@"resumen\s+del\s+pedido", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumeroPedidor = new Regex(@"n[uú]mero\s+del\s+pedid", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PedidoEnAsunto = new Regex(@"(?<![\p{L}\p{N}])pedido\s+(SO\d{4,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PedidoEnTexto = new Regex(@"(?<![\p{L}\p{N}])(SO\d{4,10})(?![\p{L}\p{N}])", RegexOptions.Compiled);
        private static readonly Regex ReferenciaCliente = new Regex(@"referencia\s+del\s+cliente", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override string Nombre => NOMBRE;

        /// <summary>Su aviso de salida aún no se conoce.</summary>
        public override bool Reconoce(string texto) => false;

        public override ConfirmacionPedidoProveedor Confirmacion(string asunto, string texto, IEnumerable<IAgenciaEntrada> agencias)
        {
            string t = texto ?? string.Empty;
            if (!Recibido.IsMatch(t) || !Resumen.IsMatch(t) || !NumeroPedidor.IsMatch(t))
            {
                return null;
            }
            Match enAsunto = PedidoEnAsunto.Match(asunto ?? string.Empty);
            Match enTexto = PedidoEnTexto.Match(t);
            string pedido = enAsunto.Success ? enAsunto.Groups[1].Value : enTexto.Success ? enTexto.Groups[1].Value : null;
            if (pedido == null)
            {
                return null;
            }
            string referencia = null;
            if (ReferenciaCliente.IsMatch(t))
            {
                // Tabla «Fecha de compra | Número del pedidor | Referencia del cliente»: la referencia va justo detrás del número del pedido
                Match m = new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(pedido) + @"\s+(\d[0-9A-Za-z\-/]{2,19})(?![\p{L}\p{N}])").Match(t);
                referencia = m.Success ? m.Groups[1].Value : null;
            }
            return new ConfirmacionPedidoProveedor
            {
                Formato = NOMBRE,
                PedidoProveedor = pedido.ToUpperInvariant(),
                ReferenciaCliente = referencia,
                Agencia = AgenciaPorNombreEnTexto.Buscar(t, agencias ?? AgenciasEntradaRegistradas.Todas)
            };
        }
    }

    /// <summary>NestoAPI#857f (nivel 1): los formatos de aviso conocidos, en orden (el primero que reconoce el texto, gana); si ninguno, el genérico.</summary>
    public static class FormatosAvisoSalida
    {
        public static readonly IReadOnlyList<IFormatoAvisoSalida> Registrados = new List<IFormatoAvisoSalida>
        {
            new FormatoAvisoSalidaDduueett(), new FormatoAvisoSalidaWeelko()
        }.AsReadOnly();

        /// <summary>NestoAPI#857g: la confirmación de pedido que reconozca algún formato, o null.</summary>
        public static ConfirmacionPedidoProveedor Confirmacion(string asunto, string texto, IEnumerable<IAgenciaEntrada> agencias = null,
            IEnumerable<IFormatoAvisoSalida> formatos = null)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            foreach (IFormatoAvisoSalida formato in formatos ?? Registrados)
            {
                ConfirmacionPedidoProveedor confirmacion = formato.Confirmacion(asunto, texto, agencias);
                if (confirmacion != null && !string.IsNullOrWhiteSpace(confirmacion.PedidoProveedor))
                {
                    return confirmacion;
                }
            }
            return null;
        }

        public static readonly IFormatoAvisoSalida Generico = new FormatoAvisoSalidaGenerico();

        public static IFormatoAvisoSalida Para(string texto, IEnumerable<IFormatoAvisoSalida> formatos = null) =>
            string.IsNullOrWhiteSpace(texto) ? Generico : (formatos ?? Registrados).FirstOrDefault(f => f.Reconoce(texto)) ?? Generico;

        public static bool EsGenerico(IFormatoAvisoSalida formato) => formato == null || formato is FormatoAvisoSalidaGenerico;

        /// <summary>Lee un texto con su formato (sin el pedido, que necesita los pedidos en el aire).</summary>
        public static AvisoSalidaLeido Leer(string texto, DateTime recibido, IEnumerable<IAgenciaEntrada> agencias)
        {
            IFormatoAvisoSalida formato = Para(texto);
            return new AvisoSalidaLeido
            {
                Formato = formato.Nombre,
                Agencia = formato.Agencia(texto, agencias),
                Codigos = formato.Codigos(texto),
                Entrega = formato.Entrega(texto, recibido)
            };
        }
    }
}
