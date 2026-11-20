using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.PedidosVenta.Autorizaciones
{
    /// <summary>Novedades 1258: los estados de una solicitud (columna Estado de dbo.PedidosAutorizacionesSolicitadas, con su CHECK).</summary>
    public static class EstadosAutorizacionPedido
    {
        public const string PENDIENTE = "Pendiente";
        public const string AUTORIZADA = "Autorizada";
        public const string DENEGADA = "Denegada";
        public const string ANULADA = "Anulada";
    }

    /// <summary>POST api/PedidosVenta/Autorizaciones: el pedido tal como se iba a guardar (el mismo cuerpo que POST api/PedidosVenta).</summary>
    public class SolicitarAutorizacionPedidoRequest
    {
        public PedidoVentaDTO Pedido { get; set; }
        /// <summary>Opcional, 500 como mucho.</summary>
        public string Comentario { get; set; }
    }

    /// <summary>POST …/{id}/Autorizar.</summary>
    public class AutorizarPedidoRequest
    {
        /// <summary>Opcional, 500 como mucho (se guarda en MotivoResolucion).</summary>
        public string Comentario { get; set; }
    }

    /// <summary>POST …/{id}/Denegar.</summary>
    public class DenegarPedidoRequest
    {
        /// <summary>Obligatorio, 1..500.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>Un error de la validación con su producto.</summary>
    public class ErrorAutorizacionPedidoDTO
    {
        public string Producto { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>201 de POST api/PedidosVenta/Autorizaciones.</summary>
    public class SolicitudAutorizacionCreadaDTO
    {
        public int Id { get; set; }
        public string Estado { get; set; }
        public List<string> Motivos { get; set; } = new List<string>();
        public List<ErrorAutorizacionPedidoDTO> Errores { get; set; } = new List<ErrorAutorizacionPedidoDTO>();
        /// <summary>A quién le ha llegado (nombres sin dominio).</summary>
        public List<string> Destinatarios { get; set; } = new List<string>();
    }

    /// <summary>GET api/PedidosVenta/Autorizaciones (y, con <see cref="Pedido"/>, GET …/{id}).</summary>
    public class AutorizacionPedidoDTO
    {
        public int Id { get; set; }
        public string Estado { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Solicitante { get; set; }
        public string NombreSolicitante { get; set; }
        public DateTime FechaSolicitud { get; set; }
        /// <summary>Base imponible del borrador.</summary>
        public decimal Importe { get; set; }
        public int NumeroLineas { get; set; }
        public List<string> Motivos { get; set; } = new List<string>();
        public List<ErrorAutorizacionPedidoDTO> Errores { get; set; } = new List<ErrorAutorizacionPedidoDTO>();
        public string Comentario { get; set; }
        public string UsuarioResolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string MotivoResolucion { get; set; }
        public int? PedidoCreado { get; set; }
        /// <summary>Solo en GET …/{id}: el pedido guardado.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public PedidoVentaDTO Pedido { get; set; }
    }

    /// <summary>200 de Autorizar, Denegar y DELETE.</summary>
    public class ResolucionAutorizacionPedidoDTO
    {
        public int Id { get; set; }
        public string Estado { get; set; }
        /// <summary>Solo al autorizar: el número del pedido creado.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Pedido { get; set; }
    }

    /// <summary>Una fila de la tabla (lo que se lee y escribe por SQL).</summary>
    public class FilaAutorizacionPedido
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Solicitante { get; set; }
        public DateTime FechaSolicitud { get; set; }
        /// <summary>Null en el listado (no se lee).</summary>
        public string PedidoJson { get; set; }
        /// <summary>JSON { Motivos: [..], Errores: [{Producto, Motivo}] }.</summary>
        public string Motivos { get; set; }
        public string Comentario { get; set; }
        public string Estado { get; set; }
        public string UsuarioResolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string MotivoResolucion { get; set; }
        public int? PedidoCreado { get; set; }
        public string Huella { get; set; }
        public string PermisoNecesario { get; set; }
        public decimal Importe { get; set; }
        public int NumeroLineas { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Los almacenes de las líneas, separados por comas (para la regla de Tiendas sin leer el JSON).</summary>
        public string Almacenes { get; set; }
        /// <summary>Solo en el listado y en el detalle: Clientes.Nombre.</summary>
        public string NombreCliente { get; set; }

        public List<string> ListaAlmacenes() => (Almacenes ?? string.Empty).Split(',')
            .Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
    }

    /// <summary>Lo que se guarda de la validación (columna Motivos).</summary>
    public class MotivosAutorizacionPedido
    {
        public List<string> Motivos { get; set; } = new List<string>();
        public List<ErrorAutorizacionPedidoDTO> Errores { get; set; } = new List<ErrorAutorizacionPedidoDTO>();

        public static MotivosAutorizacionPedido De(RespuestaValidacion validacion)
        {
            var m = new MotivosAutorizacionPedido();
            if (validacion == null)
            {
                return m;
            }
            m.Motivos = (validacion.Motivos ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
            m.Errores = (validacion.Errores ?? new List<ErrorValidacion>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Motivo))
                .Select(e => new ErrorAutorizacionPedidoDTO { Producto = e.ProductoId?.Trim(), Motivo = e.Motivo.Trim() })
                .ToList();
            if (m.Motivos.Count == 0 && m.Errores.Count > 0)
            {
                m.Motivos = m.Errores.Select(e => e.Motivo).Distinct().ToList();
            }
            return m;
        }

        public string AJson() => JsonConvert.SerializeObject(this);

        /// <summary>Tolera un JSON roto o una lista suelta de textos. Nunca lanza.</summary>
        public static MotivosAutorizacionPedido DeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new MotivosAutorizacionPedido();
            }
            try
            {
                string limpio = json.Trim();
                if (limpio.StartsWith("[", StringComparison.Ordinal))
                {
                    return new MotivosAutorizacionPedido { Motivos = JsonConvert.DeserializeObject<List<string>>(limpio) ?? new List<string>() };
                }
                MotivosAutorizacionPedido m = JsonConvert.DeserializeObject<MotivosAutorizacionPedido>(limpio) ?? new MotivosAutorizacionPedido();
                m.Motivos = m.Motivos ?? new List<string>();
                m.Errores = m.Errores ?? new List<ErrorAutorizacionPedidoDTO>();
                return m;
            }
            catch (JsonException)
            {
                return new MotivosAutorizacionPedido { Motivos = new List<string> { json.Trim() } };
            }
        }
    }

    /// <summary>Novedades 1258: las reglas puras de las autorizaciones de pedidos.</summary>
    public static class ReglasAutorizacionPedido
    {
        public const int LARGO_TEXTO = 500;
        /// <summary>Una solicitud idéntica de las últimas 24 h no se repite (409 YA_SOLICITADA).</summary>
        public static readonly TimeSpan VENTANA_IDENTICA = TimeSpan.FromHours(24);
        /// <summary>El listado mira 30 días atrás.</summary>
        public const int DIAS_LISTADO = 30;

        /// <summary>Si quien pregunta es quien la pidió (mismo usuario, con o sin dominio, sin mayúsculas).</summary>
        public static bool EsDe(string solicitante, string usuario)
        {
            string a = solicitante?.Trim();
            string b = usuario?.Trim();
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ReglaOmitirValidacionPedido.SinDominio(a), ReglaOmitirValidacionPedido.SinDominio(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Por qué no se puede resolver (autorizar, denegar o anular): ya no está pendiente. Null si se puede.</summary>
        public static string MotivoNoPendiente(FilaAutorizacionPedido fila)
        {
            if (fila == null || fila.Estado == EstadosAutorizacionPedido.PENDIENTE)
            {
                return null;
            }
            string quien = string.IsNullOrWhiteSpace(fila.UsuarioResolucion) ? null : ReglaOmitirValidacionPedido.SinDominio(fila.UsuarioResolucion);
            switch (fila.Estado)
            {
                case EstadosAutorizacionPedido.AUTORIZADA:
                    return fila.PedidoCreado.HasValue
                        ? $"Esta solicitud ya está autorizada{(quien == null ? "" : " por " + quien)}: es el pedido {fila.PedidoCreado.Value}."
                        : $"Esta solicitud ya está autorizada{(quien == null ? "" : " por " + quien)}.";
                case EstadosAutorizacionPedido.DENEGADA:
                    return $"Esta solicitud ya está denegada{(quien == null ? "" : " por " + quien)}.";
                case EstadosAutorizacionPedido.ANULADA:
                    return "Esta solicitud la ha anulado quien la pidió.";
                default:
                    return $"Esta solicitud ya no está pendiente ({fila.Estado}).";
            }
        }

        /// <summary>Un texto opcional de 500 como mucho: null si vale; si no, el motivo (de usted).</summary>
        public static string MotivoTextoNoValido(string texto, string nombre)
            => texto != null && texto.Trim().Length > LARGO_TEXTO ? $"{nombre} no puede pasar de {LARGO_TEXTO} caracteres." : null;

        /// <summary>
        /// La huella de «el mismo pedido»: empresa, cliente, contacto y las líneas (producto, tipo, cantidad, precio, descuento, si aplica
        /// descuento, oferta y almacén) sin importar el orden. SHA-256 en hexadecimal (64).
        /// </summary>
        public static string Huella(PedidoVentaDTO pedido)
        {
            if (pedido == null)
            {
                return null;
            }
            IEnumerable<string> lineas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l != null)
                .Select(l => string.Join("|",
                    (l.Producto ?? string.Empty).Trim().ToUpperInvariant(),
                    (l.tipoLinea ?? 0).ToString(CultureInfo.InvariantCulture),
                    l.Cantidad.ToString(CultureInfo.InvariantCulture),
                    Math.Round(l.PrecioUnitario, 4).ToString("0.0000", CultureInfo.InvariantCulture),
                    Math.Round(l.DescuentoLinea, 4).ToString("0.0000", CultureInfo.InvariantCulture),
                    l.AplicarDescuento ? "1" : "0",
                    (l.oferta ?? 0).ToString(CultureInfo.InvariantCulture),
                    (l.almacen ?? string.Empty).Trim().ToUpperInvariant()))
                .OrderBy(s => s, StringComparer.Ordinal);
            string texto = string.Join("\n", new[]
            {
                (pedido.empresa ?? string.Empty).Trim(), (pedido.cliente ?? string.Empty).Trim(), (pedido.contacto ?? string.Empty).Trim()
            }.Concat(lineas));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(texto));
                return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>Los almacenes de las líneas (sin repetir), separados por comas.</summary>
        public static string Almacenes(PedidoVentaDTO pedido)
            => string.Join(",", (pedido?.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Select(l => l?.almacen?.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Los que pueden autorizar ESTE pedido según <see cref="ReglaOmitirValidacionPedido"/>, sin el solicitante: Dirección, Almacén, los
        /// que tienen activado el parámetro de crear con errores y, si TODAS las líneas son de un mismo almacén, los de Tiendas con ese
        /// almacén; y, si la denegación pide un permiso propio, solo los que además lo tienen. <paramref name="parametros"/> son las filas
        /// PROPIAS de ParametrosUsuario (sin «(defecto)», que daría a todo el mundo). Usuarios con dominio, sin repetir. Pura.
        /// </summary>
        public static List<string> Destinatarios(IEnumerable<string> direccion, IEnumerable<string> almacen, IEnumerable<string> tiendas,
            IEnumerable<ParametroDestinatarioAutorizacion> parametros, IEnumerable<string> almacenesLineas, string permisoNecesario, string solicitante)
        {
            List<ParametroDestinatarioAutorizacion> filas = (parametros ?? Enumerable.Empty<ParametroDestinatarioAutorizacion>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Usuario)).ToList();
            bool Activado(string usuario, string clave) => filas.Any(p => SinDominioIgual(p.Usuario, usuario)
                && string.Equals(p.Clave?.Trim(), clave, StringComparison.OrdinalIgnoreCase) && ReglaOmitirValidacionPedido.ValorActivado(p.Valor));

            var candidatos = new List<string>();
            candidatos.AddRange(direccion ?? Enumerable.Empty<string>());
            candidatos.AddRange(almacen ?? Enumerable.Empty<string>());
            candidatos.AddRange(filas.Where(p => string.Equals(p.Clave?.Trim(), Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES,
                StringComparison.OrdinalIgnoreCase) && ReglaOmitirValidacionPedido.ValorActivado(p.Valor)).Select(p => p.Usuario));
            List<string> almacenes = (almacenesLineas ?? Enumerable.Empty<string>()).Select(a => a?.Trim()).ToList();
            foreach (string tienda in tiendas ?? Enumerable.Empty<string>())
            {
                string suAlmacen = filas.FirstOrDefault(p => SinDominioIgual(p.Usuario, tienda)
                    && string.Equals(p.Clave?.Trim(), ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN, StringComparison.OrdinalIgnoreCase))?.Valor;
                if (ReglaOmitirValidacionPedido.GrupoPermite(false, false, null, true, () => suAlmacen, almacenes))
                {
                    candidatos.Add(tienda);
                }
            }
            return candidatos
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrEmpty(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Where(u => string.IsNullOrWhiteSpace(permisoNecesario) || Activado(u, permisoNecesario.Trim()))
                .Where(u => !EsDe(solicitante, u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .GroupBy(u => ReglaOmitirValidacionPedido.SinDominio(u), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        internal const string DOMINIO = "NUEVAVISION\\";

        private static bool SinDominioIgual(string a, string b)
            => string.Equals(ReglaOmitirValidacionPedido.SinDominio(a), ReglaOmitirValidacionPedido.SinDominio(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Una fila propia de ParametrosUsuario (para los destinatarios).</summary>
    public class ParametroDestinatarioAutorizacion
    {
        public string Usuario { get; set; }
        public string Clave { get; set; }
        public string Valor { get; set; }
    }

    /// <summary>Novedades 1258: los textos (de usted) de las autorizaciones de pedidos y sus notificaciones.</summary>
    public static class TextosAutorizacionPedido
    {
        /// <summary>A quien puede autorizar (NO urgente).</summary>
        public const string TIPO_PEDIDA = "AutorizacionPedidoPedida";
        /// <summary>A quien la pidió y a su vendedor.</summary>
        public const string TIPO_RESUELTA = "AutorizacionPedidoResuelta";

        public const string ERROR_SIN_TABLA = "Todavía no se pueden pedir autorizaciones de pedidos: falta la tabla PedidosAutorizacionesSolicitadas " +
            "(Scripts/PedidosAutorizacionesSolicitadas.sql). Avise a Informática.";
        public const string ERROR_NO_NECESITA = "Este pedido no necesita autorización: puede guardarlo.";
        public const string ERROR_SIN_PEDIDO = "Falta el pedido que quiere que le autoricen.";
        public const string ERROR_SIN_CLIENTE = "Falta el cliente del pedido.";
        public const string ERROR_SIN_LINEAS = "El pedido no tiene líneas.";
        public const string ERROR_NO_ENCONTRADA = "No existe esa solicitud de autorización.";
        public const string ERROR_SIN_PERMISO_VER = "Esta solicitud no es suya y usted no puede autorizar este pedido.";
        public const string ERROR_SIN_PERMISO_AUTORIZAR = "Usted no puede autorizar este pedido: solo quien puede crear un pedido que no pasa la validación " +
            "(Dirección, Almacén, Tiendas con todas las líneas de su almacén o quien tenga el permiso) y, si la denegación pide un permiso propio, quien lo tenga.";
        public const string ERROR_SIN_PERMISO_ANULAR = "Solo quien pidió la autorización puede anularla.";
        public const string ERROR_SIN_MOTIVO = "Escriba por qué deniega el pedido.";
        public const string ERROR_YA_SOLICITADA = "Ya pidió la autorización de este mismo pedido y sigue pendiente.";
        public const string ERROR_SE_ESTA_AUTORIZANDO = "Otra persona está autorizando esta solicitud ahora mismo. Vuelva a mirarla en un momento.";
        public const string ERROR_PEDIDO_ILEGIBLE = "No se puede leer el pedido guardado en la solicitud. Pida que lo vuelvan a mandar.";
        public const string CODIGO_YA_SOLICITADA = "YA_SOLICITADA";

        /// <summary>«el cliente 15191 (PELUQUERÍA LUCÍA)».</summary>
        public static string Cliente(string cliente, string contacto, string nombre)
        {
            string c = cliente?.Trim();
            if (string.IsNullOrEmpty(c))
            {
                return "un cliente";
            }
            string con = string.IsNullOrWhiteSpace(contacto) || contacto.Trim() == "0" ? c : $"{c}/{contacto.Trim()}";
            return string.IsNullOrWhiteSpace(nombre) ? $"el cliente {con}" : $"el cliente {con} ({nombre.Trim()})";
        }

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static string Importe(decimal importe) => importe.ToString("N2", castellano) + " €";

        /// <summary>«Ana pide autorización para un pedido de el cliente…» → «para un pedido del cliente 15191 (…) (123,45 €): primer motivo…».</summary>
        public static NotificacionPushDTO Pedida(FilaAutorizacionPedido f, IReadOnlyList<string> motivos)
        {
            string quien = ReglaOmitirValidacionPedido.SinDominio(f.Solicitante) ?? "Alguien";
            string primero = motivos != null && motivos.Count > 0 ? motivos[0].Trim().TrimEnd('.') : "no pasa la validación de ofertas y descuentos";
            string mas = motivos != null && motivos.Count > 1 ? "…" : ".";
            string cliente = Cliente(f.Cliente, f.Contacto, f.NombreCliente);
            string del = cliente.StartsWith("el ", StringComparison.Ordinal) ? "del " + cliente.Substring(3) : "de " + cliente;
            var n = new NotificacionPushDTO
            {
                Titulo = $"Autorización de pedido: {quien}",
                Cuerpo = $"{quien} pide autorización para un pedido {del} ({Importe(f.Importe)}): {primero}{mas}"
                    + (string.IsNullOrWhiteSpace(f.Comentario) ? string.Empty : $" Comentario: {f.Comentario.Trim()}"),
                Tipo = TIPO_PEDIDA,
                Datos = Datos(TIPO_PEDIDA, f)
            };
            return n;
        }

        /// <summary>A quien la pidió: autorizada (con el número de pedido) o denegada (con el motivo).</summary>
        public static NotificacionPushDTO Resuelta(FilaAutorizacionPedido f)
        {
            string quien = ReglaOmitirValidacionPedido.SinDominio(f.UsuarioResolucion) ?? "Alguien";
            string cliente = Cliente(f.Cliente, f.Contacto, f.NombreCliente);
            string del = cliente.StartsWith("el ", StringComparison.Ordinal) ? "del " + cliente.Substring(3) : "de " + cliente;
            string titulo;
            string cuerpo;
            if (f.Estado == EstadosAutorizacionPedido.AUTORIZADA)
            {
                titulo = f.PedidoCreado.HasValue ? $"Pedido {f.PedidoCreado.Value} autorizado" : "Pedido autorizado";
                cuerpo = $"{quien} ha autorizado su pedido {del}" + (f.PedidoCreado.HasValue ? $": es el pedido {f.PedidoCreado.Value}." : ".")
                    + (string.IsNullOrWhiteSpace(f.MotivoResolucion) ? string.Empty : $" Comentario: {f.MotivoResolucion.Trim()}");
            }
            else
            {
                titulo = "Pedido no autorizado";
                cuerpo = $"{quien} no ha autorizado su pedido {del}."
                    + (string.IsNullOrWhiteSpace(f.MotivoResolucion) ? string.Empty : $" Motivo: {f.MotivoResolucion.Trim().TrimEnd('.')}.");
            }
            Dictionary<string, string> datos = Datos(TIPO_RESUELTA, f);
            datos["estado"] = f.Estado;
            if (f.PedidoCreado.HasValue)
            {
                datos["pedido"] = f.PedidoCreado.Value.ToString(CultureInfo.InvariantCulture);
            }
            return new NotificacionPushDTO { Titulo = titulo, Cuerpo = cuerpo, Tipo = TIPO_RESUELTA, Datos = datos };
        }

        /// <summary>«notificacionResuelta» a los demás que podían autorizarla (Nesto la quita de la campana).</summary>
        public static NotificacionResueltaDTO NotificacionResuelta(FilaAutorizacionPedido f) => new NotificacionResueltaDTO
        {
            Tipo = TIPO_PEDIDA,
            Referencia = f.Id.ToString(CultureInfo.InvariantCulture),
            Respondio = ReglaOmitirValidacionPedido.SinDominio(f.UsuarioResolucion),
            Resultado = f.Estado
        };

        private static Dictionary<string, string> Datos(string tipo, FilaAutorizacionPedido f)
        {
            var datos = new Dictionary<string, string>
            {
                [NotificacionesUrgentes.DATO_TIPO] = tipo,
                ["id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                [NotificacionesUrgentes.DATO_REFERENCIA] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["empresa"] = string.IsNullOrWhiteSpace(f.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : f.Empresa.Trim()
            };
            if (!string.IsNullOrWhiteSpace(f.Cliente))
            {
                datos["cliente"] = f.Cliente.Trim();
            }
            if (!string.IsNullOrWhiteSpace(f.Contacto))
            {
                datos["contacto"] = f.Contacto.Trim();
            }
            return datos;
        }
    }
}
