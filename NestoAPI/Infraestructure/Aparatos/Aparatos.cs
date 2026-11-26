using NestoAPI.Infraestructure.Inmovilizados;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Aparatos
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#913, corte 1 (Carlos 26/11/26), SOLO LECTURA: la historia de un aparato (producto + número de serie) montada con lo
    // que ya existe (compras, ventas, registro de Alquileres, inmovilizados) y el informe de descuadres por aparato.
    // ------------------------------------------------------------------------------------------------------------------

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TipoMovimientoAparato
    {
        Compra,
        /// <summary>Línea de compra en negativo: el aparato se devolvió al proveedor.</summary>
        DevolucionProveedor,
        Venta,
        /// <summary>Línea de venta en negativo: el cliente lo devolvió.</summary>
        Abono,
        Inmovilizado,
        /// <summary>El registro de la ventana de Alquileres (informativo: no decide la situación).</summary>
        RegistroAlquiler
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SituacionAparato
    {
        EnStock,
        Vendido,
        Inmovilizado,
        Alquilado,
        DevueltoAlProveedor,
        SinRastro
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MotivoDescuadreAparato
    {
        SerieRepetidaEnElRegistro,
        RegistroEnStockPeroVendido,
        RegistroEnStockPeroInmovilizado,
        InmovilizadoSinEnlazar,
        RegistroConClienteSinVenta,
        RegistroConClientePeroDevuelto,
        VendidoSinRegistro
    }

    public class MovimientoAparatoDTO
    {
        public DateTime? Fecha { get; set; }
        public TipoMovimientoAparato Tipo { get; set; }
        /// <summary>«Pedido 930489 · albarán 731200 · factura NV2616046», «Pedido de compra 220931», «I/1824», «Registro 1934».</summary>
        public string Documento { get; set; }
        /// <summary>Cliente, proveedor o inmovilizado («22709», «P123», «I/1824»).</summary>
        public string Tercero { get; set; }
        public string NombreTercero { get; set; }
        public string Texto { get; set; }

        // Datos para la regla (no viajan)
        [JsonIgnore] public string Producto { get; set; }
        [JsonIgnore] public string NumeroSerie { get; set; }
        [JsonIgnore] public int? Pedido { get; set; }
        /// <summary>Inmovilizado anulado (Estado &lt; 0): sale en la historia pero no decide la situación.</summary>
        [JsonIgnore] public bool Anulado { get; set; }
    }

    public class RegistroAparatoDTO
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Producto { get; set; }
        public string Inmovilizado { get; set; }
        public int? Cuotas { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaSenal { get; set; }
        public decimal? ImporteSenal { get; set; }
        public string NumeroSerie { get; set; }
        public string NombreProducto { get; set; }
        public string Familia { get; set; }
        public decimal? Importe { get; set; }
        public int? CabPedidoVta { get; set; }
        public string RutaContrato { get; set; }
        public string Comentarios { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>Lo que dice el registro (sin cliente ni inmovilizado = en stock), sin mirar los movimientos.</summary>
        public SituacionAparato SituacionSegunRegistro => ReglaSituacionAparato.SegunRegistro(this);
    }

    public class HistoriaAparatoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string NumeroSerie { get; set; }
        public SituacionAparato Situacion { get; set; }
        public string TextoSituacion { get; set; }
        /// <summary>El registro de Alquileres (el más reciente si hay varios); null si no tiene.</summary>
        public RegistroAparatoDTO Registro { get; set; }
        /// <summary>Más de un registro con ese producto y serie: los números.</summary>
        public List<int> OtrosRegistros { get; set; } = new List<int>();
        /// <summary>La serie está en la tabla NumerosSerie (series «en stock» de Nesto viejo).</summary>
        public bool EnTablaNumerosSerie { get; set; }
        public List<MovimientoAparatoDTO> Movimientos { get; set; } = new List<MovimientoAparatoDTO>();
    }

    public class DescuadreAparatoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string NumeroSerie { get; set; }
        /// <summary>El motivo principal (el primero de <see cref="Motivos"/>).</summary>
        public MotivoDescuadreAparato Motivo { get; set; }
        public string TextoMotivo { get; set; }
        public List<MotivoDescuadreAparato> Motivos { get; set; } = new List<MotivoDescuadreAparato>();
        public SituacionAparato? SituacionSegunRegistro { get; set; }
        public SituacionAparato SituacionCalculada { get; set; }
        public string TextoSituacion { get; set; }
        public List<int> Registros { get; set; } = new List<int>();
    }

    public class StockAlmacenAparatoDTO
    {
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResumenProductoAparatosDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>ExtractoProducto de las empresas 1 y 3, por almacén (solo los que no dan 0).</summary>
        public List<StockAlmacenAparatoDTO> StockContable { get; set; } = new List<StockAlmacenAparatoDTO>();
        public int StockContableTotal { get; set; }
        /// <summary>Registros de Alquileres sin cliente ni inmovilizado (lo que contaba la ventana).</summary>
        public int EnStockSegunRegistro { get; set; }
        /// <summary>Aparatos cuya situación calculada es «en stock».</summary>
        public int EnStockSegunMovimientos { get; set; }
        /// <summary>EnStockSegunRegistro − EnStockSegunMovimientos.</summary>
        public int Diferencia { get; set; }
        /// <summary>StockContableTotal − EnStockSegunRegistro (la columna «Descuadre» de la ventana, con las empresas 1 y 3).</summary>
        public int DiferenciaStockContable { get; set; }
    }

    public class InformeDescuadresAparatosDTO
    {
        public bool SoloListaAlquileres { get; set; }
        public int TotalDescuadres { get; set; }
        public List<DescuadreAparatoDTO> Descuadres { get; set; } = new List<DescuadreAparatoDTO>();
        public List<ResumenProductoAparatosDTO> Productos { get; set; } = new List<ResumenProductoAparatosDTO>();
        public string Aviso { get; set; }
    }

    // ---------------------------------------------------------------- filas de la lectura

    public class ProductoAparatoFila
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public bool NecesitaNumSerie { get; set; }
        public bool EnListaAlquileres { get; set; }
    }

    public class VentaAparatoFila
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public string NumeroSerie { get; set; }
        public int Cantidad { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public int? Albaran { get; set; }
        public string Factura { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public string Texto { get; set; }
        public string Almacen { get; set; }
    }

    public class CompraAparatoFila
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public string NumeroSerie { get; set; }
        public int Cantidad { get; set; }
        public DateTime? Fecha { get; set; }
        public int? Albaran { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Texto { get; set; }
        public string Almacen { get; set; }
    }

    public class InmovilizadoAparatoFila
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public short Estado { get; set; }
        public DateTime? FechaCreacion { get; set; }
    }

    public class NumeroSerieFila
    {
        public string Producto { get; set; }
        public string NumeroSerie { get; set; }
    }

    public class StockAparatoFila
    {
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Todo lo leído (de un aparato o de todos los del alcance).</summary>
    public class DatosAparatos
    {
        public List<ProductoAparatoFila> Productos { get; set; } = new List<ProductoAparatoFila>();
        public List<RegistroAparatoDTO> Registros { get; set; } = new List<RegistroAparatoDTO>();
        public List<VentaAparatoFila> Ventas { get; set; } = new List<VentaAparatoFila>();
        public List<CompraAparatoFila> Compras { get; set; } = new List<CompraAparatoFila>();
        public List<InmovilizadoAparatoFila> Inmovilizados { get; set; } = new List<InmovilizadoAparatoFila>();
        public List<NumeroSerieFila> NumerosSerie { get; set; } = new List<NumeroSerieFila>();
        public List<StockAparatoFila> Stock { get; set; } = new List<StockAparatoFila>();
    }

    /// <summary>
    /// NestoAPI#913: LA REGLA (pura) de la situación de un aparato. Se calcula de los movimientos, nunca del registro: manda el último
    /// por fecha (a igual fecha, compra, devolución al proveedor, venta, abono, inmovilizado). Compra o abono → en stock (la última venta
    /// manda: vendido y después abonado vuelve a estar en stock); venta → vendido (alquilado si el registro tiene cuotas y es del mismo
    /// cliente); inmovilizado vivo → inmovilizado; devolución al proveedor → devuelto; nada → sin rastro. Las series se comparan sin los
    /// espacios de los extremos y sin mayúsculas.
    /// </summary>
    public static class ReglaSituacionAparato
    {
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        /// <summary>La clave de comparación de una serie (o de un producto): recortada y en mayúsculas; "" = sin serie.</summary>
        public static string Clave(string texto) => (texto ?? string.Empty).Trim().ToUpperInvariant();

        public static bool MismaSerie(string a, string b) => Clave(a).Length > 0 && Clave(a) == Clave(b);

        /// <summary>Lo que dice el registro: con inmovilizado, inmovilizado; con cliente, alquilado (con cuotas) o vendido; si no, en stock.</summary>
        public static SituacionAparato SegunRegistro(RegistroAparatoDTO registro)
        {
            if (registro == null)
            {
                return SituacionAparato.SinRastro;
            }
            if (!string.IsNullOrWhiteSpace(registro.Inmovilizado))
            {
                return SituacionAparato.Inmovilizado;
            }
            if (!string.IsNullOrWhiteSpace(registro.Cliente))
            {
                return (registro.Cuotas ?? 0) > 0 ? SituacionAparato.Alquilado : SituacionAparato.Vendido;
            }
            return SituacionAparato.EnStock;
        }

        private static int OrdenTipo(TipoMovimientoAparato tipo)
        {
            switch (tipo)
            {
                case TipoMovimientoAparato.Compra: return 0;
                case TipoMovimientoAparato.DevolucionProveedor: return 1;
                case TipoMovimientoAparato.Venta: return 2;
                case TipoMovimientoAparato.Abono: return 3;
                case TipoMovimientoAparato.Inmovilizado: return 4;
                default: return 5;
            }
        }

        /// <summary>Los movimientos en orden (fecha, y a igual fecha el orden natural de los tipos). Sin fecha, al principio.</summary>
        public static List<MovimientoAparatoDTO> Ordenar(IEnumerable<MovimientoAparatoDTO> movimientos)
        {
            return (movimientos ?? Enumerable.Empty<MovimientoAparatoDTO>())
                .Where(m => m != null)
                .OrderBy(m => m.Fecha ?? DateTime.MinValue)
                .ThenBy(m => OrdenTipo(m.Tipo))
                .ThenBy(m => m.Pedido ?? 0)
                .ToList();
        }

        /// <summary>El movimiento que decide la situación (el último que no es el registro ni un inmovilizado anulado), o null.</summary>
        public static MovimientoAparatoDTO UltimoQueDecide(IEnumerable<MovimientoAparatoDTO> movimientos)
        {
            return Ordenar(movimientos)
                .Where(m => m.Tipo != TipoMovimientoAparato.RegistroAlquiler && !(m.Tipo == TipoMovimientoAparato.Inmovilizado && m.Anulado))
                .LastOrDefault();
        }

        public static SituacionAparato Calcular(IEnumerable<MovimientoAparatoDTO> movimientos, RegistroAparatoDTO registro)
        {
            return Situacion(UltimoQueDecide(movimientos), registro);
        }

        private static SituacionAparato Situacion(MovimientoAparatoDTO ultimo, RegistroAparatoDTO registro)
        {
            if (ultimo == null)
            {
                return SituacionAparato.SinRastro;
            }
            switch (ultimo.Tipo)
            {
                case TipoMovimientoAparato.Compra:
                case TipoMovimientoAparato.Abono:
                    return SituacionAparato.EnStock;
                case TipoMovimientoAparato.DevolucionProveedor:
                    return SituacionAparato.DevueltoAlProveedor;
                case TipoMovimientoAparato.Inmovilizado:
                    return SituacionAparato.Inmovilizado;
                case TipoMovimientoAparato.Venta:
                    return registro != null && (registro.Cuotas ?? 0) > 0 && Clave(registro.Cliente) == Clave(ultimo.Tercero)
                        ? SituacionAparato.Alquilado
                        : SituacionAparato.Vendido;
                default:
                    return SituacionAparato.SinRastro;
            }
        }

        /// <summary>La situación en una frase, de usted.</summary>
        public static string Texto(IEnumerable<MovimientoAparatoDTO> movimientos, RegistroAparatoDTO registro)
        {
            MovimientoAparatoDTO ultimo = UltimoQueDecide(movimientos);
            SituacionAparato situacion = Situacion(ultimo, registro);
            string fecha = ultimo?.Fecha.HasValue == true ? " el " + ultimo.Fecha.Value.ToString("dd/MM/yyyy", ES) : string.Empty;
            string pedido = ultimo?.Pedido.HasValue == true ? $", pedido {ultimo.Pedido.Value}" : string.Empty;
            switch (situacion)
            {
                case SituacionAparato.Vendido:
                    return $"Vendido al cliente {Quien(ultimo)}{fecha}{pedido}.";
                case SituacionAparato.Alquilado:
                    return $"Alquilado al cliente {Quien(ultimo)} ({registro.Cuotas} cuotas en el registro {registro.Numero}){fecha}{pedido}.";
                case SituacionAparato.EnStock:
                    return ultimo.Tipo == TipoMovimientoAparato.Abono
                        ? $"En stock: el cliente {Quien(ultimo)} lo devolvió{fecha}{pedido}."
                        : $"En stock: comprado al proveedor {Quien(ultimo)}{fecha}{(ultimo.Pedido.HasValue ? $", pedido de compra {ultimo.Pedido.Value}" : string.Empty)}.";
                case SituacionAparato.Inmovilizado:
                    return $"Inmovilizado {ultimo.Tercero}{(ultimo.Fecha.HasValue ? " desde el " + ultimo.Fecha.Value.ToString("dd/MM/yyyy", ES) : string.Empty)}" +
                        $"{(string.IsNullOrWhiteSpace(ultimo.Texto) ? string.Empty : $" ({ultimo.Texto.Trim()})")}.";
                case SituacionAparato.DevueltoAlProveedor:
                    return $"Devuelto al proveedor {Quien(ultimo)}{fecha}{(ultimo.Pedido.HasValue ? $", pedido de compra {ultimo.Pedido.Value}" : string.Empty)}.";
                default:
                    return registro == null
                        ? "No hay ninguna compra, venta ni inmovilizado con este número de serie."
                        : $"No hay ninguna compra, venta ni inmovilizado con este número de serie; el registro {registro.Numero} de Alquileres dice: {TextoSegunRegistro(registro)}.";
            }
        }

        /// <summary>Lo que dice el registro, en pocas palabras.</summary>
        public static string TextoSegunRegistro(RegistroAparatoDTO registro)
        {
            switch (SegunRegistro(registro))
            {
                case SituacionAparato.Inmovilizado:
                    return $"inmovilizado {CodigoInmovilizado.Componer(registro.Inmovilizado) ?? registro.Inmovilizado.Trim()}";
                case SituacionAparato.Alquilado:
                    return $"alquilado al cliente {registro.Cliente?.Trim()} ({registro.Cuotas} cuotas)";
                case SituacionAparato.Vendido:
                    return $"vendido al cliente {registro.Cliente?.Trim()}";
                case SituacionAparato.EnStock:
                    return "en stock";
                default:
                    return "sin registro";
            }
        }

        private static string Quien(MovimientoAparatoDTO m)
        {
            string tercero = m?.Tercero?.Trim();
            string nombre = m?.NombreTercero?.Trim();
            return string.IsNullOrEmpty(nombre) ? tercero : $"{tercero} ({nombre})";
        }
    }

    /// <summary>
    /// NestoAPI#913: de las filas leídas a los movimientos de cada aparato (pura). Ventas en positivo = venta, en negativo = abono;
    /// compras en positivo = compra, en negativo = devolución al proveedor; un inmovilizado es de un aparato si su descripción lleva el
    /// número de serie («n/s …», <see cref="ReglaDescripcionEtiquetaInmovilizado.SerieEscrita"/>) y, si lleva «ref.», que sea ese
    /// producto; sin «ref.», solo si la serie es de UN único producto de los leídos.
    /// </summary>
    public static class ConstructorMovimientosAparato
    {
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        public static List<MovimientoAparatoDTO> Movimientos(DatosAparatos datos)
        {
            var movimientos = new List<MovimientoAparatoDTO>();
            if (datos == null)
            {
                return movimientos;
            }
            foreach (VentaAparatoFila v in datos.Ventas ?? new List<VentaAparatoFila>())
            {
                if (v.Cantidad == 0 || ReglaSituacionAparato.Clave(v.NumeroSerie).Length == 0)
                {
                    continue;
                }
                var documento = new List<string> { $"Pedido {v.Pedido}" };
                if (v.Albaran.HasValue)
                {
                    documento.Add($"albarán {v.Albaran.Value}");
                }
                if (!string.IsNullOrWhiteSpace(v.Factura))
                {
                    documento.Add($"factura {v.Factura.Trim()}");
                }
                movimientos.Add(new MovimientoAparatoDTO
                {
                    Fecha = v.FechaAlbaran,
                    Tipo = v.Cantidad > 0 ? TipoMovimientoAparato.Venta : TipoMovimientoAparato.Abono,
                    Documento = string.Join(" · ", documento),
                    Tercero = v.Cliente?.Trim(),
                    NombreTercero = v.NombreCliente?.Trim(),
                    Texto = $"{(v.Cantidad > 0 ? "Venta" : "Abono")} de {Math.Abs(v.Cantidad)} desde {v.Almacen?.Trim()}" +
                        (string.IsNullOrWhiteSpace(v.Empresa) || v.Empresa.Trim() == "1" ? string.Empty : $" (empresa {v.Empresa.Trim()})"),
                    Producto = v.Producto?.Trim(),
                    NumeroSerie = v.NumeroSerie?.Trim(),
                    Pedido = v.Pedido
                });
            }
            foreach (CompraAparatoFila c in datos.Compras ?? new List<CompraAparatoFila>())
            {
                if (c.Cantidad == 0 || ReglaSituacionAparato.Clave(c.NumeroSerie).Length == 0)
                {
                    continue;
                }
                movimientos.Add(new MovimientoAparatoDTO
                {
                    Fecha = c.Fecha,
                    Tipo = c.Cantidad > 0 ? TipoMovimientoAparato.Compra : TipoMovimientoAparato.DevolucionProveedor,
                    Documento = $"Pedido de compra {c.Pedido}{(c.Albaran.HasValue ? $" · albarán {c.Albaran.Value}" : string.Empty)}",
                    Tercero = c.Proveedor?.Trim(),
                    NombreTercero = c.NombreProveedor?.Trim(),
                    Texto = $"{(c.Cantidad > 0 ? "Compra" : "Devolución al proveedor")} de {Math.Abs(c.Cantidad)} en {c.Almacen?.Trim()}",
                    Producto = c.Producto?.Trim(),
                    NumeroSerie = c.NumeroSerie?.Trim(),
                    Pedido = c.Pedido
                });
            }
            foreach (RegistroAparatoDTO r in datos.Registros ?? new List<RegistroAparatoDTO>())
            {
                if (ReglaSituacionAparato.Clave(r.NumeroSerie).Length == 0)
                {
                    continue;
                }
                movimientos.Add(new MovimientoAparatoDTO
                {
                    Fecha = r.FechaEntrega ?? r.FechaModificacion,
                    Tipo = TipoMovimientoAparato.RegistroAlquiler,
                    Documento = $"Registro {r.Numero}",
                    Tercero = r.Cliente?.Trim(),
                    NombreTercero = r.NombreCliente?.Trim(),
                    Texto = "Registro de Alquileres: " + ReglaSituacionAparato.TextoSegunRegistro(r)
                        + (r.CabPedidoVta.HasValue ? $", pedido {r.CabPedidoVta.Value}" : string.Empty),
                    Producto = r.Producto?.Trim(),
                    NumeroSerie = r.NumeroSerie?.Trim()
                });
            }
            movimientos.AddRange(MovimientosInmovilizado(datos, movimientos));
            return movimientos;
        }

        private static IEnumerable<MovimientoAparatoDTO> MovimientosInmovilizado(DatosAparatos datos, List<MovimientoAparatoDTO> conocidos)
        {
            HashSet<string> productosLeidos = new HashSet<string>((datos.Productos ?? new List<ProductoAparatoFila>())
                .Select(p => ReglaSituacionAparato.Clave(p.Producto)), StringComparer.Ordinal);
            // Serie → productos en que aparece (para el inmovilizado sin «ref.»)
            Dictionary<string, HashSet<string>> productosDeSerie = conocidos
                .GroupBy(m => ReglaSituacionAparato.Clave(m.NumeroSerie))
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(m => ReglaSituacionAparato.Clave(m.Producto)), StringComparer.Ordinal), StringComparer.Ordinal);

            foreach (InmovilizadoAparatoFila i in datos.Inmovilizados ?? new List<InmovilizadoAparatoFila>())
            {
                string serie = ReglaDescripcionEtiquetaInmovilizado.SerieEscrita(i.Descripcion);
                if (serie == null)
                {
                    continue;
                }
                string claveSerie = ReglaSituacionAparato.Clave(serie);
                string referencia = ReglaSituacionAparato.Clave(ReglaDescripcionEtiquetaInmovilizado.ReferenciaEscrita(i.Descripcion));
                string producto;
                if (referencia.Length > 0)
                {
                    if (!productosLeidos.Contains(referencia))
                    {
                        continue;
                    }
                    producto = referencia;
                }
                else if (productosDeSerie.TryGetValue(claveSerie, out HashSet<string> candidatos) && candidatos.Count == 1)
                {
                    producto = candidatos.First();
                }
                else if (productosLeidos.Count == 1)
                {
                    // Historia de un aparato: solo se ha leído ese producto (y la descripción ya lleva su serie).
                    producto = productosLeidos.First();
                }
                else
                {
                    continue;
                }
                string codigo = CodigoInmovilizado.Componer(i.Numero);
                yield return new MovimientoAparatoDTO
                {
                    Fecha = i.FechaCreacion,
                    Tipo = TipoMovimientoAparato.Inmovilizado,
                    Documento = codigo,
                    Tercero = codigo,
                    Texto = (i.Descripcion?.Trim() ?? string.Empty) + (i.Estado < 0 ? " (anulado)" : string.Empty),
                    Producto = producto,
                    NumeroSerie = serie,
                    Anulado = i.Estado < 0
                };
            }
        }

        internal static string Fecha(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy", ES);
    }

    /// <summary>NestoAPI#913: la historia de un aparato con lo leído (pura). Null si no hay rastro en ningún sitio.</summary>
    public static class ReglaHistoriaAparato
    {
        public static HistoriaAparatoDTO Construir(string producto, string numeroSerie, DatosAparatos datos)
        {
            string claveProducto = ReglaSituacionAparato.Clave(producto);
            string claveSerie = ReglaSituacionAparato.Clave(numeroSerie);
            List<MovimientoAparatoDTO> movimientos = ConstructorMovimientosAparato.Movimientos(datos)
                .Where(m => ReglaSituacionAparato.Clave(m.Producto) == claveProducto && ReglaSituacionAparato.Clave(m.NumeroSerie) == claveSerie)
                .ToList();
            List<RegistroAparatoDTO> registros = (datos?.Registros ?? new List<RegistroAparatoDTO>())
                .Where(r => ReglaSituacionAparato.Clave(r.Producto) == claveProducto && ReglaSituacionAparato.Clave(r.NumeroSerie) == claveSerie)
                .OrderByDescending(r => r.Numero)
                .ToList();
            bool enTabla = (datos?.NumerosSerie ?? new List<NumeroSerieFila>())
                .Any(n => ReglaSituacionAparato.Clave(n.Producto) == claveProducto && ReglaSituacionAparato.Clave(n.NumeroSerie) == claveSerie);
            if (!movimientos.Any() && !registros.Any() && !enTabla)
            {
                return null;
            }
            RegistroAparatoDTO registro = registros.FirstOrDefault();
            ProductoAparatoFila ficha = (datos?.Productos ?? new List<ProductoAparatoFila>())
                .FirstOrDefault(p => ReglaSituacionAparato.Clave(p.Producto) == claveProducto);
            return new HistoriaAparatoDTO
            {
                Producto = producto?.Trim(),
                Nombre = ficha?.Nombre?.Trim() ?? registro?.NombreProducto?.Trim(),
                NumeroSerie = numeroSerie?.Trim(),
                Situacion = ReglaSituacionAparato.Calcular(movimientos, registro),
                TextoSituacion = ReglaSituacionAparato.Texto(movimientos, registro),
                Registro = registro,
                OtrosRegistros = registros.Skip(1).Select(r => r.Numero).ToList(),
                EnTablaNumerosSerie = enTabla,
                Movimientos = ReglaSituacionAparato.Ordenar(movimientos)
            };
        }
    }

    /// <summary>
    /// NestoAPI#913: LA REGLA (pura) del informe de descuadres por aparato (producto + serie): la situación calculada de los
    /// movimientos frente a la que dice el registro; solo los que no coinciden, con sus motivos, y el resumen por producto.
    /// </summary>
    public static class ReglaDescuadresAparatos
    {
        public const int MAXIMO_FILAS = 2000;

        public static InformeDescuadresAparatosDTO Calcular(DatosAparatos datos, bool soloListaAlquileres)
        {
            datos = datos ?? new DatosAparatos();
            Dictionary<string, ProductoAparatoFila> fichas = (datos.Productos ?? new List<ProductoAparatoFila>())
                .GroupBy(p => ReglaSituacionAparato.Clave(p.Producto))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            List<MovimientoAparatoDTO> movimientos = ConstructorMovimientosAparato.Movimientos(datos)
                .Where(m => fichas.ContainsKey(ReglaSituacionAparato.Clave(m.Producto)))
                .ToList();
            List<RegistroAparatoDTO> registros = (datos.Registros ?? new List<RegistroAparatoDTO>())
                .Where(r => fichas.ContainsKey(ReglaSituacionAparato.Clave(r.Producto)))
                .ToList();

            var claves = new SortedSet<(string Producto, string Serie)>(
                movimientos.Select(m => (ReglaSituacionAparato.Clave(m.Producto), ReglaSituacionAparato.Clave(m.NumeroSerie)))
                    .Where(c => c.Item2.Length > 0));
            ILookup<(string, string), MovimientoAparatoDTO> porAparato = movimientos
                .ToLookup(m => (ReglaSituacionAparato.Clave(m.Producto), ReglaSituacionAparato.Clave(m.NumeroSerie)));
            ILookup<(string, string), RegistroAparatoDTO> registrosPorAparato = registros
                .ToLookup(r => (ReglaSituacionAparato.Clave(r.Producto), ReglaSituacionAparato.Clave(r.NumeroSerie)));

            var descuadres = new List<DescuadreAparatoDTO>();
            var enStockMovimientos = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach ((string producto, string serie) clave in claves)
            {
                List<MovimientoAparatoDTO> deEste = porAparato[clave].ToList();
                List<RegistroAparatoDTO> regs = registrosPorAparato[clave].OrderByDescending(r => r.Numero).ToList();
                RegistroAparatoDTO registro = regs.FirstOrDefault();
                SituacionAparato calculada = ReglaSituacionAparato.Calcular(deEste, registro);
                if (calculada == SituacionAparato.EnStock)
                {
                    enStockMovimientos[clave.producto] = (enStockMovimientos.TryGetValue(clave.producto, out int n) ? n : 0) + 1;
                }
                List<MotivoDescuadreAparato> motivos = Motivos(deEste, regs, calculada, fichas[clave.producto].NecesitaNumSerie);
                if (!motivos.Any())
                {
                    continue;
                }
                ProductoAparatoFila ficha = fichas[clave.producto];
                descuadres.Add(new DescuadreAparatoDTO
                {
                    Producto = ficha.Producto?.Trim(),
                    Nombre = ficha.Nombre?.Trim(),
                    NumeroSerie = deEste.Select(m => m.NumeroSerie).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? clave.serie,
                    Motivo = motivos[0],
                    Motivos = motivos,
                    TextoMotivo = TextoMotivos(motivos, regs, deEste),
                    SituacionSegunRegistro = registro == null ? (SituacionAparato?)null : ReglaSituacionAparato.SegunRegistro(registro),
                    SituacionCalculada = calculada,
                    TextoSituacion = ReglaSituacionAparato.Texto(deEste, registro),
                    Registros = regs.Select(r => r.Numero).OrderBy(x => x).ToList()
                });
            }

            var informe = new InformeDescuadresAparatosDTO
            {
                SoloListaAlquileres = soloListaAlquileres,
                TotalDescuadres = descuadres.Count,
                Descuadres = descuadres.Take(MAXIMO_FILAS).ToList(),
                Productos = Resumen(fichas, registros, enStockMovimientos, datos.Stock ?? new List<StockAparatoFila>(), claves)
            };
            if (descuadres.Count > MAXIMO_FILAS)
            {
                informe.Aviso = $"Hay {descuadres.Count.ToString("N0", new CultureInfo("es-ES"))} aparatos con descuadre: se enseñan los {MAXIMO_FILAS.ToString("N0", new CultureInfo("es-ES"))} primeros (por producto y número de serie).";
            }
            return informe;
        }

        /// <summary>Por qué no coincide (en orden de importancia); vacía si coincide.</summary>
        public static List<MotivoDescuadreAparato> Motivos(List<MovimientoAparatoDTO> movimientos, List<RegistroAparatoDTO> registros,
            SituacionAparato calculada, bool necesitaNumSerie)
        {
            var motivos = new List<MotivoDescuadreAparato>();
            RegistroAparatoDTO registro = registros?.FirstOrDefault();
            bool hayVenta = movimientos.Any(m => m.Tipo == TipoMovimientoAparato.Venta);
            bool hayInmovilizadoVivo = movimientos.Any(m => m.Tipo == TipoMovimientoAparato.Inmovilizado && !m.Anulado);
            if (registros != null && registros.Count > 1)
            {
                motivos.Add(MotivoDescuadreAparato.SerieRepetidaEnElRegistro);
            }
            if (registro != null)
            {
                SituacionAparato segunRegistro = ReglaSituacionAparato.SegunRegistro(registro);
                if (segunRegistro == SituacionAparato.EnStock && calculada == SituacionAparato.Vendido)
                {
                    motivos.Add(MotivoDescuadreAparato.RegistroEnStockPeroVendido);
                }
                if (segunRegistro == SituacionAparato.EnStock && calculada == SituacionAparato.Inmovilizado)
                {
                    motivos.Add(MotivoDescuadreAparato.RegistroEnStockPeroInmovilizado);
                }
                if (string.IsNullOrWhiteSpace(registro.Inmovilizado) && hayInmovilizadoVivo)
                {
                    motivos.Add(MotivoDescuadreAparato.InmovilizadoSinEnlazar);
                }
                if (!string.IsNullOrWhiteSpace(registro.Cliente) && !hayVenta)
                {
                    motivos.Add(MotivoDescuadreAparato.RegistroConClienteSinVenta);
                }
                if (!string.IsNullOrWhiteSpace(registro.Cliente) && hayVenta && calculada == SituacionAparato.EnStock)
                {
                    motivos.Add(MotivoDescuadreAparato.RegistroConClientePeroDevuelto);
                }
            }
            else if (necesitaNumSerie && calculada == SituacionAparato.Vendido)
            {
                motivos.Add(MotivoDescuadreAparato.VendidoSinRegistro);
            }
            return motivos;
        }

        public static string TextoMotivo(MotivoDescuadreAparato motivo, List<RegistroAparatoDTO> registros, List<MovimientoAparatoDTO> movimientos)
        {
            RegistroAparatoDTO registro = registros?.FirstOrDefault();
            MovimientoAparatoDTO inmovilizado = movimientos?.LastOrDefault(m => m.Tipo == TipoMovimientoAparato.Inmovilizado && !m.Anulado);
            switch (motivo)
            {
                case MotivoDescuadreAparato.SerieRepetidaEnElRegistro:
                    return $"La serie está en {registros.Count} registros de Alquileres ({string.Join(", ", registros.Select(r => r.Numero).OrderBy(x => x))}).";
                case MotivoDescuadreAparato.RegistroEnStockPeroVendido:
                    return $"El registro {registro.Numero} dice que está en stock, pero está vendido.";
                case MotivoDescuadreAparato.RegistroEnStockPeroInmovilizado:
                    return $"El registro {registro.Numero} dice que está en stock, pero está inmovilizado ({inmovilizado?.Tercero}).";
                case MotivoDescuadreAparato.InmovilizadoSinEnlazar:
                    return $"El inmovilizado {inmovilizado?.Tercero} nombra esta serie, pero el registro {registro.Numero} no tiene puesto el inmovilizado.";
                case MotivoDescuadreAparato.RegistroConClienteSinVenta:
                    return $"El registro {registro.Numero} tiene el cliente {registro.Cliente?.Trim()}, pero no hay ninguna venta con esta serie.";
                case MotivoDescuadreAparato.RegistroConClientePeroDevuelto:
                    return $"El registro {registro.Numero} tiene el cliente {registro.Cliente?.Trim()}, pero la última venta se abonó: está en stock.";
                case MotivoDescuadreAparato.VendidoSinRegistro:
                    return "Vendido con número de serie y sin registro en Alquileres.";
                default:
                    return motivo.ToString();
            }
        }

        private static string TextoMotivos(List<MotivoDescuadreAparato> motivos, List<RegistroAparatoDTO> registros, List<MovimientoAparatoDTO> movimientos)
            => string.Join(" ", motivos.Select(m => TextoMotivo(m, registros, movimientos)));

        private static List<ResumenProductoAparatosDTO> Resumen(Dictionary<string, ProductoAparatoFila> fichas, List<RegistroAparatoDTO> registros,
            Dictionary<string, int> enStockMovimientos, List<StockAparatoFila> stock, SortedSet<(string Producto, string Serie)> claves)
        {
            ILookup<string, StockAparatoFila> stockPorProducto = stock.Where(s => s.Cantidad != 0).ToLookup(s => ReglaSituacionAparato.Clave(s.Producto));
            var conAlgo = new HashSet<string>(claves.Select(c => c.Producto), StringComparer.Ordinal);
            conAlgo.UnionWith(registros.Select(r => ReglaSituacionAparato.Clave(r.Producto)));
            conAlgo.UnionWith(stockPorProducto.Select(g => g.Key));
            return conAlgo
                .Where(fichas.ContainsKey)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p =>
                {
                    List<StockAlmacenAparatoDTO> porAlmacen = stockPorProducto[p]
                        .GroupBy(s => s.Almacen?.Trim())
                        .Select(g => new StockAlmacenAparatoDTO { Almacen = g.Key, Cantidad = g.Sum(s => s.Cantidad) })
                        .Where(s => s.Cantidad != 0)
                        .OrderBy(s => s.Almacen, StringComparer.Ordinal)
                        .ToList();
                    int total = porAlmacen.Sum(s => s.Cantidad);
                    int segunRegistro = registros.Count(r => ReglaSituacionAparato.Clave(r.Producto) == p
                        && ReglaSituacionAparato.SegunRegistro(r) == SituacionAparato.EnStock);
                    int segunMovimientos = enStockMovimientos.TryGetValue(p, out int n) ? n : 0;
                    return new ResumenProductoAparatosDTO
                    {
                        Producto = fichas[p].Producto?.Trim(),
                        Nombre = fichas[p].Nombre?.Trim(),
                        StockContable = porAlmacen,
                        StockContableTotal = total,
                        EnStockSegunRegistro = segunRegistro,
                        EnStockSegunMovimientos = segunMovimientos,
                        Diferencia = segunRegistro - segunMovimientos,
                        DiferenciaStockContable = total - segunRegistro
                    };
                })
                .ToList();
        }
    }
}
