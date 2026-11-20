using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// 20/11/26 (Carlos): una línea del aviso de salida de un proveedor («[F806.N] UTECH HIGH FREQUENCY … 2,00»): su referencia, la
    /// descripción y las unidades que van en ese envío. La da el formato (<see cref="IFormatoAvisoSalida.Lineas"/>).
    /// </summary>
    public class LineaAvisoSalida
    {
        public string ReferenciaProveedor { get; set; }
        public string Descripcion { get; set; }
        public decimal Cantidad { get; set; }
    }

    /// <summary>Una fila de ProveedoresProducto: la referencia del proveedor de un producto nuestro.</summary>
    public class ReferenciaProductoProveedor
    {
        public string Proveedor { get; set; }
        public string ReferenciaProveedor { get; set; }
        public string Producto { get; set; }
    }

    /// <summary>Una línea en el aire (LinPedidoCmp -1/1 con Enviado) de un pedido de compra, para casarla con un aviso.</summary>
    public class LineaEnAireAviso
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public string Proveedor { get; set; }
    }

    /// <summary>Lo que lee el repositorio para casar avisos: líneas en el aire y referencias de los proveedores (una consulta).</summary>
    public class DatosLineasAviso
    {
        public List<LineaEnAireAviso> Lineas { get; set; } = new List<LineaEnAireAviso>();
        public List<ReferenciaProductoProveedor> Referencias { get; set; } = new List<ReferenciaProductoProveedor>();
    }

    /// <summary>Una línea nuestra que casa con el aviso: las unidades que trae el envío y si es parcial (menos que lo pendiente).</summary>
    public class LineaCasadaAviso
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string ReferenciaProveedor { get; set; }
        /// <summary>Lo pendiente de la línea.</summary>
        public int Cantidad { get; set; }
        /// <summary>Las unidades del aviso que le tocan.</summary>
        public decimal Unidades { get; set; }
        public bool Parcial { get; set; }
    }

    /// <summary>El resultado de <see cref="ReglaLineasAvisoSalida.Casar"/>.</summary>
    public class ResultadoLineasAviso
    {
        /// <summary>El pedido, si las líneas del aviso casan con UNO solo; null si con ninguno o con varios.</summary>
        public CandidatoPedidoCompra Pedido { get; set; }
        /// <summary>Los pedidos con alguna línea que casa (uno, varios o ninguno).</summary>
        public List<CandidatoPedidoCompra> PedidosQueCasan { get; set; } = new List<CandidatoPedidoCompra>();
        /// <summary>Las líneas nuestras que casan (de todos los pedidos que casan).</summary>
        public List<LineaCasadaAviso> Casadas { get; set; } = new List<LineaCasadaAviso>();
        public int LineasAviso { get; set; }
        /// <summary>Líneas del aviso cuya referencia no se sabe traducir a un producto nuestro (no impiden casar las demás).</summary>
        public int SinTraducir { get; set; }

        public bool Decide => Pedido != null;
    }

    /// <summary>
    /// LA REGLA (20/11/26, Carlos: «como la fecha va línea a línea, podemos saber en qué líneas hay que poner la fecha»): casa las líneas de un
    /// aviso de salida con las líneas en el aire de los pedidos candidatos. La referencia del aviso se traduce a producto nuestro con
    /// ProveedoresProducto del proveedor de CADA pedido (una referencia puede ser de varios productos: vale cualquiera). Por pedido y producto,
    /// las unidades del aviso se reparten entre sus líneas por nº de orden (cada una, como mucho lo pendiente); una línea que recibe menos que lo
    /// pendiente es un envío PARCIAL: se le pone igual la fecha (es cuando empieza a llegar; no se parte la línea) y queda dicho en el rastro;
    /// las líneas que no reciben nada no son de este envío. Si casa un solo pedido, ese; con varios, el ÚNICO que casa el aviso entero (todas sus
    /// líneas y unidades), y si no lo hay no se elige (se deja por asignar con ellos);
    /// sin ninguno, la regla no decide nada (todo como antes). PURA.
    /// </summary>
    public static class ReglaLineasAvisoSalida
    {
        public static ResultadoLineasAviso Casar(IEnumerable<LineaAvisoSalida> aviso, IEnumerable<ReferenciaProductoProveedor> referencias,
            IEnumerable<LineaEnAireAviso> lineas)
        {
            List<LineaAvisoSalida> delAviso = (aviso ?? Enumerable.Empty<LineaAvisoSalida>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.ReferenciaProveedor) && l.Cantidad > 0).ToList();
            var resultado = new ResultadoLineasAviso { LineasAviso = delAviso.Count };
            if (!delAviso.Any())
            {
                return resultado;
            }
            // Proveedor → referencia normalizada → productos
            var mapa = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (ReferenciaProductoProveedor r in (referencias ?? Enumerable.Empty<ReferenciaProductoProveedor>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Producto) && !string.IsNullOrWhiteSpace(r.ReferenciaProveedor)))
            {
                string proveedor = r.Proveedor?.Trim() ?? string.Empty;
                if (!mapa.TryGetValue(proveedor, out Dictionary<string, HashSet<string>> delProveedor))
                {
                    mapa[proveedor] = delProveedor = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                }
                string referencia = Normalizar(r.ReferenciaProveedor);
                if (!delProveedor.TryGetValue(referencia, out HashSet<string> productos))
                {
                    delProveedor[referencia] = productos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                _ = productos.Add(r.Producto.Trim());
            }

            List<LineaEnAireAviso> todas = (lineas ?? Enumerable.Empty<LineaEnAireAviso>()).Where(l => l != null && l.Pedido > 0).ToList();
            var traducidas = new HashSet<LineaAvisoSalida>();
            var enteros = new List<CandidatoPedidoCompra>();
            foreach (IGrouping<string, LineaEnAireAviso> pedido in todas.GroupBy(l => AsignadorPedidosCompra.Clave(l.Empresa, l.Pedido)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                string proveedor = pedido.Select(l => l.Proveedor?.Trim()).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? string.Empty;
                mapa.TryGetValue(proveedor, out Dictionary<string, HashSet<string>> delProveedor);
                // Unidades del aviso por producto de este pedido (dos líneas del aviso pueden ser del mismo producto)
                var unidades = new Dictionary<string, (decimal Unidades, string Referencia)>(StringComparer.OrdinalIgnoreCase);
                bool entero = true; // todas las líneas del aviso tienen su producto en este pedido y caben en lo pendiente
                foreach (LineaAvisoSalida linea in delAviso)
                {
                    if (delProveedor == null || !delProveedor.TryGetValue(Normalizar(linea.ReferenciaProveedor), out HashSet<string> productos))
                    {
                        entero = false;
                        continue;
                    }
                    _ = traducidas.Add(linea);
                    // De los productos de esa referencia, el que tenga líneas en el pedido (el primero por orden si varios)
                    string producto = pedido.Where(l => productos.Contains(l.Producto?.Trim() ?? string.Empty)).OrderBy(l => l.NumeroOrden)
                        .Select(l => l.Producto.Trim()).FirstOrDefault();
                    if (producto == null)
                    {
                        entero = false;
                        continue;
                    }
                    unidades[producto] = unidades.TryGetValue(producto, out var ya)
                        ? (ya.Unidades + linea.Cantidad, ya.Referencia)
                        : (linea.Cantidad, linea.ReferenciaProveedor.Trim());
                }
                var casadasPedido = new List<LineaCasadaAviso>();
                foreach (KeyValuePair<string, (decimal Unidades, string Referencia)> u in unidades)
                {
                    decimal quedan = u.Value.Unidades;
                    foreach (LineaEnAireAviso nuestra in pedido.Where(l => string.Equals(l.Producto?.Trim(), u.Key, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.NumeroOrden))
                    {
                        if (quedan <= 0)
                        {
                            break;
                        }
                        decimal le = nuestra.Cantidad > 0 ? Math.Min(quedan, nuestra.Cantidad) : quedan;
                        quedan -= le;
                        casadasPedido.Add(new LineaCasadaAviso
                        {
                            Empresa = nuestra.Empresa?.Trim(),
                            Pedido = nuestra.Pedido,
                            NumeroOrden = nuestra.NumeroOrden,
                            Producto = nuestra.Producto?.Trim(),
                            ReferenciaProveedor = u.Value.Referencia,
                            Cantidad = nuestra.Cantidad,
                            Unidades = le,
                            Parcial = nuestra.Cantidad > 0 && le < nuestra.Cantidad
                        });
                    }
                    if (quedan > 0)
                    {
                        entero = false; // el aviso trae más unidades de las que este pedido espera
                    }
                }
                if (casadasPedido.Any())
                {
                    LineaEnAireAviso primera = pedido.First();
                    resultado.PedidosQueCasan.Add(new CandidatoPedidoCompra
                    {
                        Empresa = primera.Empresa?.Trim(), Pedido = primera.Pedido, Proveedor = proveedor, Motivo = AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO
                    });
                    resultado.Casadas.AddRange(casadasPedido.OrderBy(c => c.NumeroOrden));
                    if (entero)
                    {
                        enteros.Add(resultado.PedidosQueCasan.Last());
                    }
                }
            }
            resultado.SinTraducir = delAviso.Count(l => !traducidas.Contains(l));
            // Carlos 20/11/26 («dentro de una seguridad razonable, cuanto más automático mejor»): con varios pedidos que casan, gana el ÚNICO
            // que casa el aviso ENTERO (todas sus líneas, con todas sus unidades); si son varios los enteros, o ninguno, no se elige.
            resultado.Pedido = resultado.PedidosQueCasan.Count == 1 ? resultado.PedidosQueCasan[0]
                : enteros.Count == 1 ? enteros[0] : null;
            return resultado;
        }

        /// <summary>Las líneas casadas de ESE pedido (vacía si ninguna: entonces la fecha va a todo el pedido, como antes).</summary>
        public static List<LineaCasadaAviso> DelPedido(ResultadoLineasAviso resultado, string empresa, int pedido) =>
            (resultado?.Casadas ?? new List<LineaCasadaAviso>())
                .Where(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido) == AsignadorPedidosCompra.Clave(empresa, pedido)).ToList();

        /// <summary>
        /// La asignación con lo que dicen las líneas: si casan con algún pedido, esos pedidos (motivo <see cref="AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO"/>);
        /// mandan sobre todo menos sobre nuestro número en el texto y el PDF. Si la regla decide un pedido, solo ese. Sin ninguna, la asignación tal cual.
        /// </summary>
        public static AsignacionPedidoCompra Completar(AsignacionPedidoCompra asignacion, ResultadoLineasAviso resultado)
        {
            if (resultado == null || !resultado.PedidosQueCasan.Any() || MandanSobreLasLineas(asignacion))
            {
                return asignacion;
            }
            List<string> proveedores = resultado.PedidosQueCasan.Select(c => c.Proveedor?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new AsignacionPedidoCompra
            {
                ProveedorPropuesto = proveedores.Count == 1 ? proveedores[0] : null,
                // Con pedido decidido (el único que casa, o el único que casa el aviso entero), solo ese: queda inequívoco
                Candidatos = resultado.Pedido != null
                    ? new List<CandidatoPedidoCompra> { resultado.Pedido }
                    : resultado.PedidosQueCasan.Take(AsignadorPedidosCompra.MAXIMO_CANDIDATOS).ToList()
            };
        }

        /// <summary>Nuestro número de pedido en el texto o en el PDF mandan sobre las líneas.</summary>
        public static bool MandanSobreLasLineas(AsignacionPedidoCompra asignacion) =>
            (asignacion?.Candidatos ?? new List<CandidatoPedidoCompra>()).Any(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NUMERO
                || c.Motivo == AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO);

        /// <summary>Los proveedores cuyos pedidos en el aire hay que mirar: los de los candidatos de la asignación.</summary>
        public static List<string> Proveedores(AsignacionPedidoCompra asignacion) =>
            (asignacion?.Candidatos ?? new List<CandidatoPedidoCompra>()).Select(c => c.Proveedor?.Trim())
                .Concat(new[] { asignacion?.ProveedorPropuesto?.Trim() })
                .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Sin espacios ni mayúsculas.</summary>
        internal static string Normalizar(string referencia) => (referencia ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Lo que se guarda del aviso en ComprasCorreosSeguimiento.LineasAviso (JSON, Scripts/Issue857h): sus líneas y las que casaron al analizarlo.
    /// En LineasAplicadas, las <see cref="LineaCasadaAviso"/> a las que se puso la fecha (null = todo el pedido).
    /// </summary>
    public class LineasAvisoCorreo
    {
        public List<LineaAvisoSalida> Lineas { get; set; } = new List<LineaAvisoSalida>();
        public List<LineaCasadaAviso> Casadas { get; set; } = new List<LineaCasadaAviso>();
        public int SinTraducir { get; set; }

        public static string Serializar(List<LineaAvisoSalida> lineas, ResultadoLineasAviso resultado) =>
            lineas == null || !lineas.Any() ? null : JsonConvert.SerializeObject(new LineasAvisoCorreo
            {
                Lineas = lineas,
                Casadas = resultado?.Casadas ?? new List<LineaCasadaAviso>(),
                SinTraducir = resultado?.SinTraducir ?? 0
            });

        public static LineasAvisoCorreo Leer(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<LineasAvisoCorreo>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static string SerializarAplicadas(List<LineaCasadaAviso> aplicadas) =>
            aplicadas == null || !aplicadas.Any() ? null : JsonConvert.SerializeObject(aplicadas);

        public static List<LineaCasadaAviso> LeerAplicadas(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<List<LineaCasadaAviso>>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Las líneas del aviso de un ENVÍO (correos que comparten código, <see cref="MismoEnvio"/>): las del último correo que las traiga
        /// (los de la agencia no las traen: heredan las del aviso del proveedor). Vacía si ninguno.
        /// </summary>
        public static List<LineaAvisoSalida> DelEnvio(IEnumerable<CorreoSeguimientoFila> correos) =>
            (correos ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null)
                .OrderByDescending(f => f.Recibido).ThenByDescending(f => f.Id)
                .Select(f => Leer(f.LineasAviso)?.Lineas)
                .FirstOrDefault(l => l != null && l.Any()) ?? new List<LineaAvisoSalida>();
    }

    /// <summary>Los textos de las líneas casadas (de usted). PUROS.</summary>
    public static class TextosLineasAviso
    {
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>«32831 (2 uds.) y 40695 (1 ud.)».</summary>
        public static string Lista(IEnumerable<LineaCasadaAviso> casadas)
        {
            List<string> partes = (casadas ?? Enumerable.Empty<LineaCasadaAviso>()).Where(c => c != null)
                .Select(c => $"{c.Producto?.Trim()} ({Unidades(c.Unidades)})").ToList();
            if (!partes.Any())
            {
                return string.Empty;
            }
            return partes.Count == 1 ? partes[0] : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes.Last();
        }

        /// <summary>«Envío parcial: de 45182 vienen 4 de 6 uds.» o null si no hay ninguna parcial.</summary>
        public static string Parcial(IEnumerable<LineaCasadaAviso> casadas)
        {
            List<string> partes = (casadas ?? Enumerable.Empty<LineaCasadaAviso>()).Where(c => c != null && c.Parcial)
                .Select(c => $"de {c.Producto?.Trim()} vienen {Numero(c.Unidades)} de {Unidades(c.Cantidad)}").ToList();
            if (!partes.Any())
            {
                return null;
            }
            string texto = "Envío parcial: " + string.Join("; ", partes);
            return texto.EndsWith(".", StringComparison.Ordinal) ? texto : texto + ".";
        }

        public static string Unidades(decimal n) => Numero(n) + (n == 1 ? " ud." : " uds.");

        private static string Numero(decimal n) => n.ToString("0.###", es);
    }
}
