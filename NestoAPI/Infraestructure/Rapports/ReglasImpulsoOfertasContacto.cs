using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#866: una fila de oferta vigente hoy, sea de DescuentosProducto (descuento o precio especial) o de OfertasPermitidas
    /// (N+M). Producto, o familia / grupo / subgrupo / prefijo del nombre (los que vengan, en AND).
    /// </summary>
    public class FilaOfertaDia
    {
        public string Producto { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string FiltroProducto { get; set; }
        /// <summary>Descuento de 0 a 1 (DescuentosProducto.Descuento).</summary>
        public decimal Descuento { get; set; }
        /// <summary>Precio especial (DescuentosProducto.Precio): manda sobre el descuento si el producto tiene PVP.</summary>
        public decimal? Precio { get; set; }
        /// <summary>N+M de OfertasPermitidas (CantidadConPrecio + CantidadRegalo); 0 si no es un N+M.</summary>
        public int CantidadConPrecio { get; set; }
        public int CantidadRegalo { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
    }

    /// <summary>NestoAPI#866: lo que hace falta de un producto para casar las ofertas de familia o grupo y escribir el motivo.</summary>
    public class ProductoOfertable
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public decimal? PVP { get; set; }
    }

    /// <summary>NestoAPI#866: la mejor oferta de hoy de un producto.</summary>
    public class OfertaProductoDia
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>De 0 a 1 (en un N+M, el equivalente: M / (N + M)).</summary>
        public decimal Descuento { get; set; }
        /// <summary>«un 20 % de descuento» o «el 6+1».</summary>
        public string Texto { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
    }

    /// <summary>NestoAPI#866: una compra (día de pedido) de un producto en oferta hoy por un cliente de la cartera.</summary>
    public class CompraProductoCliente
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Producto { get; set; }
        public DateTime Dia { get; set; }
        public decimal Importe { get; set; }

        public string Clave => ClienteCarteraContacto.ClaveDe(Cliente, Contacto);
    }

    /// <summary>NestoAPI#866: un producto que el cliente compra habitualmente y que hoy está en oferta.</summary>
    public class ProductoEnOfertaCliente
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Compras { get; set; }
        /// <summary>Mediana de los días entre sus compras.</summary>
        public int CicloDias { get; set; }
        public int DiasDesdeUltima { get; set; }
        /// <summary>Ya le toca: lleva al menos su ciclo sin comprarlo.</summary>
        public bool LeToca { get; set; }
        public decimal Descuento { get; set; }
        public string TextoOferta { get; set; }
        /// <summary>Lo que pesa en su compra de 12 meses (0 a 1).</summary>
        public decimal Peso { get; set; }
        /// <summary>La oferta entró hace <see cref="ReglasImpulsoOfertasContacto.DIAS_NOVEDAD"/> días o menos.</summary>
        public bool RecienEntrada { get; set; }
        /// <summary>La oferta caduca en <see cref="ReglasImpulsoOfertasContacto.DIAS_CADUCA_PRONTO"/> días o menos.</summary>
        public bool CaducaPronto { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public double Puntuacion { get; set; }
    }

    /// <summary>NestoAPI#866: un cliente de la cartera con sus productos habituales en oferta hoy (el mejor, el primero).</summary>
    public class ClienteConOfertas
    {
        public ClienteCarteraContacto Cliente { get; set; }
        public List<ProductoEnOfertaCliente> Productos { get; set; } = new List<ProductoEnOfertaCliente>();
        /// <summary>Ya se habló con él desde que entró la oferta (en las 2 últimas semanas), no contestó hace menos de 2 días o pidió
        /// hace menos de 6: no se le sube (sus productos se apuntan igual).</summary>
        public bool Excluido { get; set; }

        public ProductoEnOfertaCliente Mejor => Productos.FirstOrDefault();
        public double Puntuacion => Mejor?.Puntuacion ?? 0;
        /// <summary>Sube en la lista: no excluido y su mejor producto ya le toca.</summary>
        public bool PuedeSubir => !Excluido && Mejor != null && Mejor.LeToca;
    }

    /// <summary>
    /// NestoAPI#866 (corte 1): impulso por las ofertas vigentes del día (Carlos, 16/11/26: siempre hay ofertas —bimestrales que
    /// caducan y entran, de marca, campañas como la feria online— y todas están en Nesto, así que va SIEMPRE encendido). Es una regla
    /// DESPUÉS del modelo, como <see cref="ImpulsoChequesRegaloContacto"/>: el modelo no se toca. PURA.
    /// <list type="bullet">
    /// <item>Oferta = fila global de DescuentosProducto (sin cliente ni proveedor, desde 1 unidad) que «parece campaña» (con fechas,
    /// audiencia o nombre de campaña, el mismo criterio que la pantalla de campañas) o N+M de OfertasPermitidas con fechas, vigente
    /// hoy con la regla única <c>Vigencia</c>. De cada producto, la de más descuento.</item>
    /// <item>Por cliente de la cartera: los productos en oferta que compra habitualmente (≥ <see cref="COMPRAS_MINIMAS"/> días con
    /// compra en 12 meses), su ciclo (mediana entre compras), los días desde la última, el descuento, su peso en la compra y si la
    /// oferta acaba de entrar o caduca pronto.</item>
    /// <item><see cref="Puntuacion"/>: pesa más el producto «que ya le toca», luego el descuento y el peso.</item>
    /// <item>Suben como mucho <see cref="TOPE_SUBIDOS"/> al día, detrás de los del cheque regalo, solo si su mejor producto ya le
    /// toca y no está <see cref="ClienteConOfertas.Excluido"/>.</item>
    /// </list>
    /// </summary>
    public static class ReglasImpulsoOfertasContacto
    {
        public const string CLAVE_INTERRUPTOR = "Rapports.ImpulsoOfertas";
        public const int COMPRAS_MINIMAS = 2;
        public const int DIAS_NOVEDAD = 7;
        public const int DIAS_CADUCA_PRONTO = 7;
        /// <summary>Suben como mucho tantos al día (la lista son ~20).</summary>
        public const int TOPE_SUBIDOS = 5;
        /// <summary>Hablado en estos días (y después de entrar la oferta): no se le sube.</summary>
        public const int DIAS_CONTACTO_RECIENTE = 14;
        /// <summary>Productos que se apuntan por sugerencia (corte 3).</summary>
        public const int PRODUCTOS_APUNTADOS = 3;
        public const int LONGITUD_NOMBRE_CORTO = 25;

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>Encendido salvo «0», «false», «no», «off» o «apagado» (como Salidas.NoEstaRegulariza); sin fila, encendido.</summary>
        public static bool Interpretar(string valor)
        {
            string limpio = valor?.Trim().ToLowerInvariant();
            return !(limpio == "0" || limpio == "false" || limpio == "no" || limpio == "off" || limpio == "apagado");
        }

        // ---------------- Ofertas de hoy por producto ----------------

        /// <summary>¿La fila de oferta vale para este producto? Producto exacto, o familia/grupo/subgrupo/prefijo en AND (alguno tiene que venir).</summary>
        public static bool Casa(FilaOfertaDia fila, ProductoOfertable producto)
        {
            if (fila == null || producto == null)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(fila.Producto))
            {
                return Igual(fila.Producto, producto.Producto);
            }
            if (string.IsNullOrWhiteSpace(fila.Familia) && string.IsNullOrWhiteSpace(fila.Grupo))
            {
                return false;
            }
            return (string.IsNullOrWhiteSpace(fila.Familia) || Igual(fila.Familia, producto.Familia))
                && (string.IsNullOrWhiteSpace(fila.Grupo) || Igual(fila.Grupo, producto.Grupo))
                && (string.IsNullOrWhiteSpace(fila.SubGrupo) || Igual(fila.SubGrupo, producto.SubGrupo))
                && (string.IsNullOrEmpty(fila.FiltroProducto)
                    || (producto.Nombre != null && producto.Nombre.TrimStart().StartsWith(fila.FiltroProducto.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Descuento de 0 a 1 que da la fila a ese producto: N+M → M / (N + M); precio especial → 1 − precio / PVP; si no, el descuento.</summary>
        public static decimal DescuentoDe(FilaOfertaDia fila, ProductoOfertable producto)
        {
            decimal descuento;
            if (fila.CantidadRegalo > 0 && fila.CantidadConPrecio > 0)
            {
                descuento = (decimal)fila.CantidadRegalo / (fila.CantidadConPrecio + fila.CantidadRegalo);
            }
            else if (fila.Precio.HasValue && producto?.PVP > 0)
            {
                descuento = 1 - fila.Precio.Value / producto.PVP.Value;
            }
            else
            {
                descuento = fila.Descuento;
            }
            return Math.Max(0, Math.Min(1, descuento));
        }

        public static string TextoOferta(FilaOfertaDia fila, decimal descuento)
            => fila.CantidadRegalo > 0 && fila.CantidadConPrecio > 0
                ? $"el {fila.CantidadConPrecio}+{fila.CantidadRegalo}"
                : $"un {Porcentaje(descuento)} % de descuento";

        /// <summary>La mejor oferta de hoy (más descuento; a igualdad, la que antes caduca) de cada producto que tenga alguna.</summary>
        public static Dictionary<string, OfertaProductoDia> OfertasPorProducto(IEnumerable<FilaOfertaDia> filas, IEnumerable<ProductoOfertable> productos)
        {
            var resultado = new Dictionary<string, OfertaProductoDia>(StringComparer.OrdinalIgnoreCase);
            List<FilaOfertaDia> lista = (filas ?? Enumerable.Empty<FilaOfertaDia>()).Where(f => f != null).ToList();
            foreach (ProductoOfertable producto in productos ?? Enumerable.Empty<ProductoOfertable>())
            {
                if (producto == null || string.IsNullOrWhiteSpace(producto.Producto))
                {
                    continue;
                }
                var mejor = lista
                    .Where(f => Casa(f, producto))
                    .Select(f => new { Fila = f, Descuento = DescuentoDe(f, producto) })
                    .Where(x => x.Descuento > 0)
                    .OrderByDescending(x => x.Descuento)
                    .ThenBy(x => x.Fila.FechaHasta ?? DateTime.MaxValue)
                    .FirstOrDefault();
                if (mejor == null)
                {
                    continue;
                }
                string clave = producto.Producto.Trim();
                resultado[clave] = new OfertaProductoDia
                {
                    Producto = clave,
                    Nombre = producto.Nombre?.Trim(),
                    Descuento = mejor.Descuento,
                    Texto = TextoOferta(mejor.Fila, mejor.Descuento),
                    FechaDesde = mejor.Fila.FechaDesde?.Date,
                    FechaHasta = mejor.Fila.FechaHasta?.Date
                };
            }
            return resultado;
        }

        // ---------------- Clientes ----------------

        /// <summary>Mediana de los días entre compras consecutivas (días distintos). 0 si hay menos de 2.</summary>
        public static int Ciclo(IEnumerable<DateTime> dias)
        {
            List<DateTime> ordenados = (dias ?? Enumerable.Empty<DateTime>()).Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
            if (ordenados.Count < 2)
            {
                return 0;
            }
            List<int> huecos = ordenados.Zip(ordenados.Skip(1), (a, b) => (b - a).Days).OrderBy(h => h).ToList();
            int medio = huecos.Count / 2;
            double mediana = huecos.Count % 2 == 1 ? huecos[medio] : (huecos[medio - 1] + huecos[medio]) / 2.0;
            return Math.Max(1, (int)Math.Round(mediana, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Pesa más el producto que ya le toca (de 1 a 1,5 según lo pasado que vaya; si aún no le toca, de 0 a 0,5 según lo cerca que
        /// esté), luego el descuento y el peso en su compra; la que caduca pronto un 20 % más y la recién entrada un 10 %.
        /// </summary>
        public static double Puntuacion(int cicloDias, int diasDesdeUltima, decimal descuento, decimal peso, bool recienEntrada, bool caducaPronto)
        {
            double ratio = cicloDias > 0 ? (double)diasDesdeUltima / cicloDias : 0;
            double toca = ratio >= 1 ? 1 + 0.5 * Math.Min(ratio - 1, 1) : 0.5 * Math.Max(0, ratio);
            double urgencia = caducaPronto ? 1.2 : recienEntrada ? 1.1 : 1.0;
            return Math.Round(toca * (0.5 + (double)descuento) * (0.5 + (double)peso) * urgencia, 6);
        }

        /// <summary>No se le sube: hablado desde que entró la oferta (y en las 2 últimas semanas), no contestó hace menos de 2 días o pidió hace menos de 6.</summary>
        public static bool Excluido(ClienteCarteraContacto cliente, ProductoEnOfertaCliente mejor, DateTime hoy)
        {
            DateTime desde = new[] { mejor?.FechaDesde?.Date ?? DateTime.MinValue, hoy.Date.AddDays(-DIAS_CONTACTO_RECIENTE) }.Max();
            int? diasIntento = MotorSugerenciasContacto.DiasDesde(cliente.UltimoIntento, hoy);
            int? diasPedido = MotorSugerenciasContacto.DiasDesde(RepositorioCarteraContactoSql.MasReciente(cliente.UltimoPedido, cliente.UltimoPedidoCliente), hoy);
            return (cliente.UltimoContacto.HasValue && cliente.UltimoContacto.Value.Date >= desde)
                || (diasIntento.HasValue && diasIntento.Value < UmbralesSugerenciasContacto.DIAS_TRAS_INTENTO)
                || (diasPedido.HasValue && diasPedido.Value < UmbralesSugerenciasContacto.DIAS_TRAS_PEDIDO);
        }

        /// <summary>Clave de la cartera → el cliente con sus productos habituales en oferta hoy (solo los que tienen alguno).</summary>
        public static Dictionary<string, ClienteConOfertas> Evaluar(IEnumerable<ClienteCarteraContacto> cartera, IEnumerable<CompraProductoCliente> compras,
            IDictionary<string, OfertaProductoDia> ofertas, DateTime hoy)
        {
            var resultado = new Dictionary<string, ClienteConOfertas>(StringComparer.OrdinalIgnoreCase);
            if (ofertas == null || ofertas.Count == 0)
            {
                return resultado;
            }
            Dictionary<string, ClienteCarteraContacto> porClave = (cartera ?? Enumerable.Empty<ClienteCarteraContacto>())
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            DateTime dia = hoy.Date;
            foreach (var delCliente in (compras ?? Enumerable.Empty<CompraProductoCliente>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto) && c.Dia.Date < dia.AddDays(1))
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase))
            {
                if (!porClave.TryGetValue(delCliente.Key, out ClienteCarteraContacto cliente))
                {
                    continue;
                }
                var productos = new List<ProductoEnOfertaCliente>();
                foreach (var delProducto in delCliente.GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    if (!ofertas.TryGetValue(delProducto.Key, out OfertaProductoDia oferta))
                    {
                        continue;
                    }
                    List<DateTime> dias = delProducto.Select(c => c.Dia.Date).Distinct().ToList();
                    if (dias.Count < COMPRAS_MINIMAS)
                    {
                        continue;
                    }
                    int ciclo = Ciclo(dias);
                    int desdeUltima = (dia - dias.Max()).Days;
                    decimal peso = cliente.Importe12Meses > 0 ? Math.Max(0, Math.Min(1, delProducto.Sum(c => c.Importe) / cliente.Importe12Meses)) : 0;
                    bool recien = oferta.FechaDesde.HasValue && (dia - oferta.FechaDesde.Value.Date).Days <= DIAS_NOVEDAD;
                    bool caduca = oferta.FechaHasta.HasValue && (oferta.FechaHasta.Value.Date - dia).Days <= DIAS_CADUCA_PRONTO;
                    productos.Add(new ProductoEnOfertaCliente
                    {
                        Producto = delProducto.Key,
                        Nombre = oferta.Nombre,
                        Compras = dias.Count,
                        CicloDias = ciclo,
                        DiasDesdeUltima = desdeUltima,
                        LeToca = desdeUltima >= ciclo,
                        Descuento = oferta.Descuento,
                        TextoOferta = oferta.Texto,
                        Peso = peso,
                        RecienEntrada = recien,
                        CaducaPronto = caduca,
                        FechaDesde = oferta.FechaDesde,
                        FechaHasta = oferta.FechaHasta,
                        Puntuacion = Puntuacion(ciclo, desdeUltima, oferta.Descuento, peso, recien, caduca)
                    });
                }
                if (!productos.Any())
                {
                    continue;
                }
                List<ProductoEnOfertaCliente> ordenados = productos
                    .OrderByDescending(p => p.Puntuacion)
                    .ThenBy(p => p.Producto, StringComparer.Ordinal)
                    .ToList();
                resultado[cliente.Clave] = new ClienteConOfertas
                {
                    Cliente = cliente,
                    Productos = ordenados,
                    Excluido = Excluido(cliente, ordenados[0], dia)
                };
            }
            return resultado;
        }

        // ---------------- La lista ----------------

        /// <summary>
        /// La lista (ya con los del cheque delante) con los mejores de las ofertas (<see cref="ClienteConOfertas.PuedeSubir"/>, por
        /// puntuación, como mucho <paramref name="tope"/>) detrás de los del cheque y delante del resto, con Origen «Ofertas». A los que
        /// suben por el cheque no se les toca. Orden = 1, 2, 3…
        /// </summary>
        public static List<SugerenciaContactoDTO> Aplicar(IList<SugerenciaContactoDTO> lista, IDictionary<string, ClienteConOfertas> conOfertas,
            DateTime hoy, int tope = TOPE_SUBIDOS)
        {
            List<SugerenciaContactoDTO> todas = (lista ?? new List<SugerenciaContactoDTO>()).ToList();
            if (conOfertas == null || conOfertas.Count == 0 || tope <= 0)
            {
                return todas;
            }
            List<SugerenciaContactoDTO> cheques = todas.Where(s => s.Origen == OrigenesSugerenciaContacto.CHEQUE_REGALO).ToList();
            var clavesCheque = new HashSet<string>(cheques.Select(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto)), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaContactoDTO> subidos = conOfertas.Values
                .Where(c => c.PuedeSubir && !clavesCheque.Contains(c.Cliente.Clave))
                .OrderByDescending(c => c.Puntuacion)
                .ThenBy(c => c.Cliente.Clave, StringComparer.Ordinal)
                .Take(tope)
                .Select(c => Sugerencia(c, hoy))
                .ToList();
            if (!subidos.Any())
            {
                return todas;
            }
            var clavesSubidos = new HashSet<string>(subidos.Select(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto)), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaContactoDTO> resultado = cheques
                .Concat(subidos)
                .Concat(todas.Where(s => s.Origen != OrigenesSugerenciaContacto.CHEQUE_REGALO && !clavesSubidos.Contains(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto))))
                .ToList();
            for (int i = 0; i < resultado.Count; i++)
            {
                resultado[i].Orden = i + 1;
            }
            return resultado;
        }

        /// <summary>La sugerencia de un cliente que sube por las ofertas: su prioridad de siempre (Alta si hoy no le tocaba), el motivo de la oferta.</summary>
        public static SugerenciaContactoDTO Sugerencia(ClienteConOfertas con, DateTime hoy)
        {
            SugerenciaContactoDTO dto = MotorSugerenciasContacto.ADTO(con.Cliente, hoy);
            dto.Prioridad = MotorSugerenciasContacto.Prioridad(con.Cliente, hoy) ?? PrioridadesContacto.ALTA;
            dto.Motivo = Motivo(con, hoy);
            dto.Origen = OrigenesSugerenciaContacto.OFERTAS;
            dto.ProductosOferta = ProductosJson(con);
            return dto;
        }

        /// <summary>
        /// «Compra el 45929 (CREMA HIDRATANTE) cada 45 días y lleva 64 sin comprarlo; hoy tiene un 20 % de descuento (hasta el 30/11).
        /// No hablamos con él desde el 02/10.» Sin pasar de <see cref="MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO"/>: primero se
        /// acorta el nombre, luego se quita, luego la coletilla de los demás productos.
        /// </summary>
        public static string Motivo(ClienteConOfertas con, DateTime hoy)
        {
            ProductoEnOfertaCliente p = con?.Mejor;
            if (p == null)
            {
                return string.Empty;
            }
            int otros = con.Productos.Count(x => x.LeToca && x != p);
            string coletilla = otros == 0 ? string.Empty
                : otros == 1 ? " Tiene otro producto suyo en oferta." : $" Tiene {otros} productos suyos más en oferta.";
            int maximo = MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO;
            foreach (int longitudNombre in new[] { LONGITUD_NOMBRE_CORTO, 12, 0 })
            {
                foreach (string extra in new[] { coletilla, string.Empty })
                {
                    string texto = Frase(con.Cliente, p, longitudNombre, hoy) + extra;
                    if (texto.Length <= maximo)
                    {
                        return texto;
                    }
                }
            }
            string corto = Frase(con.Cliente, p, 0, hoy);
            return corto.Length > maximo ? corto.Substring(0, maximo) : corto;
        }

        private static string Frase(ClienteCarteraContacto cliente, ProductoEnOfertaCliente p, int longitudNombre, DateTime hoy)
        {
            string nombre = NombreCorto(p.Nombre, longitudNombre);
            string producto = string.IsNullOrEmpty(nombre) ? p.Producto : $"{p.Producto} ({nombre})";
            string compra = p.LeToca
                ? $"y lleva {p.DiasDesdeUltima} sin comprarlo"
                : $"y lo compró hace {p.DiasDesdeUltima} {(p.DiasDesdeUltima == 1 ? "día" : "días")}";
            string contacto = cliente.UltimoContacto.HasValue
                ? $"No hablamos con él desde el {Fecha(cliente.UltimoContacto.Value, hoy)}."
                : "No consta ningún contacto con él.";
            return $"Compra el {producto} cada {p.CicloDias} días {compra}; hoy tiene {p.TextoOferta}{TextoVigencia(p, hoy)}. {contacto}";
        }

        /// <summary>« (solo hasta el 30/11)» si caduca pronto, « (oferta nueva, desde el 12/11)» si acaba de entrar, « (hasta el 30/11)» o nada.</summary>
        public static string TextoVigencia(ProductoEnOfertaCliente p, DateTime hoy)
        {
            if (p.CaducaPronto && p.FechaHasta.HasValue)
            {
                return $" (solo hasta el {Fecha(p.FechaHasta.Value, hoy)})";
            }
            if (p.RecienEntrada && p.FechaDesde.HasValue)
            {
                return $" (oferta nueva, desde el {Fecha(p.FechaDesde.Value, hoy)})";
            }
            return p.FechaHasta.HasValue ? $" (hasta el {Fecha(p.FechaHasta.Value, hoy)})" : string.Empty;
        }

        public static string NombreCorto(string nombre, int longitud)
        {
            if (longitud <= 0 || string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            string limpio = string.Join(" ", nombre.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (limpio.Length <= longitud)
            {
                return limpio;
            }
            // Por una palabra entera si se puede.
            string cortado = limpio.Substring(0, longitud);
            int espacio = limpio[longitud] == ' ' ? longitud : cortado.LastIndexOf(' ');
            return (espacio > 0 ? cortado.Substring(0, espacio) : cortado).TrimEnd();
        }

        /// <summary>
        /// Corte 3: los <see cref="PRODUCTOS_APUNTADOS"/> mejores en JSON corto: [{"p":"45929","d":20,"c":45,"u":64,"t":1,"n":0,"f":1}]
        /// (producto, descuento %, ciclo, días desde la última, le toca, oferta nueva, caduca pronto). Null si no tiene ninguno.
        /// </summary>
        public static string ProductosJson(ClienteConOfertas con)
        {
            if (con?.Productos == null || !con.Productos.Any())
            {
                return null;
            }
            return JsonConvert.SerializeObject(con.Productos.Take(PRODUCTOS_APUNTADOS).Select(p => new
            {
                p = p.Producto,
                d = Porcentaje(p.Descuento),
                c = p.CicloDias,
                u = p.DiasDesdeUltima,
                t = p.LeToca ? 1 : 0,
                n = p.RecienEntrada ? 1 : 0,
                f = p.CaducaPronto ? 1 : 0
            }), Formatting.None);
        }

        public static int Porcentaje(decimal descuento) => (int)Math.Round(descuento * 100, MidpointRounding.AwayFromZero);

        private static string Fecha(DateTime fecha, DateTime hoy) => fecha.ToString(fecha.Year == hoy.Year ? "dd/MM" : "dd/MM/yyyy", es);

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
