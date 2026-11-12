using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857: un pedido de compra ENVIADO y sin recibir (alguna línea -1/1 con Enviado), con lo que sirve para reconocerlo.</summary>
    public class PedidoCompraEnAire
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        /// <summary>Proveedores.Web (de ahí sale un dominio).</summary>
        public string Web { get; set; }
        /// <summary>Correos de las personas de contacto del proveedor.</summary>
        public List<string> Correos { get; set; } = new List<string>();
        /// <summary>CabPedidoCmp.[Fecha Modificación] (cuando se crea el pedido).</summary>
        public DateTime? FechaEnvio { get; set; }
        /// <summary>Solo en el histórico: CabPedidoCmp.Fecha.</summary>
        public DateTime? FechaPedido { get; set; }
        /// <summary>Solo en el histórico: el último albarán si ya no le queda nada pendiente; null si sigue en el aire.</summary>
        public DateTime? FechaRecibidoEntero { get; set; }
        /// <summary>Fase 3: líneas enviadas y sin recibir (en el job; en el histórico, las del filtro).</summary>
        public int LineasPendientes { get; set; }
        /// <summary>Fase 3: líneas ya albaraneadas del pedido (si hay, lo pendiente es un resto).</summary>
        public int LineasYaRecibidas { get; set; }
    }

    public class CandidatoPedidoCompra
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        /// <summary><see cref="AsignadorPedidosCompra.MOTIVO_NUMERO"/>, _CORREO, _DOMINIO o _NOMBRE.</summary>
        public string Motivo { get; set; }
    }

    public class AsignacionPedidoCompra
    {
        /// <summary>El proveedor, solo si todos los candidatos son de uno; null si no hay o hay varios.</summary>
        public string ProveedorPropuesto { get; set; }
        public List<CandidatoPedidoCompra> Candidatos { get; set; } = new List<CandidatoPedidoCompra>();
    }

    public class AlbaranPedidoCompra
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaAlbaran { get; set; }
    }

    /// <summary>
    /// NestoAPI#857: asignación EN SOMBRA (pura) de un correo de seguimiento a pedidos de compra en el aire. Por este orden, y se
    /// queda con el primer criterio que encuentra algo: (1) un número de pedido nuestro en el asunto o el cuerpo; (1 bis, fase 3) quien
    /// expide el paquete está en la memoria de remitentes del paquete (todos los pedidos en el aire de su proveedor); (2) la dirección
    /// del remitente (directo u original de un reenvío) es la de un contacto del proveedor; (3) su dominio es el del proveedor (web o
    /// contactos; nunca uno de correo gratuito, nuestro o de una agencia); (4) el nombre del proveedor sale en el texto. Con (2)-(4)
    /// TODOS los pedidos en el aire de ese proveedor son candidatos: no se elige uno a ciegas.
    /// </summary>
    public static class AsignadorPedidosCompra
    {
        public const string MOTIVO_NUMERO = "Número";
        public const string MOTIVO_CORREO = "Correo";
        public const string MOTIVO_DOMINIO = "Dominio";
        public const string MOTIVO_NOMBRE = "Nombre";
        /// <summary>Fase 3: quien expide el paquete está en la memoria de remitentes (dbo.ComprasRemitentesPaquete).</summary>
        public const string MOTIVO_REMITENTE_PAQUETE = "RemitentePaquete";
        /// <summary>Fase 3: heredado de otro correo del mismo envío (comparten un código de seguimiento, <see cref="MismoEnvio"/>).</summary>
        public const string MOTIVO_MISMO_ENVIO = "MismoEnvio";
        public const int MAXIMO_CANDIDATOS = 20;
        public const int LONGITUD_MINIMA_NOMBRE = 5;

        private static readonly Regex Numeros = new Regex(@"(?<![\d.,/])\d{4,8}(?!\d|[.,/]\d)", RegexOptions.Compiled);

        internal static readonly string[] DominiosGratuitos =
        {
            "gmail.com", "googlemail.com", "hotmail.com", "hotmail.es", "outlook.com", "outlook.es", "live.com", "msn.com", "yahoo.com",
            "yahoo.es", "icloud.com", "me.com", "gmx.com", "gmx.es", "protonmail.com", "proton.me", "telefonica.net", "terra.es", "ono.com"
        };

        private static readonly HashSet<string> SufijosSociedad = new HashSet<string>
        {
            "S", "L", "A", "U", "SL", "SA", "SLU", "SAU", "SLL", "SC", "CB", "COOP", "SCOOP", "LTD", "LIMITED", "GMBH", "SRL", "SPA",
            "INC", "LLC", "BV", "SAS", "SARL", "AG", "CO", "Y", "CIA", "SLNE", "SCP", "SCCL", "SCL", "SRLS", "PLC", "CORP", "KG", "OHG", "NV", "SPRL"
        };

        /// <param name="remitentePaquete">Quién expide el paquete según el aviso (<see cref="ExtractorRemitentePaquete"/>).</param>
        /// <param name="recordados">La memoria de remitentes del paquete (dbo.ComprasRemitentesPaquete), como dato.</param>
        public static AsignacionPedidoCompra Asignar(CorreoCompras correo, IEnumerable<string> remitentesOriginales, IEnumerable<PedidoCompraEnAire> pedidos,
            string remitentePaquete = null, IEnumerable<RemitentePaqueteRecordado> recordados = null)
        {
            var asignacion = new AsignacionPedidoCompra();
            List<PedidoCompraEnAire> enAire = (pedidos ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null).ToList();
            if (correo == null || !enAire.Any())
            {
                return asignacion;
            }

            // (1) Número de pedido nuestro
            string textoCompleto = (correo.Asunto ?? string.Empty) + "\n" + correo.Texto;
            var numeros = new HashSet<int>(Numeros.Matches(textoCompleto).Cast<Match>()
                .Select(m => int.TryParse(m.Value, out int n) ? n : -1).Where(n => n > 0));
            List<PedidoCompraEnAire> porNumero = enAire.Where(p => numeros.Contains(p.Pedido)).ToList();
            if (porNumero.Any())
            {
                return Componer(porNumero, MOTIVO_NUMERO);
            }

            // (1 bis) Fase 3: quien expide el paquete ya lo ha dicho Compras de qué proveedor es
            List<PedidoCompraEnAire> porRemitente = PorRemitentePaquete(enAire, remitentePaquete, recordados);
            if (porRemitente.Any())
            {
                return Componer(porRemitente, MOTIVO_REMITENTE_PAQUETE);
            }

            // (2)-(4) Por proveedor
            List<string> direcciones = new[] { correo.RemitenteDireccion }
                .Concat(remitentesOriginales ?? Enumerable.Empty<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim().ToLowerInvariant())
                .Where(d => !EsPropio(TextoCorreo.Dominio(d)))
                .Distinct()
                .ToList();
            List<string> dominios = direcciones.Select(TextoCorreo.Dominio)
                .Where(d => d != null && !EsGratuito(d) && !AgenciasEntradaRegistradas.EsDeAgenciaConocida(d))
                .Distinct().ToList();
            string textoNormalizado = " " + TextoCorreo.Normalizar((correo.RemitenteNombre ?? string.Empty) + " " + textoCompleto) + " ";

            var proveedores = enAire.GroupBy(p => (p.Empresa?.Trim() ?? string.Empty) + "|" + (p.Proveedor?.Trim() ?? string.Empty)).ToList();
            foreach (string motivo in new[] { MOTIVO_CORREO, MOTIVO_DOMINIO, MOTIVO_NOMBRE })
            {
                List<PedidoCompraEnAire> casan = proveedores
                    .Where(g => CasaProveedor(g.First(), motivo, direcciones, dominios, textoNormalizado))
                    .SelectMany(g => g)
                    .ToList();
                if (casan.Any())
                {
                    return Componer(casan, motivo);
                }
            }
            return asignacion;
        }

        /// <summary>Los pedidos en el aire del proveedor que Compras asoció a ese remitente del paquete (misma empresa).</summary>
        internal static List<PedidoCompraEnAire> PorRemitentePaquete(List<PedidoCompraEnAire> enAire, string remitentePaquete,
            IEnumerable<RemitentePaqueteRecordado> recordados)
        {
            string clave = ExtractorRemitentePaquete.Normalizar(remitentePaquete);
            if (clave == null || recordados == null)
            {
                return new List<PedidoCompraEnAire>();
            }
            RemitentePaqueteRecordado recordado = recordados
                .FirstOrDefault(r => r != null && ExtractorRemitentePaquete.Normalizar(r.RemitentePaquete) == clave && !string.IsNullOrWhiteSpace(r.Proveedor));
            if (recordado == null)
            {
                return new List<PedidoCompraEnAire>();
            }
            return enAire.Where(p => string.Equals(p.Proveedor?.Trim(), recordado.Proveedor.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Empresa?.Trim(), recordado.Empresa?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static bool CasaProveedor(PedidoCompraEnAire proveedor, string motivo, List<string> direcciones, List<string> dominios, string texto)
        {
            List<string> correos = (proveedor.Correos ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLowerInvariant()).ToList();
            switch (motivo)
            {
                case MOTIVO_CORREO:
                    return correos.Any(direcciones.Contains);
                case MOTIVO_DOMINIO:
                    var suyos = correos.Select(TextoCorreo.Dominio).Concat(new[] { DominioWeb(proveedor.Web) })
                        .Where(d => d != null && !EsGratuito(d) && !EsPropio(d)).ToList();
                    return dominios.Any(d => suyos.Any(s => TextoCorreo.HostDe(d, s) || TextoCorreo.HostDe(s, d)));
                default:
                    string nombre = NombreSinSociedad(proveedor.NombreProveedor);
                    return nombre.Length >= LONGITUD_MINIMA_NOMBRE && texto.Contains(" " + nombre + " ");
            }
        }

        private static AsignacionPedidoCompra Componer(List<PedidoCompraEnAire> pedidos, string motivo)
        {
            List<string> distintos = pedidos.Select(p => p.Proveedor?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new AsignacionPedidoCompra
            {
                ProveedorPropuesto = distintos.Count == 1 ? distintos[0] : null,
                Candidatos = pedidos
                    .GroupBy(p => (p.Empresa?.Trim(), p.Pedido)).Select(g => g.First())
                    .OrderByDescending(p => p.FechaEnvio ?? DateTime.MinValue).ThenByDescending(p => p.Pedido)
                    .Take(MAXIMO_CANDIDATOS)
                    .Select(p => new CandidatoPedidoCompra { Empresa = p.Empresa?.Trim(), Pedido = p.Pedido, Proveedor = p.Proveedor?.Trim(), Motivo = motivo })
                    .ToList()
            };
        }

        /// <summary>«COSMÉTICOS EJEMPLO, S.L.» → «COSMETICOS EJEMPLO».</summary>
        internal static string NombreSinSociedad(string nombre)
        {
            List<string> palabras = TextoCorreo.Normalizar(nombre).Split(' ').Where(p => p.Length > 0).ToList();
            // La forma societaria entera, también partida en letras sueltas («S. L. U.», «C.B.», «S.COOP.»)
            while (palabras.Count > 1)
            {
                string ultima = palabras[palabras.Count - 1];
                if (SufijosSociedad.Contains(ultima))
                {
                    palabras.RemoveAt(palabras.Count - 1);
                }
                else if (palabras.Count > 2 && SufijosSociedad.Contains(palabras[palabras.Count - 2] + ultima))
                {
                    palabras.RemoveRange(palabras.Count - 2, 2);
                }
                else
                {
                    break;
                }
            }
            return string.Join(" ", palabras);
        }

        internal static string DominioWeb(string web)
        {
            if (string.IsNullOrWhiteSpace(web))
            {
                return null;
            }
            string limpia = web.Trim();
            if (!limpia.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                limpia = "http://" + limpia;
            }
            if (!Uri.TryCreate(limpia, UriKind.Absolute, out Uri uri) || !uri.Host.Contains("."))
            {
                return null;
            }
            string host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;
        }

        internal static bool EsGratuito(string dominio) => DominiosGratuitos.Any(g => TextoCorreo.HostDe(dominio, g));
        internal static bool EsPropio(string dominio) => AgenciaEntradaGenerica.DominiosPropios.Any(g => TextoCorreo.HostDe(dominio, g));

        /// <summary>
        /// Medición: de los candidatos, el que se recibió primero el día del correo o después (un albarán de antes no puede ser
        /// la mercancía que avisaba el correo). Null si ninguno se ha recibido aún.
        /// </summary>
        public static AlbaranPedidoCompra DecidirReal(DateTime recibido, IEnumerable<CandidatoPedidoCompra> candidatos, IEnumerable<AlbaranPedidoCompra> albaranes)
        {
            var claves = new HashSet<string>((candidatos ?? Enumerable.Empty<CandidatoPedidoCompra>()).Select(c => Clave(c.Empresa, c.Pedido)));
            return (albaranes ?? Enumerable.Empty<AlbaranPedidoCompra>())
                .Where(a => a != null && claves.Contains(Clave(a.Empresa, a.Pedido)) && a.FechaAlbaran.Date >= recibido.Date)
                .OrderBy(a => a.FechaAlbaran).ThenBy(a => a.Pedido)
                .FirstOrDefault();
        }

        /// <summary>
        /// Histórico (pura): de los pedidos de <see cref="IRepositorioCorreosSeguimiento.LeerPedidosDesde"/>, los que estaban en el
        /// aire el día del correo: hechos ese día o antes y sin recibir entero antes de ese día. Así un correo de marzo no se
        /// asigna a los pedidos en el aire de hoy (y la medición no da por bueno un albarán de un pedido que aún no existía).
        /// </summary>
        public static List<PedidoCompraEnAire> EnAireEl(IEnumerable<PedidoCompraEnAire> pedidos, DateTime dia)
        {
            return (pedidos ?? Enumerable.Empty<PedidoCompraEnAire>())
                .Where(p => p != null)
                .Where(p => (p.FechaPedido ?? p.FechaEnvio) == null || (p.FechaPedido ?? p.FechaEnvio).Value.Date <= dia.Date)
                .Where(p => p.FechaRecibidoEntero == null || p.FechaRecibidoEntero.Value.Date >= dia.Date)
                .ToList();
        }

        internal static string Clave(string empresa, int pedido) => (empresa?.Trim() ?? string.Empty) + "/" + pedido;

        public static string Serializar(List<CandidatoPedidoCompra> candidatos) =>
            candidatos == null || !candidatos.Any() ? null : JsonConvert.SerializeObject(candidatos);

        public static List<CandidatoPedidoCompra> Deserializar(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<CandidatoPedidoCompra>();
            }
            try
            {
                return JsonConvert.DeserializeObject<List<CandidatoPedidoCompra>>(json) ?? new List<CandidatoPedidoCompra>();
            }
            catch (JsonException)
            {
                return new List<CandidatoPedidoCompra>();
            }
        }
    }
}
