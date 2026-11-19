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

    /// <summary>NestoAPI#857f (nivel 1): los formatos de aviso conocidos, en orden (el primero que reconoce el texto, gana); si ninguno, el genérico.</summary>
    public static class FormatosAvisoSalida
    {
        public static readonly IReadOnlyList<IFormatoAvisoSalida> Registrados = new List<IFormatoAvisoSalida>
        {
            new FormatoAvisoSalidaDduueett()
        }.AsReadOnly();

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
